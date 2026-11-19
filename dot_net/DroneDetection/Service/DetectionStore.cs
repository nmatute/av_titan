using DroneDetection.Models;

namespace DroneDetection.Services;

/// <summary>
/// Keeps everything received so far and works out the numbers for the dashboard.
/// It's all in memory, so it resets when the server restarts.
/// </summary>
public class DetectionStore
{
    private int WindowSeconds = 300;   // the histogram covers the last 5 minutes
    private int RssiMin = -110;        // the histogram starts at -110
    private int BinSize = 5;           // each bin is 5 dB wide
    private int NumberOfBins = 9;      // 9 bins of 5: (-110..-65)

    private readonly TimeProvider _clock;                  // gives the current time. Tests pass in a fake one.
    private readonly object _lock = new object();          // requests can arrive at the same moment, so they take turns

    private int _messageCount = 0;                                                                // how many messages have been received
    private List<Detection> _active = new List<Detection>();                                      // the drones in the latest message
    private readonly Dictionary<string, Detection> _firstSightings = new Dictionary<string, Detection>();   // drone_id -> the first time we saw that drone
    private readonly List<Reading> _readings = new List<Reading>();                               // a reading for every drone in every message

    public DetectionStore(TimeProvider clock)
    {
        _clock = clock;
    }

    // Store one message (already checked by the model validation).
    public void AddMessage(DetectionMessage message)
    {
        lock (_lock)
        {
            _messageCount++;

            // "Active" drones are the ones in the latest message.
            _active = message.Detections;

            foreach (Detection detection in message.Detections)
            {
                // Only remember a drone the first time we see it, so it counts once
                if (!_firstSightings.ContainsKey(detection.DroneId))
                {
                    _firstSightings.Add(detection.DroneId, detection);
                }

                _readings.Add(new Reading { Timestamp = message.Timestamp, Rssi = detection.Rssi });
            }

            // Throw away readings older than 5 minutes
            long cutoff = Now() - WindowSeconds;
            // Throw away readings older than 5 minutes (the oldest are at the front).
            while (_readings.Count > 0 && _readings[0].Timestamp < cutoff)
            {
                _readings.RemoveAt(0);
            }
        }
    }

    // Everything the dashboard needs.
    public StateResponse GetState()
    {
        lock (_lock)
        {
            // Count each distinct drone once, by band and by classification.
            var bands = new Dictionary<string, int>();
            var classes = new Dictionary<string, int>();
            foreach (Detection drone in _firstSightings.Values)
            {
                if (!bands.ContainsKey(drone.Band))
                {
                    bands[drone.Band] = 0;
                }
                bands[drone.Band]++;

                if (!classes.ContainsKey(drone.Classification))
                {
                    classes[drone.Classification] = 0;
                }
                classes[drone.Classification]++;
            }

            return new StateResponse
            {
                MessageCount = _messageCount,
                Active = _active,
                Bands = bands,
                Classes = classes,
                Histogram = BuildHistogram(),
                ServerTime = Now(),   // the dashboard uses this to line up its clock with the server's
            };
        }
    }

    // Forget everything (the dashboard's Clear button).
    public void Clear()
    {
        lock (_lock)
        {
            _messageCount = 0;
            _active = new List<Detection>();
            _firstSightings.Clear();
            _readings.Clear();
        }
    }

    // The current time in seconds since 1970.
    private long Now()
    {
        return _clock.GetUtcNow().ToUnixTimeSeconds();
    }

    // Count how many readings from the last 5 minutes fall into each 5 dB bin.
    // Only called from GetState, which already holds the lock.
    private List<HistogramBin> BuildHistogram()
    {
        long cutoff = Now() - WindowSeconds;
        var counts = new int[NumberOfBins];

        foreach (Reading reading in _readings)
        {
            // only look at readings from the last 5 minutes
            if (reading.Timestamp >= cutoff)
            {
                int index = (int)Math.Floor((reading.Rssi - RssiMin) / BinSize);   // for example -87 -> bin 4

                if (index > NumberOfBins - 1)
                {
                    index = NumberOfBins - 1;   // EDGE CASE: -65 gives 9, so it goes in the last bin (8)
                }

                // increment the count for that bin
                counts[index]++;
            }
        }

        // 9 bins: -110 to -105, -105 to -100, ... -70 to -65
        var bins = new List<HistogramBin>();
        for (int i = 0; i < NumberOfBins; i++)
        {
            int low = RssiMin + i * BinSize; //low = start + (how many bins to skip) × (width of each bin)
            bins.Add(new HistogramBin { Low = low, High = low + BinSize, Count = counts[i] });
        }
        return bins;
    }
}
namespace DroneSimulator;

/// <summary>
/// Pretends to be the detection service: keeps track of the drones that are "in the air"
/// and builds the next message each time it is asked.
/// It does no networking, it only builds messages.
/// </summary>
public class DetectionSimulator
{
    // ---- Constants ---------------------------------------------------------
    private static readonly string[] Bands = { "2.4GHz", "5.8GHz" };                    // each drone gets one, chosen once
    private static readonly string[] Classifications = { "OcuSync", "Lightbridge" };    // each drone gets one, chosen once
    private const double SurvivalRate = 0.8;    // chance a drone shows up again in the next message (80%)
    private const int MaxSpawn = 2;             // most new drones that can appear in one message

    // A drone the simulator is keeping track of. Everything but Rssi stays the same for its whole life.
    private class TrackedDrone
    {
        public required string DroneId { get; init; }
        public required long StartTime { get; init; }
        public required string Band { get; init; }
        public required string Classification { get; init; }
        public required double Rssi { get; set; }
    }

    private readonly Random _random;
    private readonly RandomValues _values;                               // makes the ids and rssi numbers (see RandomValues.cs)
    private readonly Dictionary<string, TrackedDrone> _active = new();   // drone_id -> the drones currently "in the air"

    /// <summary>
    /// Pass a seeded Random (new Random(5)) in tests so the output is repeatable.
    /// With no argument it uses a normal random one.
    /// </summary>
    public DetectionSimulator(Random? random = null)
    {
        if (random == null)
        {
            random = new Random();
        }
        _random = random;
        _values = new RandomValues(random);   // shares the same Random, so a seeded run stays repeatable
    }

    /// <summary>
    /// Build and return the next detections message. Steps:
    ///   1. Each existing drone has a 20% chance to be gone for good. The ones that stay get a new rssi.
    ///   2. Spawn 0 to MaxSpawn new drones, with start_time = this timestamp.
    ///   3. List every active drone in the message.
    ///   4. Return the message
    /// </summary>
    /// <param name="timestamp">The time to put on the message, in seconds since 1970.</param>
    public DetectionMessage GenerateMessage(long timestamp)
    {
        /*
        * Step 1: each existing drone has an 80% chance to appear in this message.
        */
        foreach (string droneId in _active.Keys.ToList()) // ToList() copies the ids first, because we may remove drones while looping
        {
            if (_random.NextDouble() >= SurvivalRate)
            {
                _active.Remove(droneId);
            }
            else
            {
                TrackedDrone drone = _active[droneId];
                drone.Rssi = _values.NextRssi(drone.Rssi);
            }
        }

        /*
        * Step 2: new drones appear.
        */
        int spawn = _random.Next(0, MaxSpawn + 1);   // 0, 1 or 2 (the top number is not included)
        if (_active.Count == 0 && spawn == 0)
        {
            spawn = 1;   // never send an empty message
        }

        // Create one new drone for each spawn.
        for (int i = 0; i < spawn; i++) 
        {
            string droneId = _values.NewUniqueId();
            _active[droneId] = new TrackedDrone
            {
                DroneId = droneId,
                StartTime = timestamp,
                Band = Bands[_random.Next(Bands.Length)],
                Classification = Classifications[_random.Next(Classifications.Length)],
                Rssi = _values.FirstRssi(),
            };
        }

        /*
        * Step 3: copy each active drone into the message, so later messages don't change this one.
        */
        var readings = new List<DetectionReading>();
        foreach (TrackedDrone drone in _active.Values)
        {
            readings.Add(new DetectionReading
            {
                DroneId = drone.DroneId,
                StartTime = drone.StartTime,
                Band = drone.Band,
                Classification = drone.Classification,
                Rssi = drone.Rssi,
            });
        }

        /*
        * Step 4: Return the detections message
        */
        return new DetectionMessage
        {
            MsgId = _values.NewUniqueId(),
            Detections = readings,
            Timestamp = timestamp.ToString(),
        };
    }
}
namespace DroneDetection.Models;

/// <summary>
/// One 5 dB bucket: how many rssi readings fell between Low and High.
/// </summary>
public class HistogramBin
{
    public int Low { get; init; }
    public int High { get; init; }
    public int Count { get; init; }
}

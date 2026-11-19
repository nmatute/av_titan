namespace DroneDetection.Models;

/// <summary>
/// The JSON returned by GET /api/state
/// </summary>
public class StateResponse
{
    public int MessageCount { get; init; }
    public List<Detection> Active { get; init; } = new();
    public Dictionary<string, int> Bands { get; init; } = new();
    public Dictionary<string, int> Classes { get; init; } = new();
    public List<HistogramBin> Histogram { get; init; } = new();
    public long ServerTime { get; init; }
}
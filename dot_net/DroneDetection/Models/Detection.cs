using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DroneDetection.Models;

/// <summary>
/// One drone's reading inside a message.
/// </summary>
/// <remarks>
/// "required" means the JSON must contain the field, otherwise the request is rejected with a 400.
/// [Required] additionally rejects empty or whitespace-only text.
/// [JsonPropertyName] is the name the simulator uses in the JSON (drone_id, not DroneId).
/// </remarks>
public class Detection
{
    [JsonPropertyName("drone_id")]
    [Required]
    public required string DroneId { get; init; }

    [JsonPropertyName("start_time")]
    public required long StartTime { get; init; }

    [JsonPropertyName("band")]
    [Required]
    public required string Band { get; init; }

    [JsonPropertyName("classification")]
    [Required]
    public required string Classification { get; init; }

    [JsonPropertyName("rssi")]
    [Range(-110.0, -65.0)]      // Histogram bins are -110 to -65, so reject anything outside that range.
    public required double Rssi { get; init; }
}

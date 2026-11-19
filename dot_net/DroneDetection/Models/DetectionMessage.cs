using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DroneDetection.Models;

/// <summary>
/// One POSTed message: an id, a timestamp and the drones active right now.
/// Extra fields in the JSON (like "system") are ignored.
/// </summary>
public class DetectionMessage
{
    [JsonPropertyName("msg_type")]
    [Required]
    [AllowedValues("detections")]   // anything else is rejected
    public required string MsgType { get; init; }

    [JsonPropertyName("msg_id")]
    [Required]
    public required string MsgId { get; init; }

    // The spec sends this as a string ("1712010300"). Program.cs turns on
    // AllowReadingFromString, so a string or a plain number both work.
    [JsonPropertyName("timestamp")]
    public required long Timestamp { get; init; }

    [JsonPropertyName("detections")]
    public required List<Detection> Detections { get; init; }
}

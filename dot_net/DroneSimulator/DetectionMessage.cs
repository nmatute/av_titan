using System.Text.Json.Serialization;

namespace DroneSimulator;

//Whole message the simulator sends to the API each time.It’s one envelope that holds everything for that moment:

/// <summary>
/// The message POSTed to the API. [JsonPropertyName] sets the exact names the API expects
/// (msg_type, not MsgType).
/// </summary>
public class DetectionMessage
{
    [JsonPropertyName("msg_type")]
    public string MsgType { get; init; } = "detections";   

    [JsonPropertyName("system")]
    public string SystemName { get; init; } = "detection_service";  

    [JsonPropertyName("msg_id")]
    public required string MsgId { get; init; }         

    [JsonPropertyName("detections")]
    public required List<DetectionReading> Detections { get; init; }

    [JsonPropertyName("timestamp")]
    public required string Timestamp { get; init; }        
}

/// <summary>
/// One drone's reading inside a message.
/// </summary>
public class DetectionReading
{
    [JsonPropertyName("drone_id")]
    public required string DroneId { get; init; }

    [JsonPropertyName("start_time")]
    public required long StartTime { get; init; }          // when this drone first appeared

    [JsonPropertyName("band")]
    public required string Band { get; init; }             // "2.4GHz" or "5.8GHz"

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }   // "OcuSync" or "Lightbridge"

    [JsonPropertyName("rssi")]
    public required double Rssi { get; init; }
}

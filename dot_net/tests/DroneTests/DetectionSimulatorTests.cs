using System.Text.Json;
using DroneSimulator;

namespace DroneTests;

// Tests for DetectionSimulator, the class that makes the fake messages.
// Both tests give it a seeded Random (42), so every run produces exactly the same drones.
public class DetectionSimulatorTests
{
    // The timestamp (in seconds) of the first message. This is the example time from the spec.
    private const long FirstTimestamp = 1712010300;

    // CHECKS: the message that goes over the wire has exactly the field names and values the
    //         spec asks for. It looks at the real JSON text, because that is what the API receives.
    [Fact]
    public void GenerateMessage_SerializesExpectedApiFields()
    {
        // Random(42) is the seed: the same seed always gives the same "random" numbers.
        var simulator = new DetectionSimulator(new Random(42));
        var message = simulator.GenerateMessage(FirstTimestamp);
        // Turn the message into JSON text (like the simulator does when it POSTs),
        // then read that text back so we can check the field names (msg_type, drone_id, ...).
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(message));
        var payload = document.RootElement;

        // The fixed fields from the spec.
        Assert.Equal("detections", payload.GetProperty("msg_type").GetString());
        Assert.Equal("detection_service", payload.GetProperty("system").GetString());
        // The timestamp must be a string ("1712010300" in quotes), not a number.
        Assert.Equal("1712010300", payload.GetProperty("timestamp").GetString());
        // msg_id is 8 letters (the "!" tells C# the value is not null).
        Assert.Matches("^[a-zA-Z]{8}$", payload.GetProperty("msg_id").GetString()!);

        // Check every drone in the message.
        foreach (var reading in payload.GetProperty("detections").EnumerateArray())
        {
            // drone_id is 8 letters.
            Assert.Matches("^[a-zA-Z]{8}$", reading.GetProperty("drone_id").GetString()!);
            // In the first message every drone is new, so its start_time is this message's timestamp.
            Assert.Equal(FirstTimestamp, reading.GetProperty("start_time").GetInt64());
            // band and classification must be one of the allowed values.
            Assert.Contains(reading.GetProperty("band").GetString(), new[] { "2.4GHz", "5.8GHz" });
            Assert.Contains(reading.GetProperty("classification").GetString(), new[] { "OcuSync", "Lightbridge" });
            // rssi is inside the allowed range.
            Assert.InRange(reading.GetProperty("rssi").GetDouble(), -110, -65);
        }
    }

    // CHECKS: the behaviour across many messages in a row (40 messages, 3 seconds apart):
    //         ids are never reused, drones that stay keep their details, rssi only changes a
    //         little, and no more than 2 new drones appear in one message.
    [Fact]
    public void GenerateMessage_PreservesSurvivorsAndDoesNotReuseIds()
    {
        var simulator = new DetectionSimulator(new Random(42));
        var usedIds = new HashSet<string>();                        // every message id and drone id seen so far
        var previous = new Dictionary<string, DetectionReading>(); // the drones from the previous message, by drone_id
        bool sawSurvivor = false;    // did at least one drone stay from one message to the next?
        bool sawDeparture = false;   // did at least one drone leave?
        // The two flags above make sure the test really exercised both cases,
        // otherwise it could pass without ever checking a drone that stays or leaves.

        for (int tick = 0; tick < 40; tick++)
        {
            // Each message is 3 seconds after the last one, like the real simulator.
            long timestamp = FirstTimestamp + tick * 3;
            var message = simulator.GenerateMessage(timestamp);
            // Add() returns false if the id was already in the set, so this fails if an id is reused.
            Assert.True(usedIds.Add(message.MsgId));
            // A message must never be empty.
            Assert.NotEmpty(message.Detections);
            // Turn this message's drones into a dictionary so we can look them up by drone_id.
            var current = message.Detections.ToDictionary(reading => reading.DroneId);
            // A departure means a drone from the previous message is missing from this one.
            // "|=" means "set to true if this is true, otherwise leave it as it was".
            sawDeparture |= previous.Keys.Any(id => !current.ContainsKey(id));

            foreach (var reading in current.Values)
            {
                // rssi stays in range, and has at most 4 decimals (rounding it to 4 decimals changes nothing).
                Assert.InRange(reading.Rssi, -110, -65);
                Assert.Equal(reading.Rssi, Math.Round(reading.Rssi, 4));

                // Was this drone in the previous message too?
                if (previous.TryGetValue(reading.DroneId, out var prior))
                {
                    // A survivor: it stays the same drone, so these details must not change.
                    sawSurvivor = true;
                    Assert.Equal(prior.StartTime, reading.StartTime);
                    Assert.Equal(prior.Band, reading.Band);
                    Assert.Equal(prior.Classification, reading.Classification);
                    // The rssi must change (at least one step of 0.0001, so not equal)
                    // but by no more than 3 (a tiny bit extra allows for rounding).
                    Assert.InRange(Math.Abs(reading.Rssi - prior.Rssi), 0.00009, 3.0000001);
                }
                else
                {
                    // A new drone: its id was never used before, and it starts in this message.
                    Assert.True(usedIds.Add(reading.DroneId));
                    Assert.Equal(timestamp, reading.StartTime);
                }
            }

            // Count the drones in this message that were not in the previous one: 0, 1 or 2 new ones at most.
            Assert.InRange(current.Keys.Count(id => !previous.ContainsKey(id)), 0, 2);
            // This message becomes the "previous" one for the next loop.
            previous = current;
        }

        // Make sure the test actually saw drones staying and drones leaving.
        Assert.True(sawSurvivor);
        Assert.True(sawDeparture);
    }
}
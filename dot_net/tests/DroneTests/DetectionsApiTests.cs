using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace DroneTests;

// Tests for the API itself. Each test starts a real copy of the app in memory,
// sends it real HTTP requests (POST /detections, GET /api/state, POST /api/clear),
// and checks the responses. No browser or simulator is needed.
public class DetectionsApiTests
{
    // The timestamp (in seconds) that the fake clock starts at. This is the example time from the spec.
    private const long Start = 1712010300;

    // ---- helpers: small functions the tests share -----------------------------

    // A fresh copy of the whole app for every test, so tests never share data.
    // The clock is replaced with a fake one that starts at Start.
    private static HttpClient CreateClient()
    {
        // WebApplicationFactory<Program> starts your real app (the Program class) inside the test.
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Change the app's services just for the test.
            builder.ConfigureServices(services =>
            {
                // Remove the real clock that Program.cs registered...
                services.RemoveAll<TimeProvider>();
                // ...and put in a fake one. Without this, readings made at "Start" (in 2024)
                // would look older than 5 minutes compared to the real time and be thrown away.
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(Start)));
            });
        });
        // An HttpClient that talks straight to that in-memory app (no network, no port).
        return factory.CreateClient();
    }

    // A valid message body, with one piece swapped out at a time to make the bad ones.
    // Each parameter has a valid default, so a test only passes the piece it wants to break.
    // Note the timestamp default includes the quotes ("\"1712010300\""), so it is sent as a string like the spec.
    private static string Body(string msgType = "detections", string timestamp = "\"1712010300\"", string droneId = "aBcDeFgH", string rssi = "-87.3412")
    {
        // A "raw string": the text between the triple quotes is used exactly as written.
        // The $$ means values go in with {{double braces}}, so the JSON's own { } stay as they are.
        return $$"""
        {
          "msg_type": "{{msgType}}",
          "system": "detection_service",
          "msg_id": "kQhZpwrT",
          "timestamp": {{timestamp}},
          "detections": [
            { "drone_id": "{{droneId}}", "start_time": 1712010300, "band": "2.4GHz", "classification": "OcuSync", "rssi": {{rssi}} }
          ]
        }
        """;
    }

    // Sends a JSON text to POST /detections, the way the simulator does, and returns the API's response.
    private static Task<HttpResponseMessage> Post(HttpClient client, string json)
    {
        return client.PostAsync("/detections", new StringContent(json, Encoding.UTF8, "application/json"));
    }

    // Asks GET /api/state for the dashboard's data and returns it as JSON we can read
    // (state.GetProperty("messageCount") and so on).
    private static async Task<JsonElement> GetState(HttpClient client)
    {
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/api/state"));
        // Clone() makes a copy that is still usable after the "using" disposes the document.
        return document.RootElement.Clone();
    }

    // ---- tests ----------------------------------------------------------------

    // CHECKS: the happy path. A valid message is accepted (204) and then shows up in /api/state.
    [Fact]
    public async Task Valid_message_returns_204_and_shows_up_in_the_state()
    {
        HttpClient client = CreateClient();

        HttpResponseMessage response = await Post(client, Body());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);   // 204: accepted, nothing to send back
        JsonElement state = await GetState(client);
        Assert.Equal(1, state.GetProperty("messageCount").GetInt32());   // one message was received
        Assert.Equal(1, state.GetProperty("active").GetArrayLength());   // one drone is active
        Assert.Equal("aBcDeFgH", state.GetProperty("active")[0].GetProperty("drone_id").GetString());   // it is the drone we sent
        Assert.Equal(9, state.GetProperty("histogram").GetArrayLength());   // the histogram always has 9 bars
    }

    // CHECKS: the timestamp is accepted both as a string and as a plain number.
    //         A [Theory] runs the same test once for each [InlineData] line.
    [Theory]
    [InlineData("\"1712010300\"")]   // the spec sends the timestamp as a string
    [InlineData("1712010300")]       // a plain number works too
    public async Task Timestamp_can_be_a_string_or_a_number(string timestamp)
    {
        HttpResponseMessage response = await Post(CreateClient(), Body(timestamp: timestamp));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // CHECKS: messages with one bad value are rejected with 400 (Bad Request).
    //         Each InlineData line breaks exactly one piece: msg_type, timestamp, drone_id or rssi.
    [Theory]
    [InlineData("other", "\"1712010300\"", "aBcDeFgH", "-87.3")]    // msg_type must be "detections"
    [InlineData("detections", "\"abc\"", "aBcDeFgH", "-87.3")]      // timestamp is not a number
    [InlineData("detections", "\"1712010300\"", "", "-87.3")]       // empty drone_id
    [InlineData("detections", "\"1712010300\"", "aBcDeFgH", "null")] // rssi is null
    public async Task Bad_messages_return_400(string msgType, string timestamp, string droneId, string rssi)
    {
        HttpResponseMessage response = await Post(CreateClient(), Body(msgType, timestamp, droneId, rssi));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // CHECKS: bodies that are not a valid message at all are also rejected with 400.
    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"msg_type\": \"detections\", \"msg_id\": \"kQhZpwrT\", \"timestamp\": \"1712010300\" }")]   // no detections list
    public async Task Broken_or_incomplete_bodies_return_400(string json)
    {
        HttpResponseMessage response = await Post(CreateClient(), json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // CHECKS: the whole chain (controller -> store -> JSON) counts a drone once in the
    //         Device Breakdown, even when the same drone is sent in two messages.
    [Fact]
    public async Task Same_drone_in_two_messages_is_counted_once_in_the_bands()
    {
        HttpClient client = CreateClient();

        // the same message (same drone) twice
        await Post(client, Body());
        await Post(client, Body());

        JsonElement state = await GetState(client);
        Assert.Equal(2, state.GetProperty("messageCount").GetInt32());   // two messages were received...
        Assert.Equal(1, state.GetProperty("bands").GetProperty("2.4GHz").GetInt32());       // ...but only one drone
        Assert.Equal(1, state.GetProperty("classes").GetProperty("OcuSync").GetInt32());
    }

    // CHECKS: the Clear button's endpoint (POST /api/clear) empties everything.
    [Fact]
    public async Task Clear_empties_the_state()
    {
        HttpClient client = CreateClient();
        await Post(client, Body());   // put something in first

        HttpResponseMessage response = await client.PostAsync("/api/clear", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        JsonElement state = await GetState(client);
        Assert.Equal(0, state.GetProperty("messageCount").GetInt32());
        Assert.Equal(0, state.GetProperty("active").GetArrayLength());
    }
}
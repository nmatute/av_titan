using System.Net.Http.Json;

namespace DroneSimulator;
class Program
{
    static async Task Main(string[] args)
    {
        string apiUrl = "http://localhost:3000/detections";
        double intervalSeconds = 3;

        // Optional: dotnet run -- http://localhost:3000/detections 1
        if (args.Length > 0)
        {
            apiUrl = args[0];
        }
        if (args.Length > 1)
        {
            intervalSeconds = double.Parse(args[1]);
        }

        var simulator = new DetectionSimulator();
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // Ticks on a fixed schedule, so the rate doesn't slowly drift.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        Console.WriteLine($"Sending to {apiUrl} every {intervalSeconds}s. Press Ctrl+C to stop.");

        // Wait for the next 3-second tick, then run the loop once. Ctrl+C stops it.
        while (await timer.WaitForNextTickAsync())
        {

            // The current time in seconds since 1970, used as the message's timestamp.
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Build the next message: which drones are in the air, with their latest rssi.
            DetectionMessage message = simulator.GenerateMessage(now);

            try
            {
                // Turn the message into JSON and POST it to the API. The API replies with a status code.
                HttpResponseMessage response = await http.PostAsJsonAsync(apiUrl, message);
                Console.WriteLine($"POST {message.MsgId} ({message.Detections.Count} drones) -> {(int)response.StatusCode}");
            }
            catch (Exception error)
            {
                // Server down, timeout, etc. Print it and keep going.
                Console.WriteLine($"POST failed: {error.Message}");
            }
        }
    }
}
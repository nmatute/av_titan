namespace DroneSimulator;

/// <summary>
/// Makes the random values the simulator needs: unique ids and rssi readings.
/// It knows nothing about drones or messages, it only produces values.
/// </summary>
public class RandomValues
{
    private const string Letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";   // used to build the 8-letter ids
    private const double RssiMin = -110.0;      // allowed rssi range from the spec
    private const double RssiMax = -65.0;
    private const double MaxStep = 3.0;         // most the rssi can change between messages, keeps values "somewhat similar"

    private readonly Random _random;
    private readonly HashSet<string> _usedIds = new();   // every id handed out so far, so ids are never reused

    public RandomValues(Random random)
    {
        _random = random;
    }

    // A random 8-letter id (upper and lower case) that has never been used.
    // Used for both msg_id and drone_id. It keeps generating until it finds a new one.
    public string NewUniqueId()
    {
        string id = MakeRandomId();

        // If this id was already handed out, make another one.
        while (_usedIds.Contains(id))
        {
            id = MakeRandomId();
        }

        _usedIds.Add(id);
        return id;
    }

    // 8 random letters stuck together, like "QvfrHqEd".
    private string MakeRandomId()
    {
        string id = "";
        for (int i = 0; i < 8; i++)
        {
            id = id + Letters[_random.Next(Letters.Length)];
        }
        return id;
    }

    // The very first rssi of a new drone: anywhere in the allowed range, rounded to 4 decimals.
    public double FirstRssi()
    {
        double value = RssiMin + _random.NextDouble() * (RssiMax - RssiMin);
        return Math.Round(value, 4);
    }

    // The next rssi for a drone that is staying: the previous value plus or minus a small step
    // (up to MaxStep), kept inside the allowed range, rounded to 4 decimals.
    public double NextRssi(double previous)
    {
        double low = Math.Max(RssiMin, previous - MaxStep);    // never below -110
        double high = Math.Min(RssiMax, previous + MaxStep);   // never above -65

        double value = Math.Round(low + _random.NextDouble() * (high - low), 4);

        // The spec says rssi must change with every message,
        // so if we picked the same value as before, pick again.
        while (value == previous)
        {
            value = Math.Round(low + _random.NextDouble() * (high - low), 4);
        }

        return value;
    }
}
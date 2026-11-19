using DroneDetection.Models;
using DroneDetection.Services;
using Microsoft.Extensions.Time.Testing;

namespace DroneTests;

// Tests for DetectionStore, the class that keeps everything in memory.
// They call the store directly (no web server), so they are fast and easy to read.
public class DetectionStoreTests
{
    // The timestamp (in seconds) that every test starts at. This is the example time from the spec.
    private const long Start = 1712010300;

    // ---- helpers: small functions that build the objects the tests need -------

    // A store whose clock we control. It starts at Start and only moves when we call clock.Advance(...).
    // Returns both the store and the fake clock, so a test can move time forward.
    private static (DetectionStore store, FakeTimeProvider clock) CreateStore()
    {
        // FakeTimeProvider is a pretend clock from the testing package. It never moves by itself.
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(Start));
        // Give the store the fake clock instead of the real one.
        return (new DetectionStore(clock), clock);
    }

    // Builds one drone reading. Only id and rssi are needed; band and classification have defaults
    // so most tests don't have to repeat them.
    private static Detection Drone(string id, double rssi, string band = "2.4GHz", string classification = "OcuSync")
    {
        return new Detection { DroneId = id, StartTime = Start, Band = band, Classification = classification, Rssi = rssi };
    }

    // Builds one message, like the simulator would send: a timestamp plus any number of drones.
    // "params" lets us list the drones one after another: Message(Start, droneA, droneB).
    private static DetectionMessage Message(long timestamp, params Detection[] drones)
    {
        return new DetectionMessage
        {
            MsgType = "detections",
            MsgId = "abcdefgh",
            Timestamp = timestamp,
            Detections = drones.ToList(),
        };
    }

    // ---- tests ----------------------------------------------------------------

    // CHECKS: the message counter goes up for every message, and the "active" list is only
    //         the drones from the LATEST message (the table on the dashboard).
    [Fact]
    public void Counts_messages_and_keeps_only_the_latest_drones_as_active()
    {
        var (store, _) = CreateStore();

        // message 1 has drones A and B, message 2 has B and C (A has left)
        store.AddMessage(Message(Start, Drone("droneAAA", -80), Drone("droneBBB", -81)));
        store.AddMessage(Message(Start + 3, Drone("droneBBB", -82), Drone("droneCCC", -83)));

        StateResponse state = store.GetState();
        Assert.Equal(2, state.MessageCount);   // two messages were received
        // active is B and C only: A is gone because it was not in the latest message
        Assert.Equal(new[] { "droneBBB", "droneCCC" }, state.Active.Select(d => d.DroneId).ToArray());
    }

    // CHECKS: the "top bands" and "top classifications" count each distinct drone once,
    //         however many messages it appears in (the Device Breakdown card).
    [Fact]
    public void Counts_each_drone_once_by_band_and_classification()
    {
        var (store, _) = CreateStore();

        // droneAAA appears in all three messages but must only be counted once.
        store.AddMessage(Message(Start, Drone("droneAAA", -80, "2.4GHz", "OcuSync")));
        store.AddMessage(Message(Start + 3, Drone("droneAAA", -81, "2.4GHz", "OcuSync"), Drone("droneBBB", -82, "5.8GHz", "Lightbridge")));
        store.AddMessage(Message(Start + 6, Drone("droneAAA", -82, "2.4GHz", "OcuSync"), Drone("droneBBB", -83, "5.8GHz", "Lightbridge"), Drone("droneCCC", -84, "2.4GHz", "OcuSync")));

        StateResponse state = store.GetState();
        // 3 distinct drones in total: A and C are 2.4GHz / OcuSync, B is 5.8GHz / Lightbridge.
        // If drones were counted once per message, these numbers would be much bigger.
        Assert.Equal(2, state.Bands["2.4GHz"]);
        Assert.Equal(1, state.Bands["5.8GHz"]);
        Assert.Equal(2, state.Classes["OcuSync"]);
        Assert.Equal(1, state.Classes["Lightbridge"]);
    }

    // CHECKS: each rssi reading lands in the correct 5 dB bar of the histogram,
    //         including the edge cases at the very bottom (-110) and very top (-65).
    [Fact]
    public void Histogram_puts_each_reading_in_its_bin()
    {
        var (store, _) = CreateStore();

        // -110 -> bin 0, -87 and -88 -> bin 4 (-90 to -85), -65 is the top edge and must go in the last bin (8)
        store.AddMessage(Message(Start,
            Drone("droneAAA", -110),
            Drone("droneBBB", -87),
            Drone("droneCCC", -88),
            Drone("droneDDD", -65)));

        StateResponse state = store.GetState();
        Assert.Equal(9, state.Histogram.Count);          // always 9 bars, even the empty ones
        Assert.Equal(-110, state.Histogram[0].Low);      // the first bar starts at -110
        Assert.Equal(-70, state.Histogram[8].Low);       // the last bar starts at -70 (and ends at -65)
        // counts for bars 0 to 8: one in bar 0, two in bar 4, one in bar 8, nothing elsewhere
        Assert.Equal(new[] { 1, 0, 0, 0, 2, 0, 0, 0, 1 }, state.Histogram.Select(b => b.Count).ToArray());
    }

    // CHECKS: the histogram only covers the last 5 minutes, so old readings stop counting.
    //         This uses the fake clock to jump forward in time without waiting.
    [Fact]
    public void Histogram_ignores_readings_older_than_five_minutes()
    {
        var (store, clock) = CreateStore();

        store.AddMessage(Message(Start, Drone("droneAAA", -87)));      // will be too old
        clock.Advance(TimeSpan.FromSeconds(301));                      // jump 301 seconds (just over 5 minutes) ahead
        store.AddMessage(Message(Start + 301, Drone("droneAAA", -70))); // recent: bin 8

        StateResponse state = store.GetState();
        Assert.Equal(1, state.Histogram.Sum(b => b.Count));   // only one reading is left in total
        Assert.Equal(1, state.Histogram[8].Count);            // the recent one (-70)
        Assert.Equal(0, state.Histogram[4].Count);            // the old one (-87) is gone
    }

    // CHECKS: the Clear button's action wipes everything: counters, the table and the histogram.
    [Fact]
    public void Clear_resets_everything()
    {
        var (store, _) = CreateStore();
        store.AddMessage(Message(Start, Drone("droneAAA", -87)));   // put something in first

        store.Clear();

        StateResponse state = store.GetState();
        Assert.Equal(0, state.MessageCount);
        Assert.Empty(state.Active);                            // no drones in the table
        Assert.Empty(state.Bands);                             // no band counts
        Assert.Empty(state.Classes);                           // no classification counts
        Assert.Equal(0, state.Histogram.Sum(b => b.Count));    // all histogram bars are zero
    }
}
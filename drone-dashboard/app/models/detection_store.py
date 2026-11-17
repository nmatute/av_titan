"""The store: keeps everything received so far and works out the numbers for the dashboard."""
import threading
import time

from app.models.detection import HistogramBin, StateResponse

WINDOW_SECONDS = 300   # the histogram covers the last 5 minutes
RSSI_MIN = -110        # the histogram starts at -110
BIN_SIZE = 5           # each bin is 5 dB wide
NUMBER_OF_BINS = 9     # 9 bins of 5 dB, so the last one ends at -65


def build_histogram(readings):
    """Count how many rssi readings fall into each 5 dB bin.

    `readings` is a list of (timestamp, rssi) pairs.
    """
    # start with 9 empty bins: -110 to -105, -105 to -100, ... -70 to -65
    bins = []
    for i in range(NUMBER_OF_BINS):
        low = RSSI_MIN + i * BIN_SIZE
        bins.append(HistogramBin(low=low, high=low + BIN_SIZE, count=0))

    # put each reading into its bin
    for timestamp, rssi in readings:
        index = int((rssi - RSSI_MIN) // BIN_SIZE)   # for example -87 -> bin 4
        if index < 0:
            index = 0
        if index > NUMBER_OF_BINS - 1:
            index = NUMBER_OF_BINS - 1               # -65 gives 9, so it goes in the last bin (8)
        bins[index].count += 1

    return bins


class DetectionStore:
    """Keeps everything received so far. It's all in memory, so it resets when the server restarts."""

    def __init__(self, clock=time.time):
        self.clock = clock                # gives the current time. Tests pass in a fake one.
        self.lock = threading.Lock()      # requests can arrive at the same moment, so they take turns
        self.message_count = 0            # how many messages have been received
        self.active = []                  # the drones in the latest message
        self.first_sightings = {}         # drone_id -> the first time we saw that drone
        self.readings = []                # a (timestamp, rssi) pair for every drone in every message

    def readings_since(self, cutoff):
        """Return the readings that are not older than `cutoff`. Call this while holding the lock."""
        recent = []
        for timestamp, rssi in self.readings:
            if timestamp >= cutoff:
                recent.append((timestamp, rssi))
        return recent

    def add_message(self, message):
        """Store one message (a DetectionMessage that has already been checked)."""
        with self.lock:
            self.message_count += 1

            # "Active" drones are the ones in the latest message.
            self.active = message.detections

            for detection in message.detections:
                # Only remember a drone the first time we see it, so it counts once
                if detection.drone_id not in self.first_sightings:
                    self.first_sightings[detection.drone_id] = detection
                self.readings.append((message.timestamp, detection.rssi))


    def get_state(self):
        """Return everything the dashboard needs, as a StateResponse."""
        now = int(self.clock())

        with self.lock:
            # Count each distinct drone once, by band and by classification.
            bands = {}
            classes = {}
            for drone in self.first_sightings.values():
                bands[drone.band] = bands.get(drone.band, 0) + 1
                classes[drone.classification] = classes.get(drone.classification, 0) + 1

            # Only the last 5 minutes of readings go in the histogram.
            recent = self.readings_since(now - WINDOW_SECONDS)

            return StateResponse(
                message_count=self.message_count,
                active=self.active,
                bands=bands,
                classes=classes,
                histogram=build_histogram(recent),
                server_time=now,   # the dashboard uses this to line up its clock with the server's
            )
"""Drone detection simulator.

Builds 'detections' messages and POSTs one to the server every 3 seconds.

Usage:
    python simulator.py                      # POST to http://localhost:3000/detections
    python simulator.py --url http://host:port/detections
    python simulator.py --dry-run            # print messages instead of POSTing
"""
import argparse
import json
import random
import string
import time
import urllib.request

# ---- Constants -------------------------------------------------------------
LETTERS = string.ascii_letters  # a-z and A-Z, used to build the random 8-char ids
BANDS = ["2.4GHz", "5.8GHz"]  # each drone gets one of these, chosen once
CLASSIFICATIONS = ["OcuSync", "Lightbridge"]  # each drone gets one of these, chosen once
RSSI_MIN, RSSI_MAX = -110.0, -65.0  # allowed rssi range from the spec
MAX_STEP = 3.0  # max rssi change between messages, keeps values "somewhat similar"


class Simulator:
    """Pretends to be the detection service: tracks active drones and builds messages."""

    def __init__(self, survival_rate=0.8, max_spawn=2, rng=None):
        """Set up the simulator.

        survival_rate: chance that a drone shows up again in the next message (0.8 = 80%).
        max_spawn:     most new drones that can appear in a single message.
        rng:           optional seed. Pass a seeded one in tests so output is repeatable.
        """
        self.survival_rate = survival_rate
        self.max_spawn = max_spawn
        self.rng = rng or random.Random()
        self.active = {}        # drone_id -> drone state (the drones currently "in the air")
        self.used_ids = set()   # every id handed out so far, so ids are never reused

    def _unique_id(self):
        """Return a random 8-letter id (upper and lower case) that has never been used.

        Used for both msg_id and drone_id. It keeps generating until it finds
        one that is not in used_ids, then records it.
        """
        while True:
            new_id = "".join(self.rng.choice(LETTERS) for i in range(8))
            if new_id not in self.used_ids:
                self.used_ids.add(new_id)
                return new_id

    def _next_rssi(self, previous):
        """Return the next rssi value for a drone, rounded to 4 decimals.

        First reading (previous is None): a random value anywhere in the range.
        Later readings: the previous value plus a small random step of up to
        +/- MAX_STEP, bounced back into range if it overshoots.
        """
        if previous is None:
            return round(self.rng.uniform(RSSI_MIN, RSSI_MAX), 4)
        
        low = max(RSSI_MIN, previous - MAX_STEP)    # never below -110
        high = min(RSSI_MAX, previous + MAX_STEP)   # never above -65


        # Keep picking random values until we get one that differs from the
        # previous rssi. The spec says rssi must change with every message.
        while True:
            value = round(self.rng.uniform(low, high), 4)
            if value != previous:                   # must change every message
                return value

    def generate_message(self, now=None):
        """Build and return the next detections message. 

        `now` defaults to the current time. Steps:
          1. Drop each existing drone with a 20% chance (it is "gone" for good).
          2. Spawn 0 to max_spawn new drones, with start_time = this timestamp.
          3. Give every active drone a new rssi (band, classification and start_time stay fixed).
          4. Wrap everything in the message envelope.
        """
        if now is None:
            now = int(time.time())

        # Step 1: each existing drone has an 80% chance to appear in this message
        for drone_id in list(self.active):
            if self.rng.random() >= self.survival_rate:
                del self.active[drone_id]

        # Step 2: new drones appear with start_time
        spawn = self.rng.randint(0, self.max_spawn)
        if not self.active:
            spawn = max(spawn, 1)  # never send an empty message
        for i in range(spawn):
            drone_id = self._unique_id()
            self.active[drone_id] = {
                "drone_id": drone_id,
                "start_time": now,
                "band": self.rng.choice(BANDS),
                "classification": self.rng.choice(CLASSIFICATIONS),
                "rssi": None,  # filled in by step 3
            }

        # Step 3: new rssi for every active drone (copy each dict so the message
        # isn't changed by later ticks)
        detections = []
        for drone in self.active.values():
            drone["rssi"] = self._next_rssi(drone["rssi"])
            detections.append(dict(drone))

        # Step 4: the message envelope. timestamp is a string, as in the spec example.
        return {
            "msg_type": "detections",
            "system": "detection_service",
            "msg_id": self._unique_id(),
            "detections": detections,
            "timestamp": str(now),
        }


def post(url, message):
    """POST one message to the server as JSON and return the HTTP status code. """
    request = urllib.request.Request(
        url,
        data=json.dumps(message).encode("utf-8"),  # the message dict -> JSON bytes
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=5) as response:
        return response.status


def main():
    """Command-line entry point: build a message every interval and send it.
    """
    parser = argparse.ArgumentParser(description="Drone detection simulator")
    parser.add_argument("--url", default="http://localhost:3000/detections")
    parser.add_argument("--interval", type=float, default=3.0, help="seconds between messages")
    args = parser.parse_args()

    sim = Simulator()
    next_send = time.monotonic()
    while True:
        message = sim.generate_message()

        try:
            status = post(args.url, message)
            print(f"POST {message['msg_id']} ({len(message['detections'])} drones) -> {status}")
        except Exception as error:  # server down, etc. keep going
            print(f"POST failed: {error}")

        # schedule against a fixed clock so the rate doesn't drift
        # (sleeping a flat 3s would slowly slip because each loop takes some time)
        next_send += args.interval
        time.sleep(max(0.0, next_send - time.monotonic()))


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass  # Ctrl+C stops the simulator quietly
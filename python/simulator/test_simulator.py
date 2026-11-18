"""Try out the simulator on its own. No server needed.

Put this file in the same folder as simulator.py and run:
    python try_simulator.py
"""
import random
from simulator import Simulator

def show_messages(count=6):
    """Part 1: print a few messages so you can eyeball them."""
    print("=" * 60)
    print(f"PART 1: first {count} messages (seeded, so the same every run)")
    print("=" * 60)
    sim = Simulator(rng=random.Random(42))

    # First Message:
    msg = sim.generate_message(now=1712010300) #time stamp + 3s each time
    print(f"\nmessage 1: msg_id={msg['msg_id']} timestamp={msg['timestamp']} "
            f"drones={len(msg['detections'])}")
    for d in msg["detections"]:
        print(f"   {d['drone_id']}  start={d['start_time']}  {d['band']:<6} "
                f"{d['classification']:<11} rssi={d['rssi']}")

    # Second Message:
    msg = sim.generate_message(now=1712010303) #time stamp + 3s each time
    print(f"\nmessage 2: msg_id={msg['msg_id']} timestamp={msg['timestamp']} "
            f"drones={len(msg['detections'])}")
    for d in msg["detections"]:
        print(f"   {d['drone_id']}  start={d['start_time']}  {d['band']:<6} "
                f"{d['classification']:<11} rssi={d['rssi']}")


if __name__ == "__main__":
    show_messages()

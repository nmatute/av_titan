"""Tests for the detections API.

Run from the project root with:  pytest
"""
import importlib.util
import random
from collections import Counter
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app import create_app
from app.models.detection_store import DetectionStore

T0 = 1712010300  # the start time used in the spec's example
JSON_HEADERS = {"content-type": "application/json"}


# ---- helpers ---------------------------------------------------------------

class FakeClock:
    """A clock the test controls, so "5 minutes ago" is exact and repeatable."""

    def __init__(self, now=T0):
        self.now = now

    def __call__(self):
        return self.now


@pytest.fixture
def clock():
    return FakeClock()


@pytest.fixture
def client(clock):
    return TestClient(create_app(DetectionStore(clock=clock)))


def drone(drone_id="aaaaaaaa", band="2.4GHz", classification="OcuSync", rssi=-90.0, start_time=T0):
    return {"drone_id": drone_id, "start_time": start_time, "band": band,
            "classification": classification, "rssi": rssi}


def message(detections, msg_id="msg00001", timestamp=T0):
    # timestamp is a string, like the spec's example
    return {"msg_type": "detections", "system": "detection_service",
            "msg_id": msg_id, "detections": detections, "timestamp": str(timestamp)}


def post(client, msg):
    return client.post("/detections", json=msg)


def state(client):
    return client.get("/api/state").json()


def counts_by_low(histogram):
    return {b["low"]: b["count"] for b in histogram}


# ---- receiving messages ----------------------------------------------------

def test_valid_message_is_accepted_with_204_and_no_body(client):
    response = post(client, message([drone()]))
    assert response.status_code == 204
    assert response.content == b""


def test_message_count_goes_up_by_one_per_message(client):
    for i in range(5):
        post(client, message([drone()], msg_id=f"msg{i:05d}"))
    assert state(client)["messageCount"] == 5


def test_state_before_any_message_is_empty(client):
    s = state(client)
    assert s["messageCount"] == 0
    assert s["active"] == []
    assert s["bands"] == {} and s["classes"] == {}
    assert sum(b["count"] for b in s["histogram"]) == 0


def test_state_uses_the_json_names_the_dashboard_expects(client):
    assert set(state(client)) == {
        "messageCount", "active", "bands", "classes", "histogram", "serverTime"}


def test_timestamp_can_be_a_string_or_a_number(client):
    as_string = message([drone()], msg_id="msg00001")
    as_number = message([drone(drone_id="bbbbbbbb")], msg_id="msg00002")
    as_number["timestamp"] = T0
    assert post(client, as_string).status_code == 204
    assert post(client, as_number).status_code == 204
    assert state(client)["messageCount"] == 2


def test_a_message_with_no_drones_is_accepted(client):
    post(client, message([drone()], msg_id="msg00001"))
    assert post(client, message([], msg_id="msg00002")).status_code == 204
    s = state(client)
    assert s["messageCount"] == 2
    assert s["active"] == []


# ---- active detections -----------------------------------------------------

def test_active_drones_are_the_ones_in_the_latest_message(client):
    post(client, message([drone("aaaaaaaa"), drone("bbbbbbbb")], msg_id="msg00001"))
    post(client, message([drone("aaaaaaaa", rssi=-88.5)], msg_id="msg00002", timestamp=T0 + 3))
    active = state(client)["active"]
    assert [d["drone_id"] for d in active] == ["aaaaaaaa"]  # bbbbbbbb is gone
    assert active[0]["rssi"] == -88.5


def test_active_drone_fields_are_returned(client):
    post(client, message([drone("aaaaaaaa", "5.8GHz", "Lightbridge", -93.3412, T0 + 5)]))
    assert state(client)["active"] == [{
        "drone_id": "aaaaaaaa", "start_time": T0 + 5, "band": "5.8GHz",
        "classification": "Lightbridge", "rssi": -93.3412,
    }]


# ---- top bands and classifications -----------------------------------------

def test_same_drone_in_many_messages_counts_once(client):
    for i in range(5):
        post(client, message([drone("aaaaaaaa", "2.4GHz", "OcuSync")],
                             msg_id=f"msg{i:05d}", timestamp=T0 + i * 3))
    s = state(client)
    assert s["bands"] == {"2.4GHz": 1}
    assert s["classes"] == {"OcuSync": 1}


def test_different_drones_are_counted_separately(client):
    post(client, message([
        drone("aaaaaaaa", "2.4GHz", "OcuSync"),
        drone("bbbbbbbb", "2.4GHz", "Lightbridge"),
        drone("cccccccc", "5.8GHz", "OcuSync"),
    ]))
    s = state(client)
    assert s["bands"] == {"2.4GHz": 2, "5.8GHz": 1}
    assert s["classes"] == {"OcuSync": 2, "Lightbridge": 1}


def test_drones_that_left_still_count_in_the_totals(client):
    post(client, message([drone("aaaaaaaa", "2.4GHz")], msg_id="msg00001"))
    post(client, message([drone("bbbbbbbb", "5.8GHz")], msg_id="msg00002", timestamp=T0 + 3))
    assert state(client)["bands"] == {"2.4GHz": 1, "5.8GHz": 1}


# ---- histogram -------------------------------------------------------------

def test_histogram_has_nine_five_db_bins_from_minus_110_to_minus_65(client):
    histogram = state(client)["histogram"]
    assert [b["low"] for b in histogram] == list(range(-110, -65, 5))
    assert histogram[0]["low"] == -110 and histogram[-1]["high"] == -65


def test_histogram_bin_edges(client):
    post(client, message([
        drone("aaaaaaaa", rssi=-110.0),   # lowest allowed  -> first bin
        drone("bbbbbbbb", rssi=-65.0),    # highest allowed -> last bin
        drone("cccccccc", rssi=-87.0),    # in between      -> bin starting at -90
    ]))
    counts = counts_by_low(state(client)["histogram"])
    assert counts[-110] == 1
    assert counts[-70] == 1
    assert counts[-90] == 1
    assert sum(counts.values()) == 3


def test_histogram_counts_every_reading_of_every_drone(client):
    for i in range(3):
        post(client, message([drone("aaaaaaaa", rssi=-80.0 - i), drone("bbbbbbbb", rssi=-100.0 + i)],
                             msg_id=f"msg{i:05d}", timestamp=T0 + i * 3))
    # 2 drones x 3 messages = 6 readings
    assert sum(b["count"] for b in state(client)["histogram"]) == 6


def test_readings_older_than_five_minutes_leave_the_histogram(client, clock):
    post(client, message([drone(rssi=-90.0)], timestamp=T0))

    clock.now = T0 + 300   # exactly 5 minutes old: still inside the window
    assert sum(b["count"] for b in state(client)["histogram"]) == 1

    clock.now = T0 + 301   # one second older: dropped
    assert sum(b["count"] for b in state(client)["histogram"]) == 0


def test_old_readings_go_but_the_distinct_drone_counts_stay(client, clock):
    post(client, message([drone("aaaaaaaa", "2.4GHz")], timestamp=T0))
    clock.now = T0 + 1000
    s = state(client)
    assert sum(b["count"] for b in s["histogram"]) == 0
    assert s["bands"] == {"2.4GHz": 1}      # distinct counts are not windowed
    assert s["messageCount"] == 1


def test_server_time_comes_from_the_clock(client, clock):
    clock.now = T0 + 42
    assert state(client)["serverTime"] == T0 + 42


# ---- bad input -------------------------------------------------------------
# FastAPI answers 422 Unprocessable Entity when a request body is not valid.

BAD_MESSAGES = {
    "wrong msg_type": lambda m: m.update(msg_type="status"),
    "missing msg_id": lambda m: m.pop("msg_id"),
    "blank msg_id": lambda m: m.update(msg_id="   "),
    "timestamp is not a number": lambda m: m.update(timestamp="yesterday"),
    "missing timestamp": lambda m: m.pop("timestamp"),
    "detections is not a list": lambda m: m.update(detections="none"),
    "missing detections": lambda m: m.pop("detections"),
    "a detection is not an object": lambda m: m.update(detections=["x"]),
    "detection missing drone_id": lambda m: m["detections"][0].pop("drone_id"),
    "detection missing band": lambda m: m["detections"][0].pop("band"),
    "detection missing start_time": lambda m: m["detections"][0].pop("start_time"),
    "start_time is fractional": lambda m: m["detections"][0].update(start_time=1.5),
    "rssi is text": lambda m: m["detections"][0].update(rssi="loud"),
}


@pytest.mark.parametrize("break_it", BAD_MESSAGES.values(), ids=BAD_MESSAGES.keys())
def test_invalid_message_gets_422_and_is_not_counted(client, break_it):
    bad = message([drone()])
    break_it(bad)
    response = post(client, bad)
    assert response.status_code == 422
    assert "detail" in response.json()   # says which field is wrong
    assert state(client)["messageCount"] == 0


def test_body_that_is_not_json_gets_422(client):
    response = client.post("/detections", content="hello", headers=JSON_HEADERS)
    assert response.status_code == 422
    assert state(client)["messageCount"] == 0


def test_json_that_is_not_an_object_gets_422(client):
    assert client.post("/detections", json=[1, 2, 3]).status_code == 422


def test_nan_rssi_is_rejected(client):
    # NaN is not valid JSON, but Python's parser accepts it, so it must be checked
    body = ('{"msg_type":"detections","msg_id":"m1","timestamp":"1712010300",'
            '"detections":[{"drone_id":"a","start_time":1712010300,"band":"2.4GHz",'
            '"classification":"OcuSync","rssi":NaN}]}')
    response = client.post("/detections", content=body, headers=JSON_HEADERS)
    assert response.status_code == 422
    assert state(client)["messageCount"] == 0


# ---- integration with the real simulator -----------------------------------

def load_simulator():
    """Load simulator/simulator.py under its own name, whatever the folder setup."""
    path = Path(__file__).resolve().parent.parent / "simulator" / "simulator.py"
    spec = importlib.util.spec_from_file_location("drone_simulator", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_api_totals_match_what_the_simulator_sent(client, clock):
    sim = load_simulator().Simulator(rng=random.Random(5))
    total = 200
    sent = []
    for i in range(total):
        msg = sim.generate_message(now=T0 + i * 3)
        clock.now = T0 + i * 3              # time passes as messages arrive
        assert post(client, msg).status_code == 204
        sent.append(msg)

    s = state(client)

    # worked out independently from the messages, not from the API
    first_seen = {}
    for msg in sent:
        for d in msg["detections"]:
            first_seen.setdefault(d["drone_id"], (d["band"], d["classification"]))
    cutoff = clock.now - 300
    readings_in_window = sum(
        len(m["detections"]) for m in sent if int(m["timestamp"]) >= cutoff
    )

    assert s["messageCount"] == total
    assert s["bands"] == dict(Counter(band for band, _ in first_seen.values()))
    assert s["classes"] == dict(Counter(cls for _, cls in first_seen.values()))
    assert sum(s["bands"].values()) == len(first_seen)
    assert sum(b["count"] for b in s["histogram"]) == readings_in_window
    assert [d["drone_id"] for d in s["active"]] == [d["drone_id"] for d in sent[-1]["detections"]]

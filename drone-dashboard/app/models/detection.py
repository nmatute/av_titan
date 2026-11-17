"""Models: the shape of the data going in and out of the API.

These are Pydantic classes. FastAPI uses them to check every incoming request,
so wrong types or missing fields are rejected with a 422 that says which field
is wrong. The store that keeps the data is in detection_store.py.
"""
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints
# A string that can't be empty or only spaces.
NonEmptyStr = Annotated[str, StringConstraints(strip_whitespace=True, min_length=1)]


# ---- what the simulator POSTs ----------------------------------------------

class Detection(BaseModel):
    """One drone's reading inside a message."""
    drone_id: NonEmptyStr
    start_time: int
    band: NonEmptyStr
    classification: NonEmptyStr
    rssi: float = Field(allow_inf_nan=False)


class DetectionMessage(BaseModel):
    """One POSTed message: an id, a timestamp and the drones active right now.

    Extra fields in the JSON (like "system") are ignored.
    """
    msg_type: Literal["detections"]
    msg_id: NonEmptyStr
    timestamp: int  # the spec sends it as a string; Pydantic converts "1712010300" to 1712010300
    detections: list[Detection]


# ---- what the API sends back to the dashboard ------------------------------

class HistogramBin(BaseModel):
    """One 5 dB bucket: how many rssi readings fell between low and high."""
    low: int
    high: int
    count: int


class StateResponse(BaseModel):
    """The JSON returned by GET /api/state."""
    # alias_generator turns message_count into "messageCount" in the JSON,
    # which is the naming the JavaScript side will expect.
    message_count: int
    active: list[Detection]
    bands: dict[str, int]
    classes: dict[str, int]
    histogram: list[HistogramBin]
    server_time: int
"""Controller: handles HTTP requests and responses. No logic in here."""
from fastapi import APIRouter, Depends, Request, Response
from app.models.detection import DetectionMessage, StateResponse
from app.models.detection_store import DetectionStore

# A router is a group of routes defined on their own, then attached to the app
# in create_app(). (It is FastAPI's version of Flask's Blueprint.)
router = APIRouter()


def get_store(request: Request) -> DetectionStore:
    """Dependency: hands each route the store that create_app() attached."""
    return request.app.state.store


@router.post("/detections", status_code=204)
def receive_detections(
    message: DetectionMessage,   # FastAPI reads the JSON body and validates it into this
    store: DetectionStore = Depends(get_store),
):
    """POST /detections: where the simulator sends its messages.
    """
    store.add_message(message)
    return Response(status_code=204)


@router.get("/api/state", response_model=StateResponse)
def get_state(store: DetectionStore = Depends(get_store)):
    """GET /api/state: the current data as JSON. The dashboard page polls this."""
    return store.get_state()
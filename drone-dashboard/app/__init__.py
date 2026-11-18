"""App factory: builds the FastAPI app and attaches the controller and the store."""
from pathlib import Path
from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from fastapi.staticfiles import StaticFiles

from app.controllers.detections import router as detections_router
from app.controllers.pages import router as pages_router
from app.models.detection_store import DetectionStore

STATIC_FOLDER = Path(__file__).resolve().parent / "static"

def create_app(store=None):
    """Create the app. """
    app = FastAPI(title="Drone Dashboard API")

    # One shared store for the whole app: use the one passed in (tests), else create it
    app.state.store = store or DetectionStore()
    app.include_router(detections_router)
    app.include_router(pages_router)

    # serves app/static/css/dashboard.css at /static/css/dashboard.css, and the same for js
    app.mount("/static", StaticFiles(directory=STATIC_FOLDER), name="static")

    #Tell FastAPI how to reply when a request fails validation (422)
    @app.exception_handler(RequestValidationError)
    async def invalid_request(request, error):
        """Reply 422 saying where and why a message is invalid.
        """
        problems = [
            {"loc": list(e["loc"]), "msg": e["msg"], "type": e["type"]}
            for e in error.errors()
        ]
        return JSONResponse(status_code=422, content={"detail": problems})

    return app

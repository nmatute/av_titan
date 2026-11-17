"""App factory: builds the FastAPI app and attaches the controller and the store."""
from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from app.controllers.detections import router
from app.models.detection_store import DetectionStore

def create_app(store=None):
    """Create the app.
    """
    app = FastAPI(title="Drone Dashboard API")

    # One shared store for the whole app: use the one passed in (tests), else create it
    app.state.store = store or DetectionStore()
    app.include_router(router)

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

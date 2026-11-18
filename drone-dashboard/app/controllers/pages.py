"""Controller for the dashboard page itself."""
from pathlib import Path

from fastapi import APIRouter
from fastapi.responses import FileResponse

router = APIRouter()

# app/templates/index.html, found relative to this file so it works from any folder
INDEX_FILE = Path(__file__).resolve().parent.parent / "templates" / "index.html"


@router.get("/", include_in_schema=False)
def dashboard():
    """GET /: the dashboard page. Its JavaScript then polls /api/state."""
    return FileResponse(INDEX_FILE)
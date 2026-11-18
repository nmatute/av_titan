"""Start the API on port 3000, where the simulator posts by default."""
import uvicorn

from app import create_app

app = create_app()

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=3000)
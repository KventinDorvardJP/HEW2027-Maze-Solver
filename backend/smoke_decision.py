r"""One real API/model decision. Does not move an agent or invent an outcome.

From repository root: .\.venv\Scripts\python.exe backend\smoke_decision.py
Start the backend and Ollama first. Each run creates a fresh session.
"""

import json
from pathlib import Path
from uuid import uuid4

import httpx


def main():
    body = json.loads((Path(__file__).resolve().parents[1] / "examples/decide-request.json").read_text())
    body["session_id"] = str(uuid4())
    try:
        with httpx.Client(base_url="http://127.0.0.1:8000", timeout=75, trust_env=False) as client:
            health = client.get("/health", timeout=10)
            health.raise_for_status()
            print("Health:", health.json())
            response = client.post("/decide", json=body)
            print("Decision HTTP status:", response.status_code)
            print(response.text)
            response.raise_for_status()
    except httpx.HTTPError as exc:
        print("Smoke check failed:", exc)
        print("Check the backend and Ollama consoles. No fallback move was generated.")
        return 1
    print("Received a requested move from the real model. No Unity movement was tested.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

"""Run from the repository root with uvicorn backend.app.main:app."""

import logging
from contextlib import asynccontextmanager

import httpx
from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from .errors import ApiError
from .ollama import OllamaClient
from .schemas import DecisionRequest, DecisionResponse, OutcomeRequest, OutcomeResponse
from .sessions import SessionStore


def create_app(ollama=None):
    @asynccontextmanager
    async def lifespan(app):
        async with httpx.AsyncClient(base_url="http://127.0.0.1:11434", timeout=60, trust_env=False) as client:
            if ollama is None:
                app.state.ollama = OllamaClient(client)
            yield

    app = FastAPI(title="HEW2027 Maze Solver", version="0.3", lifespan=lifespan)
    app.state.sessions = SessionStore()
    app.state.ollama = ollama

    @app.exception_handler(ApiError)
    async def api_error(request: Request, exc: ApiError):
        return JSONResponse(status_code=exc.status, content={"error": {"code": exc.code, "message": exc.message}})

    @app.exception_handler(RequestValidationError)
    async def invalid_request(request: Request, exc: RequestValidationError):
        details = "; ".join(f"{'.'.join(map(str, e['loc']))}: {e['msg']}" for e in exc.errors())
        return JSONResponse(status_code=422, content={"error": {"code": "invalid_request", "message": details}})

    @app.exception_handler(Exception)
    async def unexpected_error(request: Request, exc: Exception):
        logging.getLogger("uvicorn.error").error("Unexpected backend failure", exc_info=exc)
        return JSONResponse(status_code=500, content={"error": {"code": "internal_error", "message": "Unexpected backend failure. Check the backend console and reset."}})

    @app.get("/health")
    async def health():
        return {"status": "ok"}

    @app.post("/decide", response_model=DecisionResponse)
    async def decide(body: DecisionRequest):
        return await app.state.sessions.decide(body, app.state.ollama)

    @app.post("/outcome", response_model=OutcomeResponse)
    async def outcome(body: OutcomeRequest):
        app.state.sessions.outcome(body)
        return OutcomeResponse(session_id=body.session_id, turn=body.turn)

    return app


app = create_app()

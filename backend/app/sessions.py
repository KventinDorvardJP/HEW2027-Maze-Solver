"""Small in-memory session state machine for the single-process prototype."""

import asyncio
import logging
from dataclasses import dataclass, field

from pydantic import ValidationError

from .errors import ApiError
from .ollama import OllamaClient, build_messages
from .schemas import DecisionRequest, DecisionResponse, Move, Outcome, OutcomeRequest, adjacent_move_schema

logger = logging.getLogger("uvicorn.error")


@dataclass
class Session:
    start: dict
    goal: dict
    position: dict
    next_turn: int = 0
    pending: dict | None = None
    previous: Outcome | None = None
    finished: bool = False
    failed: bool = False
    history: list[dict] = field(default_factory=list)
    lock: asyncio.Lock = field(default_factory=asyncio.Lock)


def conflict(message: str):
    raise ApiError(409, "session_conflict", message)


class SessionStore:
    def __init__(self):
        self.sessions: dict[str, Session] = {}

    async def decide(self, request: DecisionRequest, ollama: OllamaClient) -> DecisionResponse:
        session = self.sessions.get(request.session_id)
        if session is None:
            if request.turn != 0 or request.previous_outcome is not None or request.position != request.start:
                conflict("A new session must begin at turn 0 at the start with no previous outcome. Reset the run.")
            session = Session(request.start.model_dump(), request.goal.model_dump(), request.position.model_dump())
            self.sessions[request.session_id] = session
        if session.lock.locked():
            conflict("A decision is already being generated for this session.")
        async with session.lock:
            if session.failed or session.finished or session.pending is not None:
                conflict("Session failed, completed, or still awaiting an outcome. Reset after an error.")
            if (request.turn != session.next_turn or request.start.model_dump() != session.start
                    or request.goal.model_dump() != session.goal or request.position.model_dump() != session.position
                    or request.previous_outcome != session.previous):
                conflict("Turn, fixed coordinates, position, or previous outcome does not match history.")

            record = {"observation": request.model_dump(), "raw_response": None,
                      "requested_move": None, "outcome": None, "error": None}
            messages = build_messages(record["observation"], session.history)
            session.history.append(record)
            logger.info("session=%s turn=%s observation=%s", request.session_id, request.turn, record["observation"])
            try:
                raw = await ollama.generate(messages, adjacent_move_schema(request.position))
                record["raw_response"] = raw
                logger.info("session=%s turn=%s raw_model_response=%r", request.session_id, request.turn, raw)
                try:
                    move = Move.model_validate_json(raw)
                    if abs(move.x - request.position.x) + abs(move.y - request.position.y) != 1:
                        raise ValueError("Destination is not orthogonally adjacent")
                except (ValidationError, ValueError) as exc:
                    raise ApiError(502, "invalid_model_output", "Model output is not a valid adjacent in-bounds JSON move. Reset to try again.") from exc
                record["requested_move"] = move.model_dump()
                logger.info("session=%s turn=%s requested_move=%s", request.session_id, request.turn, record["requested_move"])
                session.pending = record
                return DecisionResponse(session_id=request.session_id, turn=request.turn, move=move)
            except BaseException as exc:
                # A cancelled HTTP request must not leave a session reusable with
                # uncertain model state. Preserve evidence and require a reset.
                session.failed = True
                record["error"] = exc.code if isinstance(exc, ApiError) else type(exc).__name__
                logger.warning("session=%s turn=%s error=%s", request.session_id, request.turn, record["error"])
                raise

    def outcome(self, request: OutcomeRequest):
        session = self.sessions.get(request.session_id)
        if session is None:
            conflict("Unknown session. Reset the run after a backend restart.")
        if session.lock.locked() or session.failed or session.finished or session.pending is None:
            conflict("This session is not awaiting an outcome.")
        if request.turn != session.next_turn:
            conflict("Outcome turn does not match the pending decision.")
        outcome = request.outcome
        destination = {key: session.pending["requested_move"][key] for key in ("x", "y")}
        expected = destination if outcome.success else session.position
        if outcome.position.model_dump() != expected:
            conflict("Reported position does not match the outcome status and pending move.")
        if outcome.success and ((outcome.status == "goal_reached") != (expected == session.goal)):
            conflict("Goal status does not match the reported position.")
        session.pending["outcome"] = outcome.model_dump()
        session.position = outcome.position.model_dump()
        session.previous = outcome
        session.finished = outcome.status == "goal_reached"
        session.pending = None
        session.next_turn += 1
        logger.info("session=%s turn=%s outcome=%s", request.session_id, request.turn, outcome.model_dump())

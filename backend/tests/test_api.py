"""API tests use explicit fake model replies, never live Ollama or pathfinding."""

import asyncio
import copy
import json
from pathlib import Path

import httpx
import pytest
from fastapi.testclient import TestClient

from backend.app.errors import ApiError
from backend.app.main import create_app
from backend.app.ollama import OllamaClient, build_messages
from backend.app.schemas import Position, adjacent_move_schema

EXAMPLE = json.loads((Path(__file__).resolve().parents[2] / "examples/decide-request.json").read_text())


class FakeOllama:
    def __init__(self, reply='{"action":"move","x":1,"y":2}'):
        self.reply = reply
        self.messages = []

    async def generate(self, messages, move_schema=None):
        self.messages.append(copy.deepcopy(messages))
        if isinstance(self.reply, Exception):
            raise self.reply
        return self.reply


def report(position=None, status="moved", success=True, reason=None, turn=0):
    return {"session_id": EXAMPLE["session_id"], "turn": turn,
            "outcome": {"success": success, "status": status,
                        "position": position or {"x": 1, "y": 2}, "reason": reason}}


def test_health_and_successful_turn_history():
    model = FakeOllama()
    app = create_app(model)
    with TestClient(app) as client:
        assert client.get("/health").json() == {"status": "ok"}
        result = client.post("/decide", json=EXAMPLE)
        assert result.status_code == 200
        assert result.json()["move"] == {"action": "move", "x": 1, "y": 2}
        assert client.post("/decide", json=EXAMPLE).status_code == 409
        assert client.post("/outcome", json=report()).json()["recorded"] is True
        assert client.post("/outcome", json=report()).status_code == 409
        next_request = copy.deepcopy(EXAMPLE)
        next_request.update(turn=1, position={"x": 1, "y": 2}, previous_outcome=report()["outcome"])
        next_request["nearby"] = [{"x": 1, "y": 3, "tile": "floor"},
                                  {"x": 1, "y": 1, "tile": "floor"},
                                  {"x": 0, "y": 2, "tile": "wall"},
                                  {"x": 2, "y": 2, "tile": "floor"}]
        model.reply = '{"action":"move","x":1,"y":3}'
        assert client.post("/decide", json=next_request).status_code == 200
        prompt = json.loads(model.messages[-1][1]["content"])
        assert prompt["explored_history"][0]["outcome"] == report()["outcome"]
        assert prompt["observation"] == next_request
        assert "map" not in prompt


def test_wall_attempt_is_returned_and_position_stays_unchanged():
    app = create_app(FakeOllama('{"action":"move","x":2,"y":1}'))
    with TestClient(app) as client:
        assert client.post("/decide", json=EXAMPLE).json()["move"]["x"] == 2
        outcome = report({"x": 1, "y": 1}, "blocked", False, "wall")
        assert client.post("/outcome", json=outcome).status_code == 200
        session = app.state.sessions.sessions[EXAMPLE["session_id"]]
        assert session.position == EXAMPLE["position"]
        assert session.history[0]["outcome"] == outcome["outcome"]


@pytest.mark.parametrize("raw", [
    "not json", '```json\n{"action":"move","x":1,"y":2}\n```',
    '{"action":"move","x":2,"y":2}', '{"action":"move","x":1,"y":1}',
    '{"action":"move","x":-1,"y":1}', '{"action":"move","x":true,"y":2}',
    '{"action":"move","x":"1","y":2}', '{"action":"move","x":1.0,"y":2}',
    '{"action":"jump","x":1,"y":2}', '{"action":"move","x":1}',
    '{"action":"move","x":1,"y":2,"extra":1}',
])
def test_invalid_model_output_never_returns_move(raw):
    app = create_app(FakeOllama(raw))
    with TestClient(app) as client:
        response = client.post("/decide", json=EXAMPLE)
        assert response.status_code == 502
        assert response.json()["error"]["code"] == "invalid_model_output"
        assert "move" not in response.json()
        assert client.post("/decide", json=EXAMPLE).status_code == 409
        session = app.state.sessions.sessions[EXAMPLE["session_id"]]
        assert session.history[0]["raw_response"] == raw
        assert session.history[0]["requested_move"] is None


@pytest.mark.parametrize("field,value", [("turn", True), ("turn", -1), ("turn", "0"),
                                         ("session_id", ""), ("nearby", []), ("extra", 1)])
def test_bad_request_is_normalized_without_calling_model(field, value):
    model = FakeOllama()
    body = copy.deepcopy(EXAMPLE)
    body[field] = value
    with TestClient(create_app(model)) as client:
        response = client.post("/decide", json=body)
        assert response.status_code == 422
        assert response.json()["error"]["code"] == "invalid_request"
        assert model.messages == []


def test_bad_neighbors_and_malformed_json():
    model = FakeOllama()
    with TestClient(create_app(model)) as client:
        for tile in ["out_of_bounds", "goal"]:
            body = copy.deepcopy(EXAMPLE)
            body["nearby"][0]["tile"] = tile
            assert client.post("/decide", json=body).status_code == 422
        body = copy.deepcopy(EXAMPLE)
        body["nearby"][0] = body["nearby"][1]
        assert client.post("/decide", json=body).status_code == 422
        assert client.post("/decide", content="{", headers={"Content-Type": "application/json"}).status_code == 422
        assert model.messages == []


def test_edge_observations_accept_out_of_bounds():
    body = copy.deepcopy(EXAMPLE)
    body.update(start={"x": 0, "y": 0}, position={"x": 0, "y": 0})
    body["nearby"] = [{"x": -1, "y": 0, "tile": "out_of_bounds"},
                      {"x": 0, "y": -1, "tile": "out_of_bounds"},
                      {"x": 1, "y": 0, "tile": "floor"},
                      {"x": 0, "y": 1, "tile": "floor"}]
    with TestClient(create_app(FakeOllama('{"action":"move","x":0,"y":1}'))) as client:
        assert client.post("/decide", json=body).status_code == 200


def test_goal_outcome_and_new_session_reset():
    body = copy.deepcopy(EXAMPLE)
    body["goal"] = {"x": 1, "y": 2}
    body["nearby"][0]["tile"] = "goal"
    app = create_app(FakeOllama())
    with TestClient(app) as client:
        assert client.post("/decide", json=body).status_code == 200
        assert client.post("/outcome", json=report()).status_code == 409
        assert client.post("/outcome", json=report(status="goal_reached")).status_code == 200
        assert client.post("/decide", json=body).status_code == 409
        body["session_id"] = "new-run"
        assert client.post("/decide", json=body).status_code == 200
        assert len(app.state.sessions.sessions["new-run"].history) == 1


def test_sequence_and_outcome_consistency():
    with TestClient(create_app(FakeOllama())) as client:
        body = copy.deepcopy(EXAMPLE)
        body["turn"] = 1
        assert client.post("/decide", json=body).status_code == 409
        assert client.post("/outcome", json=report()).status_code == 409
        assert client.post("/decide", json=EXAMPLE).status_code == 200
        assert client.post("/outcome", json=report(turn=1)).status_code == 409
        assert client.post("/outcome", json=report({"x": 4, "y": 4})).status_code == 409
        invalid = report()
        invalid["outcome"]["reason"] = "wall"
        assert client.post("/outcome", json=invalid).status_code == 422
        assert client.post("/outcome", json=report()).status_code == 200
        body["previous_outcome"] = report()["outcome"]
        assert client.post("/decide", json=body).status_code == 409  # stale position


@pytest.mark.parametrize("failure,status,code", [
    (httpx.ConnectError("offline"), 503, "ollama_unavailable"),
    (httpx.ReadTimeout("slow"), 504, "ollama_timeout"),
    (httpx.Response(404, json={"error": "missing model"}), 502, "ollama_error"),
    (httpx.Response(200, json={"message": {"content": "x"}, "done": False}), 502, "ollama_error"),
    (httpx.Response(200, content="broken"), 502, "ollama_error"),
])
def test_ollama_transport_errors(failure, status, code):
    async def exercise():
        async def handler(request):
            if isinstance(failure, Exception):
                raise failure
            return failure
        async with httpx.AsyncClient(base_url="http://ollama", transport=httpx.MockTransport(handler)) as transport:
            with pytest.raises(ApiError) as caught:
                await OllamaClient(transport).generate([])
            assert (caught.value.status, caught.value.code) == (status, code)
    asyncio.run(exercise())


def test_ollama_payload_and_total_deadline():
    async def exercise():
        async def good(request):
            payload = json.loads(request.content)
            assert payload["model"] == "llama3.2:3b"
            assert payload["stream"] is False
            assert payload["format"]["additionalProperties"] is False
            return httpx.Response(200, json={"done": True, "message": {"content": "raw"}})
        async with httpx.AsyncClient(base_url="http://ollama", transport=httpx.MockTransport(good)) as transport:
            assert await OllamaClient(transport).generate([]) == "raw"
        async def slow(request):
            await asyncio.sleep(1)
        async with httpx.AsyncClient(base_url="http://ollama", transport=httpx.MockTransport(slow)) as transport:
            with pytest.raises(ApiError) as caught:
                await OllamaClient(transport, timeout=0.01).generate([])
            assert caught.value.code == "ollama_timeout"
    asyncio.run(exercise())


@pytest.mark.parametrize("status,code", [(503, "ollama_unavailable"), (504, "ollama_timeout")])
def test_model_failures_pause_session(status, code):
    app = create_app(FakeOllama(ApiError(status, code, "test error")))
    with TestClient(app) as client:
        response = client.post("/decide", json=EXAMPLE)
        assert response.status_code == status
        assert response.json() == {"error": {"code": code, "message": "test error"}}
        assert client.post("/decide", json=EXAMPLE).status_code == 409


def test_concurrent_decision_is_rejected_and_health_still_responds():
    async def exercise():
        started, release = asyncio.Event(), asyncio.Event()
        class SlowModel:
            async def generate(self, messages, move_schema=None):
                started.set()
                await release.wait()
                return '{"action":"move","x":1,"y":2}'
        app = create_app(SlowModel())
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
            first = asyncio.create_task(client.post("/decide", json=EXAMPLE))
            await started.wait()
            try:
                assert (await client.post("/decide", json=EXAMPLE)).status_code == 409
                assert (await client.get("/health")).status_code == 200
            finally:
                release.set()
            assert (await first).status_code == 200
    asyncio.run(exercise())


def test_unexpected_error_returns_safe_body():
    with TestClient(create_app(FakeOllama(RuntimeError("private details"))), raise_server_exceptions=False) as client:
        response = client.post("/decide", json=EXAMPLE)
        assert response.status_code == 500
        assert response.json()["error"]["code"] == "internal_error"
        assert "private details" not in response.text


@pytest.mark.parametrize("x,y,expected", [
    (1, 1, {(1, 2), (2, 1), (1, 0), (0, 1)}),
    (0, 0, {(0, 1), (1, 0)}),
    (9, 9, {(9, 8), (8, 9)}),
])
def test_generation_constraints_offer_all_adjacent_tiles_without_choosing(x, y, expected):
    schema = adjacent_move_schema(Position(x=x, y=y))
    offered = {(entry["properties"]["x"]["const"], entry["properties"]["y"]["const"])
               for entry in schema["oneOf"]}
    assert offered == expected
    assert all(entry["additionalProperties"] is False for entry in schema["oneOf"])
    # At (1,1), the east wall remains an allowed model attempt. This schema
    # enforces geometry, not wall knowledge or a hidden route.
    if (x, y) == (1, 1):
        assert (2, 1) in offered


def test_prompt_keeps_old_tile_knowledge_but_omits_raw_response_noise():
    history = []
    for turn in range(12):
        old = copy.deepcopy(EXAMPLE)
        old["turn"] = turn
        if turn == 0:
            old["position"] = {"x": 6, "y": 5}
            old["nearby"] = [{"x": 6, "y": 6, "tile": "wall"},
                             {"x": 7, "y": 5, "tile": "floor"},
                             {"x": 6, "y": 4, "tile": "floor"},
                             {"x": 5, "y": 5, "tile": "wall"}]
        history.append({"observation": old, "raw_response": "RAW_NOT_FOR_PROMPT",
                        "requested_move": {"action": "move", "x": 2, "y": 1},
                        "outcome": report({"x": 1, "y": 1}, "blocked", False, "wall")["outcome"]})
    messages = build_messages(EXAMPLE, history)
    context = json.loads(messages[1]["content"])
    assert context["walkable_neighbors"] == [{"x": 1, "y": 2, "visits": 0}]
    assert {"x": 6, "y": 6, "tile": "wall", "visits": 0} in context["known_tiles"]
    assert len(context["explored_history"]) == 8
    assert context["explored_history"][0]["turn"] == 4
    assert "RAW_NOT_FOR_PROMPT" not in messages[1]["content"]
    supplied_tiles = {(n["x"], n["y"]) for record in history for n in record["observation"]["nearby"]}
    supplied_tiles.update((n["x"], n["y"]) for n in EXAMPLE["nearby"])
    assert {(n["x"], n["y"]) for n in context["known_tiles"]} == supplied_tiles


def test_prompt_counts_actual_entries_not_blocked_attempts():
    history = [{"observation": EXAMPLE, "requested_move": {"action": "move", "x": 1, "y": 2},
                "outcome": report()["outcome"], "raw_response": "unused"}]
    context = json.loads(build_messages(EXAMPLE, history)[1]["content"])
    assert context["walkable_neighbors"] == [{"x": 1, "y": 2, "visits": 1}]


def test_ollama_receives_the_current_position_constraints():
    async def exercise():
        schema = adjacent_move_schema(Position(x=1, y=1))
        async def handler(request):
            assert json.loads(request.content)["format"] == schema
            return httpx.Response(200, json={"done": True, "message": {"content": "raw"}})
        async with httpx.AsyncClient(base_url="http://ollama", transport=httpx.MockTransport(handler)) as transport:
            assert await OllamaClient(transport).generate([], schema) == "raw"
    asyncio.run(exercise())

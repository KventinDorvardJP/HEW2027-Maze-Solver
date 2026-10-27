"""Async local inference only: no navigation fallback."""

import asyncio
import json

import httpx

from .errors import ApiError
from .schemas import Move

SYSTEM_PROMPT = """Choose ONE move for an agent exploring a 10x10 maze.
Coordinates: right = x+1, left = x-1, up = y+1, down = y-1.
The goal is a destination to eventually reach, NOT a coordinate you can jump to.

Read walkable_neighbors: these are the immediately adjacent tiles you can enter
this turn. Choose one of those coordinates. Never choose a wall, your current
position, a diagonal, or a distant tile. A blocked attempt did NOT change your
position. Do not repeat it.

Explore: prefer a neighbor you have not visited. If all neighbors were visited,
prefer one with fewer visits so you can backtrack and leave loops. Moving away
from the goal is allowed when walls require it. Use known_tiles and actual
outcomes. Only observed tiles are known; do not invent the hidden maze.

Return ONLY {"action":"move","x":INTEGER,"y":INTEGER} for your chosen neighbor.
Unity will validate and report what actually happened. You choose the move;
there is no fallback solver."""


def build_messages(observation: dict, history: list[dict]) -> list[dict]:
    # Summarize only Unity's observations and actual outcomes. This is memory,
    # not a route planner: no neighbor is ranked, selected, or substituted.
    known = {}
    start = observation["start"]
    visits = {(start["x"], start["y"]): 1}
    explored_history = []
    for record in history:
        old = record["observation"]
        for tile in old["nearby"]:
            known[(tile["x"], tile["y"])] = tile["tile"]
        outcome = record["outcome"]
        if outcome is not None and outcome["success"]:
            p = outcome["position"]
            key = (p["x"], p["y"])
            visits[key] = visits.get(key, 0) + 1
        explored_history.append({
            "turn": old["turn"], "position_before": old["position"],
            "requested_move": record["requested_move"], "outcome": outcome,
        })
    for tile in observation["nearby"]:
        known[(tile["x"], tile["y"])] = tile["tile"]
    walkable = [
        {"x": tile["x"], "y": tile["y"],
         "visits": visits.get((tile["x"], tile["y"]), 0)}
        for tile in observation["nearby"] if tile["tile"] in ("floor", "goal")
    ]
    return [
        {"role": "system", "content": SYSTEM_PROMPT},
        {"role": "user", "content": json.dumps({
            "walkable_neighbors": walkable,
            "observation": observation,
            "known_tiles": [{"x": x, "y": y, "tile": tile, "visits": visits.get((x, y), 0)}
                            for (x, y), tile in sorted(known.items())],
            "explored_history": explored_history[-8:],
        }, separators=(",", ":"))},
    ]


class OllamaClient:
    def __init__(self, client: httpx.AsyncClient, timeout: float = 60):
        self.client = client
        self.timeout = timeout

    async def generate(self, messages: list[dict], move_schema: dict | None = None) -> str:
        try:
            # asyncio.timeout sets a total deadline, beyond HTTPX's per-I/O limits.
            async with asyncio.timeout(self.timeout):
                response = await self.client.post(
                    "/api/chat",
                    json={
                        "model": "llama3.2:3b",
                        "messages": messages,
                        "stream": False,
                        "format": move_schema if move_schema is not None else Move.model_json_schema(),
                        "options": {"temperature": 0, "num_predict": 128},
                    },
                )
                response.raise_for_status()
                body = response.json()
                raw = body["message"]["content"]
                if not isinstance(raw, str) or body.get("done") is not True:
                    raise ValueError("Incomplete or invalid Ollama response")
                return raw
        except (TimeoutError, httpx.TimeoutException) as exc:
            raise ApiError(504, "ollama_timeout", "The local model did not respond within 60 seconds.") from exc
        except httpx.HTTPStatusError as exc:
            raise ApiError(502, "ollama_error", "Ollama returned an error. Check that llama3.2:3b is installed.") from exc
        except httpx.RequestError as exc:
            raise ApiError(503, "ollama_unavailable", "Cannot reach Ollama at 127.0.0.1:11434. Start Ollama and reset.") from exc
        except (ValueError, KeyError, TypeError) as exc:
            raise ApiError(502, "ollama_error", "Ollama returned an unusable response.") from exc

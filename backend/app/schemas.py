"""Version 0.3 HTTP objects. Unity remains authoritative about the maze."""

from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

Coordinate = Annotated[int, Field(strict=True, ge=0, le=9)]
Turn = Annotated[int, Field(strict=True, ge=0)]
SessionId = Annotated[str, Field(strict=True, min_length=1, max_length=128)]


class Message(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)


class Position(Message):
    x: Coordinate
    y: Coordinate


class Neighbor(Message):
    x: int
    y: int
    tile: Literal["floor", "wall", "goal", "out_of_bounds"]


class Move(Message):
    action: Literal["move"]
    x: Coordinate
    y: Coordinate


def adjacent_move_schema(position: Position) -> dict:
    """Constrain model syntax to the legal geometry, including wall attempts.

    All adjacent in-bounds coordinates are offered without ranking. No maze
    data or route is used, and this function never chooses a destination.
    """
    choices = []
    for x, y in ((position.x, position.y + 1), (position.x + 1, position.y),
                 (position.x, position.y - 1), (position.x - 1, position.y)):
        if 0 <= x <= 9 and 0 <= y <= 9:
            choices.append({
                "type": "object",
                "properties": {
                    "action": {"type": "string", "const": "move"},
                    "x": {"type": "integer", "const": x},
                    "y": {"type": "integer", "const": y},
                },
                "required": ["action", "x", "y"],
                "additionalProperties": False,
            })
    return {"oneOf": choices}


class Outcome(Message):
    success: bool
    status: Literal["moved", "blocked", "invalid_move", "goal_reached"]
    position: Position
    reason: Literal["wall", "out_of_bounds", "not_adjacent"] | None

    @model_validator(mode="after")
    def check_status(self):
        if self.status in ("moved", "goal_reached"):
            valid = self.success and self.reason is None
        elif self.status == "blocked":
            valid = not self.success and self.reason == "wall"
        else:
            valid = not self.success and self.reason in ("out_of_bounds", "not_adjacent")
        if not valid:
            raise ValueError("success and reason must match status")
        return self


class DecisionRequest(Message):
    session_id: SessionId
    turn: Turn
    start: Position
    goal: Position
    position: Position
    nearby: Annotated[list[Neighbor], Field(min_length=4, max_length=4)]
    previous_outcome: Outcome | None

    @model_validator(mode="after")
    def check_observations(self):
        if self.start == self.goal:
            raise ValueError("start and goal must differ")
        x, y = self.position.x, self.position.y
        expected = {(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)}
        if {(n.x, n.y) for n in self.nearby} != expected:
            raise ValueError("nearby must contain exactly the four adjacent coordinates")
        for n in self.nearby:
            in_bounds = 0 <= n.x <= 9 and 0 <= n.y <= 9
            if (n.tile == "out_of_bounds") == in_bounds:
                raise ValueError("out_of_bounds must match the coordinate bounds")
            if in_bounds and ((n.tile == "goal") != ((n.x, n.y) == (self.goal.x, self.goal.y))):
                raise ValueError("goal observations must match the goal coordinate")
            if (n.x, n.y) == (self.start.x, self.start.y) and n.tile != "floor":
                raise ValueError("the start tile must be floor")
        return self


class DecisionResponse(Message):
    session_id: SessionId
    turn: Turn
    move: Move


class OutcomeRequest(Message):
    session_id: SessionId
    turn: Turn
    outcome: Outcome


class OutcomeResponse(Message):
    session_id: SessionId
    turn: Turn
    recorded: Literal[True] = True

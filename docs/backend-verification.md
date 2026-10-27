# Version 0.3 backend verification - 2026-10-27

This is a snapshot of the backend increment. The subsequent Unity milestone is
recorded in [Unity verification](unity-verification.md); Unity items listed as
remaining below describe the state at the end of this earlier increment.

## Scope and acceptance criteria

This increment implements the Python portion of the protocol draft: health,
real local model requests, strict move validation, actual-outcome recording,
session history, and safe error responses. It does not implement the Unity game.

Acceptance for this increment: backend tests pass, and a real HTTP decision
request reaches the installed `llama3.2:3b` model and returns a validated move.

## Changes

- `backend/app/schemas.py`: JSON schemas and observation/outcome validation.
- `backend/app/ollama.py`: asynchronous local Ollama HTTP client and total timeout.
- `backend/app/sessions.py`: isolated session histories and turn sequencing.
- `backend/app/main.py`: endpoints and consistent application error responses.
- `backend/tests/test_api.py`: API/state and Ollama transport tests.
- `backend/smoke_decision.py`: repeatable one-decision real-model check.
- `examples/`: sample decision and outcome requests.
- README/protocol: root-environment commands, behavior, and limitations.

## Checks actually run

Environment: existing repository-root `.venv`, Python **3.14.0**, Windows.
Project documentation targets Python 3.14.10; that exact version was not tested.

From the repository root:

```powershell
.\.venv\Scripts\python.exe -m pytest backend/tests -q
```

Result: **33 passed**. One installed Starlette/HTTPX deprecation warning.
Tests use fake model replies and mocked network failures. They cover valid
decisions, history, blocked-outcome recording, invalid JSON/moves/observations,
session sequencing, goal outcomes, fresh sessions, Ollama transport failures,
total timeout, concurrent requests, and safe unexpected-error responses.

Started the actual HTTP server:

```powershell
.\.venv\Scripts\python.exe -m uvicorn backend.app.main:app --host 127.0.0.1 --port 8000
```

Then ran:

```powershell
.\.venv\Scripts\python.exe backend\smoke_decision.py
```

Actual health response: `{"status":"ok"}`.
Actual decision HTTP status: `200`.
Actual model-selected move: `{"action":"move","x":2,"y":1}` from `(1,1)`.

That destination is a wall in the sample observation. The move is adjacent and
in bounds, so Python correctly returned the model's request unchanged. Unity
must reject it as blocked when the Unity client exists. No real movement or
outcome was fabricated during this smoke check. This verifies connectivity and
validation, not model navigation quality or maze completion.

The smoke run initially revealed a Python docstring escape warning; that
docstring was corrected to a raw string. The temporary server was stopped after
the check. `git diff --check` was also run.

## Limitations and remaining work

- Team review of the protocol draft is still pending.
- No Unity code, movement, collision, reset UI, goal UI, or visible decision log
  has been implemented or tested in this increment.
- History is in memory, unpruned, and process-local. Use one backend worker.
  Backend restart/reload requires a new session.
- API outcome reports are supplied by the client; Python has no hidden map with
  which to verify that a wall or actual movement report is truthful.
- The prototype requires reset/new session after errors; there are no automatic
  retries or persistence.
- Next increment: fixed 10 x 10 Unity maze and a non-blocking Step button that
  calls `/decide`, validates/applies movement, displays the result, and reports
  it to `/outcome` before the next turn.

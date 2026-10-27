# Changelog

Implementation history for HEW2027 / IA12A-03 LLM Maze Solver.
Entries distinguish implemented behavior, verification, and outstanding work.

## [0.3 prototype - Unreleased] - 2026-10-27

Today's work implements the Version 0.3 prototype scope. This entry does not
represent a published release or completed team acceptance.

### Added

- A draft Unity-Python JSON contract in [docs/protocol.md](docs/protocol.md),
  covering coordinates, observations, moves, actual outcomes, errors, timeouts,
  turn sequencing, session history, and reset behavior.
- Python `POST /decide` and `POST /outcome` endpoints alongside the existing
  `GET /health` endpoint.
- Asynchronous local Ollama integration using `llama3.2:3b`, structured JSON
  output, a total decision timeout, and explicit connection/model error handling.
- Strict validation for requests, observations, model replies, and reported
  outcomes. Invalid replies produce errors rather than replacement moves.
- In-memory session histories recording observations, raw model replies,
  validated move requests, actual outcomes, and errors.
- Example decision/outcome JSON files and `backend/smoke_decision.py` for a
  repeatable real-model connectivity check.
- A fixed 10 x 10 Unity maze with start `(1,1)`, goal `(8,8)`, walls, and a
  visible agent, wired into `Assets/Scenes/SampleScene.unity`.
- Unity-authoritative movement, wall rejection, bounds/adjacency checks, reset,
  and goal detection.
- Runtime Step, Run, Pause, Reset, and Check backend controls, visible status,
  and a scrollable observation/decision/outcome log.
- A non-blocking Unity HTTP client that reports actual outcomes before the
  next turn and ignores responses from a session that has been reset.
- Backend tests and Unity EditMode/PlayMode test assemblies, including an
  opt-in real-model progress test.
- [Unity demo instructions](docs/unity-demo.md), verification records, and
  screenshots captured from the actual running scene.

### Changed

- Corrected setup/run instructions to use the repository-root `.venv` and
  `requirements.txt`; the dependency file remains at the repository root.
- Added Unity's official Newtonsoft JSON package 3.2.2 and its lock entry for
  explicit JSON null handling and response-type validation.
- Enabled Both input systems for the prototype's IMGUI controls and plain HTTP
  for communication with the local backend.
- Added `.verification/` to `.gitignore` for isolated Unity test copies and
  generated verification artifacts. Added asset metadata for new Unity assets.

### Fixed

- Reproduced repeated wall attempts at the start, followed by an invalid model
  reply. Improved the prompt with explicit walkable neighbors, factual visit
  counts, blocked-outcome explanations, and exploration guidance.
- Condensed inference history to the last eight turns while preserving all
  observed tile knowledge and visit counts. Full records remain in backend
  session history; raw response noise is omitted from the model prompt.
- Constrained generated move coordinates to the current in-bounds orthogonal
  neighbors, including wall attempts. The LLM still selects the destination;
  no hidden map, route planner, or deterministic fallback is used.
- Strengthened live Unity verification to require movement across multiple
  turns. An acknowledged blocked turn alone no longer passes the progress test.
- Corrected JSON references in Unity test assemblies, a Python docstring escape
  warning, and screenshot verification so it requires an image from the current
  test run. Rendered-editor verification replaced the failed batch screenshot
  attempt.

### Verified today

- Backend: **39 tests passed** after the navigation fix. The installed
  Starlette/HTTPX stack emitted one deprecation warning.
- Unity: **17 EditMode tests passed**; the updated **10 PlayMode tests passed**,
  including the real-model progress test.
- The initial real wall request was rejected by Unity, with position unchanged
  and the actual blocked outcome acknowledged by Python.
- After the navigation fix, real Unity/Python/Ollama turns moved the agent
  `(1,1) -> (1,2) -> (2,2) -> (3,2)`, with each actual outcome acknowledged.
- A separate 50-turn diagnostic fixture using real Ollama outputs recorded
  36 moves, 14 blocked attempts, and no invalid-output errors. It did not reach
  the goal. These fixture outcomes are distinct from the live Unity results.
- Actual rendered screenshots were captured and visually inspected; asset
  metadata and diff whitespace were checked.
- Unity verification used **6000.3.26f1**. Backend verification used the existing
  **Python 3.14.0** environment; the documented target **3.14.10** was not tested.

Detailed evidence: [backend](docs/backend-verification.md),
[Unity](docs/unity-verification.md), and
[navigation fix](docs/navigation-verification.md).

### Limitations and remaining work

- Reliable maze solving has not been demonstrated. The model can loop, request
  walls, or return errors. Goal handling was tested with test-only inputs; the
  real model has not been shown to complete this maze.
- No standalone Windows executable has been built or tested. Automated Unity
  tests exercise control methods rather than mouse clicks.
- Backend history is process-local, unpruned, and lost on restart. A new Unity
  session is required after a backend restart; errors require reset rather than
  automatic retries. The visible Unity log retains the last 200 entries.
- Team review of the protocol and manual acceptance on teammates' machines
  remain outstanding.
- Version 0.5 and Version 1.0 features are not implemented.

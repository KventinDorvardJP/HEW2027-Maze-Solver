# HEW2027 — LLM Maze Solver

**Team:** IA12A-03  
**Target platform:** Windows PC  
**Current target:** Version 0.3 (first working prototype)

A 2D maze simulation in which a **local large language model (LLM)** tries to reach the goal by choosing one neighboring tile at a time. The LLM receives only its position, the goal, nearby observations, and feedback from its previous move; it does not receive the entire hidden maze.

**Unity is authoritative:** the LLM requests a move, but Unity decides whether the move is valid, updates the game state, and reports the actual result.

## Technology

| Component | Technology | Role |
| --- | --- | --- |
| Game | Unity 6 **6000.3.26f1 LTS**, 2D (Built-In Render Pipeline), C# | Maze display, movement, collision checks, UI, goal detection |
| Backend | Python **3.14.10** (64-bit), FastAPI, Uvicorn, HTTPX | HTTP API, prompts, LLM response validation, decision history |
| Local AI | Ollama with `llama3.2:3b` | Selects the next attempted move |
| Communication | HTTP + JSON over `localhost` | Connects Unity, Python, and Ollama |
| Collaboration | Git + GitHub | Source control and team coordination |

All developers should use the same Unity editor version. Keep Python dependencies recorded in `backend/requirements.txt`.

## Repository layout

```text
HEW2027-Maze-Solver/
├── unity/                  # Entire Unity project
│   ├── Assets/
│   ├── Packages/
│   └── ProjectSettings/
├── backend/                # Python API and Ollama integration
│   ├── app/
│   ├── tests/
│   └── requirements.txt
├── docs/                   # Requirements, architecture, protocol, setup
├── examples/               # Sample requests and responses
├── .gitignore
└── README.md
```

Some folders or files above may be added as development progresses; this is the intended structure, not a claim that every component is already implemented.

## Setup (Windows)

### 1. Clone the repository

Clone this GitHub repository using GitHub Desktop or Git, then open a terminal at the repository root.

### 2. Open the Unity project

1. Install **Unity Hub** and Unity Editor **6000.3.26f1 LTS**.
2. In Unity Hub, select **Add project from disk** and choose the `unity/` folder.
3. Open the project. It uses the **2D (Built-In Render Pipeline)** template.
4. Under **Edit → Project Settings**, verify:
   - **Version Control → Mode:** `Visible Meta Files` (if this option is shown).
   - **Editor → Asset Serialization → Mode:** `Force Text`.

Always commit Unity `.meta` files. Do **not** commit generated folders such as `unity/Library/`, `unity/Temp/`, or `unity/Logs/`. The Unity project should have a Unity-specific `.gitignore` in `unity/`.

### 3. Set up Python

Install **Python 3.14.10 (64-bit)**. In PowerShell, from the repository root:

```powershell
cd backend
py -3.14 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

The `requirements.txt` file must exist before the install command. If it has not been created yet, the initial backend setup is:

```powershell
.\.venv\Scripts\python.exe -m pip install fastapi uvicorn httpx pytest
.\.venv\Scripts\python.exe -m pip freeze > requirements.txt
```

Commit `requirements.txt`, **not** `.venv/`.

### 4. Run the Python API

Once `backend/app/main.py` defines a FastAPI application named `app`, run this from `backend/`:

```powershell
.\.venv\Scripts\python.exe -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8000
```

For an initial connectivity test, the backend may implement `GET /health` returning `{"status":"ok"}`. If that endpoint has been implemented, visit <http://127.0.0.1:8000/health>.

**Note:** The health endpoint confirms the Python server responds; it does not demonstrate that the LLM navigation loop works.

### 5. Install and check Ollama

Install [Ollama](https://ollama.com/download) and run:

```powershell
ollama pull llama3.2:3b
ollama run llama3.2:3b
```

Ollama normally exposes its local API at `http://127.0.0.1:11434`. The Python backend is a **separate** service at `http://127.0.0.1:8000`. Ollama must be running for actual model decisions.

## How a navigation turn works

1. **Unity** sends the current position, goal, nearby tiles, and previous outcome to **Python**.
2. **Python** builds a prompt and asks **Ollama** for the next attempted move.
3. **Python** validates the model response and returns the requested coordinate to Unity.
4. **Unity** checks that the request moves exactly one tile up, down, left, or right and applies collision rules.
5. **Unity** logs the actual outcome, then sends the next observation. Repeat until the goal is reached or the simulation stops.

Example proposed move request:

```json
{"action":"move","x":3,"y":4}
```

Example blocked outcome reported by Unity:

```json
{"success":false,"status":"blocked","position":{"x":3,"y":3},"reason":"wall"}
```

The complete Unity–Python request/response schema and failure format belong in `docs/protocol.md`. Do not expose the full hidden maze or a precomputed optimal route to the model. An invalid LLM response, timeout, or backend failure must be reported as an error; **do not silently substitute scripted pathfinding**.

## Milestones and scope

### Version 0.3 — First working prototype (**current target**)

- Fixed **10 × 10** maze with fixed start and goal.
- **Walls only**; blocked movement leaves the position unchanged.
- Visible maze, agent, start, goal, and decision/outcome log.
- Real Unity ↔ Python ↔ Ollama decision loop.
- Reset, goal detection, and clear error handling.
- Completion does **not** require the LLM to solve every run.

### Version 0.5 — Extended prototype (**later**)

- Fixed **15 × 15** maze.
- Add **pits** (impassable), **fire** (resets to start), and **linked teleports**.
- Preserve decision history after resets and teleports.

### Version 1.0 — Original requirements (**later**)

- High priority: user-editable maze, selectable start/goal, LLM navigation.
- Medium priority: exploration visualization and reset.
- Low priority: exploration animation speed control.
- Out of scope: 3D, point-budget system, character selection, and unapproved extra trap/effect mechanics. Retain Version 0.5 special tiles in 1.0 only if agreed by the team/project owner.

The Version 1.0 requirements document takes precedence over the original proposal in case of conflict.

## Team Git workflow

1. Pull the latest `main` branch before starting work.
2. Create a short-lived branch for your task, e.g. `feature/unity-grid` or `feature/python-api`.
3. Make focused commits with descriptive messages.
4. Push your branch and open a Pull Request.
5. Ask another team member to review it before merging to `main`.

Avoid editing the same Unity scene in different branches at the same time. Coordinate changes to shared scenes and project settings. Never commit credentials, local virtual environments, generated Unity caches, or Ollama model files.

## Testing and current status

**Current status:** Repository structure and development instructions are specified here. This README does **not** claim that the game, backend, full LLM loop, or automated tests have been implemented or verified.

Recommended first smoke tests:

- Unity project opens on a teammate's machine without missing assets.
- Python `GET /health` responds once implemented.
- Unity can call the Python endpoint without freezing.
- Backend calls the actual Ollama model and validates its JSON response.
- Unity rejects invalid moves and reports the true outcome.
- A full Version 0.3 run logs observations, decisions, and outcomes.

## Project documents

See `docs/` for the approved requirements, the proposed JSON protocol, and additional technical notes as these files are added.

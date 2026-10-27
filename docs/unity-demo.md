# Version 0.3 Unity demo

## Start the demo

1. Keep the Ollama app running. The installed model must be `llama3.2:3b`.
2. Open PowerShell at the repository root and start Python:

   ```powershell
   .\.venv\Scripts\python.exe -m uvicorn backend.app.main:app --host 127.0.0.1 --port 8000
   ```

3. Open `unity/` in Unity **6000.3.26f1**. Allow Package Manager to resolve the
   official `com.unity.nuget.newtonsoft-json` package (3.2.2).
4. If Unity asks to restart for the input-handling setting change, accept the
   restart. The prototype uses **Both** input systems so its IMGUI buttons work
   in a Windows player as well as in the editor.
5. In the Project panel, open **Assets > Scenes > SampleScene**.
6. Press Unity's **Play** button. Select the **Game** tab. The maze and controls
   appear at runtime; no components or buttons need manual Inspector wiring.
   A 16:10 Game view aspect ratio or a maximized Game tab makes the text easier
   to read; the view also scales to other window sizes.
7. Click **Check backend**, then **Step**. The maze remains responsive while the
   local model thinks. A successful health check checks Python only.

## Controls and expected behavior

- **Step:** request one real model decision, validate/apply it in Unity, and
  report the actual outcome to Python. Wait for acknowledgement before another
  step. A wall request is logged as `blocked` and leaves the agent in place.
- **Run:** repeat those turns, one at a time, with a short pause between them.
- **Pause:** stop automatic turns after the current turn finishes. It does not
  discard an already requested decision or its outcome report.
- **Reset:** abort the pending HTTP request where possible, immediately return
  to `(1,1)`, clear the visible log, and generate a new session ID. Responses
  from an old run cannot move the new agent.
- **Goal:** reaching `(8,8)` reports `goal_reached` before ending the run.
- **Errors:** stop with visible feedback and no replacement move. Reset starts
  a new session. If an outcome report fails after an actual move, the agent
  stays at the position Unity already reached.

Walls are gray, the start is blue, the goal is green, and the agent is orange.
Coordinates use a bottom-left origin. The full map is a spectator view. Only
the four neighboring tiles, positions, and previous outcomes go to Python.
The LLM may repeatedly attempt a wall or fail to finish; no solver takes over.

The visible log keeps the most recent 200 entries. Full per-session backend
history is in memory until the backend restarts. Raw model replies appear in
the backend console; Unity displays validated requests and actual outcomes.

## Implementation

- `Assets/Scripts/MazeBoard.cs`: fixed map, local observations, collision,
  movement, reset, and goal detection. It never selects a move.
- `Assets/Scripts/MazeProtocol.cs`: JSON messages and strict response checks.
- `Assets/Scripts/MazeTransport.cs`: non-blocking UnityWebRequest coroutines,
  timeouts, and cancellation.
- `Assets/Scripts/MazeGame.cs`: session lifecycle, controls, and runtime view.
- `Assets/Scenes/SampleScene.unity`: camera and wired game controller.
- `Assets/Tests/`: independent movement/protocol and coroutine lifecycle tests.

Project settings enable plain HTTP for the local API and **Both** input systems
for the prototype's IMGUI controls. No tile art, paid services, or model weights
are added. Newtonsoft JSON preserves required JSON nulls and checks response
types rather than silently substituting missing integer fields.

## Run Unity tests

In Unity, use **Window > General > Test Runner**. Run **EditMode** and
**PlayMode** separately. Ordinary tests use explicit fake HTTP responses, not
fake model decisions presented as real gameplay.

`RealBackendAndModelMakeProgress` is opt-in. To run it through Unity's
test runner, launch the editor with `MAZE_LIVE_TEST=1` in its environment and
start Python/Ollama first. It loads the actual SampleScene, checks health,
requests three real decisions, applies them, and reports their actual outcomes.
It requires movement to at least three distinct positions, so repeated blocked
requests alone cannot pass. `MAZE_BACKEND_URL` can select an isolated test server;
the regular game defaults to `http://127.0.0.1:8000`.
`MAZE_SCREENSHOT` optionally specifies an absolute PNG path for that test;
graphics must be enabled for screenshot capture.

See [the verification record](unity-verification.md) for actual test results and
the screenshot captured from the real model integration check.
The later [navigation fix](navigation-verification.md) demonstrates actual
movement past the start and gives restart instructions for an already running
backend. The model can still loop; reliable solving has not been demonstrated.

Unity API references: [UnityWebRequest](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Networking.UnityWebRequest.html),
[IMGUI input support](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/manual/UISupport.html),
and [Newtonsoft JSON package](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html).

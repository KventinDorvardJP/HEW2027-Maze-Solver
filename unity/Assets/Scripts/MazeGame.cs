using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MazeSolver
{
    public sealed class MazeGame : MonoBehaviour
    {
        public string backendUrl = "http://127.0.0.1:8000";
        public MazeBoard Board { get; private set; }
        public string SessionId { get; private set; }
        public int Turn { get; private set; }
        public bool Busy { get; private set; }
        public bool Running { get; private set; }
        public bool Failed { get; private set; }
        public string Status { get; private set; }
        public readonly List<string> Log = new List<string>();
        public IMazeTransport Transport { get; set; }
        private JObject previous;
        private int generation;
        private float requestStarted;
        private Vector2 logScroll;
        private GUIStyle title, heading, text, small, tileText, button;

        private void Awake()
        {
            Transport = new MazeTransport(backendUrl);
            ResetRun();
        }

        public void ResetRun()
        {
            ++generation;
            Transport?.Cancel();
            Board = new MazeBoard();
            SessionId = Guid.NewGuid().ToString();
            Turn = 0;
            previous = null;
            Busy = Running = Failed = false;
            Status = "Ready. Press Step to ask the local model for one move.";
            Log.Clear();
            AddLog("NEW RUN  " + SessionId);
            logScroll = Vector2.zero;
        }

        private void OnDisable()
        {
            ++generation;
            Transport?.Cancel();
            Busy = Running = false;
        }

        public void StepOnce()
        {
            if (!CanStep()) return;
            StartCoroutine(Decisions(generation));
        }

        public void ToggleRun()
        {
            if (Running) { Running = false; return; }
            if (!CanStep()) return;
            Running = true;
            StartCoroutine(Decisions(generation));
        }

        private bool CanStep() => !Busy && !Failed && Board != null && !Board.AtGoal;

        private IEnumerator Decisions(int run)
        {
            do
            {
                Busy = true;
                requestStarted = Time.realtimeSinceStartup;
                yield return Attempt(run);
                if (run != generation) yield break;
                if (Failed || Board.AtGoal) Running = false;
                if (Running) yield return new WaitForSecondsRealtime(0.35f);
            } while (run == generation && Running);
            if (run == generation) Busy = false;
        }

        private IEnumerator Attempt(int run)
        {
            string session = SessionId;
            int turn = Turn;
            var observation = MazeProtocol.Observation(session, turn, Board, previous);
            AddLog("TURN " + turn + "  OBS " + Board.Position + "  " + observation["nearby"].ToString(Formatting.None));
            Status = "Waiting for Ollama to choose a move...";
            HttpReply reply = null;
            yield return Transport.Send("/decide", observation.ToString(Formatting.None), 75, value => reply = value);
            if (run != generation) yield break;
            if (!CheckReply(reply)) yield break;

            Vector2Int destination = default;
            try { destination = MazeProtocol.Decision(reply.Body, session, turn); }
            catch (Exception exc) { Fail("Invalid decision response: " + exc.Message); }
            if (Failed) yield break;
            AddLog("TURN " + turn + "  REQUEST " + destination);
            previous = Board.Apply(destination);
            AddLog("TURN " + turn + "  OUTCOME " + previous.ToString(Formatting.None));
            Status = "Recording " + (string)previous["status"] + " at " + Board.Position + "...";
            var report = new JObject { ["session_id"] = session, ["turn"] = turn, ["outcome"] = previous.DeepClone() };
            reply = null;
            yield return Transport.Send("/outcome", report.ToString(Formatting.None), 10, value => reply = value);
            if (run != generation) yield break;
            if (!CheckReply(reply)) yield break;
            try { MazeProtocol.Acknowledgement(reply.Body, session, turn); }
            catch (Exception exc) { Fail("Invalid outcome acknowledgement: " + exc.Message); }
            if (Failed) yield break;
            ++Turn;
            if ((string)previous["status"] == "invalid_move")
            {
                Fail("Backend returned an illegal destination; Unity rejected it. Reset to start a new run.");
                yield break;
            }
            Status = Board.AtGoal ? "Goal reached. Final outcome recorded!" :
                "Turn " + turn + ": " + (string)previous["status"] + " at " + Board.Position + ". Ready for the next decision.";
        }

        private bool CheckReply(HttpReply reply)
        {
            if (reply != null && reply.Success) return true;
            Fail(reply == null ? "The HTTP request did not return a result." : MazeProtocol.Error(reply.Body, reply.Error));
            return false;
        }

        private void Fail(string message)
        {
            Failed = true;
            Running = false;
            Status = message + "  Press Reset to try a new session.";
            AddLog("ERROR  " + message);
        }

        private void AddLog(string message)
        {
            Log.Add(message);
            if (Log.Count > 200) Log.RemoveAt(0);
            logScroll.y = float.MaxValue;
            Debug.Log("[Maze] " + message);
        }

        public void CheckHealth()
        {
            if (Busy || Running || Failed) return;
            Busy = true;
            requestStarted = Time.realtimeSinceStartup;
            StartCoroutine(Health(generation));
        }

        private IEnumerator Health(int run)
        {
            Status = "Checking Python backend...";
            HttpReply reply = null;
            yield return Transport.Send("/health", null, 10, value => reply = value);
            if (run != generation) yield break;
            if (CheckReply(reply))
            {
                try
                {
                    var body = MazeProtocol.ParseObject(reply.Body);
                    if (body["status"]?.Type != JTokenType.String || (string)body["status"] != "ok")
                        throw new FormatException("Expected status ok.");
                    Status = "Python backend is reachable. Press Step to test a real model decision.";
                    AddLog("HEALTH  ok (model not checked)");
                }
                catch (Exception exc) { Fail("Invalid health response: " + exc.Message); }
            }
            Busy = false;
        }

        // IMGUI keeps this prototype self-contained: no inspector wiring, scene
        // dependencies, or extra art assets. All navigation lives in the model.
        private void OnGUI()
        {
            if (Board == null) return;
            InitStyles();
            float scale = Mathf.Min(Screen.width / 1160f, Screen.height / 760f);
            var original = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1160 * scale) / 2,
                (Screen.height - 760 * scale) / 2), Quaternion.identity, Vector3.one * scale);
            Fill(new Rect(0, 0, 1160, 760), new Color(0.055f, 0.075f, 0.11f));
            GUI.Label(new Rect(28, 20, 820, 42), "LOCAL LLM / MAZE SOLVER", title);
            GUI.Label(new Rect(30, 65, 1060, 28), "VERSION 0.3     10 x 10     llama3.2:3b     Unity validates every requested move", small);
            DrawBoard();
            GUI.Label(new Rect(30, 671, 550, 30), "START (1,1)     GOAL (8,8)     AGENT = orange", small);
            GUI.Label(new Rect(30, 703, 550, 26), "Spectator map. The model sees only adjacent tiles + history.", small);
            Fill(new Rect(610, 108, 520, 620), new Color(0.085f, 0.11f, 0.16f));
            GUI.Label(new Rect(632, 123, 470, 30), "DECISION CONTROL", heading);
            bool enabled = GUI.enabled;
            GUI.enabled = CanStep() && !Running;
            if (GUI.Button(new Rect(632, 164, 98, 38), "Step", button)) StepOnce();
            GUI.enabled = Running || CanStep();
            if (GUI.Button(new Rect(740, 164, 98, 38), Running ? "Pause" : "Run", button)) ToggleRun();
            GUI.enabled = enabled;
            if (GUI.Button(new Rect(848, 164, 98, 38), "Reset", button)) ResetRun();
            GUI.enabled = !Busy && !Running && !Failed;
            if (GUI.Button(new Rect(956, 164, 152, 38), "Check backend", button)) CheckHealth();
            GUI.enabled = enabled;
            GUI.Label(new Rect(632, 217, 475, 58), Status, text);
            GUI.Label(new Rect(632, 282, 475, 25), "Position " + Board.Position + "     Completed turns " + Turn +
                (Busy ? "     Waiting " + (Time.realtimeSinceStartup - requestStarted).ToString("0") + "s" : ""), small);
            GUI.Label(new Rect(632, 315, 475, 28), "OBSERVATIONS / REQUESTS / ACTUAL OUTCOMES", small);
            var viewport = new Rect(630, 350, 480, 356);
            // Fixed row height makes every long JSON observation readable by wrapping.
            logScroll = GUI.BeginScrollView(viewport, logScroll, new Rect(0, 0, 455, Mathf.Max(350, Log.Count * 84)));
            for (int i = 0; i < Log.Count; ++i)
                GUI.Label(new Rect(4, i * 84, 444, 80), Log[i], small);
            GUI.EndScrollView();
            GUI.matrix = original;
        }

        private void DrawBoard()
        {
            const float cell = 52;
            for (int y = 0; y < MazeBoard.Size; ++y)
            {
                GUI.Label(new Rect(15, 119 + (9 - y) * cell, 22, 30), y.ToString(), small);
                for (int x = 0; x < MazeBoard.Size; ++x)
                {
                    var p = new Vector2Int(x, y);
                    var rect = new Rect(44 + x * cell, 112 + (9 - y) * cell, cell - 3, cell - 3);
                    bool wall = MazeBoard.Tile(p) == "wall";
                    Color color = wall ? new Color(0.19f, 0.24f, 0.31f) : new Color(0.11f, 0.15f, 0.21f);
                    if (p == MazeBoard.Start) color = new Color(0.1f, 0.47f, 0.6f);
                    if (p == MazeBoard.Goal) color = new Color(0.35f, 0.62f, 0.26f);
                    Fill(rect, color);
                    if (p == MazeBoard.Start || p == MazeBoard.Goal)
                        GUI.Label(rect, p == MazeBoard.Start ? "S" : "G", tileText);
                    if (p == Board.Position)
                    {
                        var agent = new Rect(rect.x + 12, rect.y + 12, rect.width - 24, rect.height - 24);
                        Fill(agent, new Color(1f, 0.65f, 0.24f));
                    }
                }
            }
            for (int x = 0; x < MazeBoard.Size; ++x)
                GUI.Label(new Rect(61 + x * cell, 636, 30, 25), x.ToString(), small);
        }

        private static void Fill(Rect rect, Color color)
        {
            var saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = saved;
        }

        private void InitStyles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold };
            heading = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
            text = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            tileText = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            foreach (var style in new[] { title, heading, text, small, tileText }) style.normal.textColor = new Color(0.88f, 0.93f, 0.98f);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using MazeSolver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

public sealed class MazeLoopTests
{
    private GameObject root;
    private MazeGame game;

    private sealed class FakeTransport : IMazeTransport
    {
        public readonly List<JObject> Requests = new List<JObject>();
        public readonly List<string> Paths = new List<string>();
        public bool Delayed;
        public bool Release;
        public bool Offline;
        public bool Broken;
        public bool OutcomeFails;
        public int X = 1, Y = 2;
        public IEnumerator Send(string path, string json, int timeout, Action<HttpReply> complete)
        {
            var body = json == null ? null : JObject.Parse(json);
            Paths.Add(path);
            Requests.Add(body);
            while (Delayed && !Release) yield return null;
            yield return null;
            if (Offline || (OutcomeFails && path == "/outcome"))
            {
                complete(new HttpReply { Success = false, Body = "", Error = "test offline/timeout" });
                yield break;
            }
            JObject response;
            if (path == "/health") response = new JObject { ["status"] = "ok" };
            else if (path == "/decide") response = new JObject {
                ["session_id"] = body["session_id"], ["turn"] = body["turn"],
                ["move"] = new JObject { ["action"] = "move", ["x"] = X, ["y"] = Y }
            };
            else response = new JObject { ["session_id"] = body["session_id"], ["turn"] = body["turn"], ["recorded"] = true };
            complete(new HttpReply { Success = true, Body = Broken ? "broken JSON" : response.ToString(Formatting.None) });
        }
        public void Cancel() { /* Intentionally allow late replies to test generation checks. */ }
    }

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Maze under test");
        game = root.AddComponent<MazeGame>();
    }

    [TearDown]
    public void TearDown() { UnityEngine.Object.DestroyImmediate(root); }

    private IEnumerator Idle(float seconds = 5)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (game.Busy && Time.realtimeSinceStartup < end) yield return null;
        Assert.IsFalse(game.Busy, "Operation timed out in test");
    }

    [UnityTest]
    public IEnumerator StepReportsActualMoveAndPreventsOverlappingRequests()
    {
        var transport = new FakeTransport();
        game.Transport = transport;
        game.StepOnce();
        game.StepOnce();
        yield return Idle();
        Assert.AreEqual(new Vector2Int(1, 2), game.Board.Position);
        Assert.AreEqual(1, game.Turn);
        CollectionAssert.AreEqual(new[] { "/decide", "/outcome" }, transport.Paths);
        Assert.AreEqual("moved", (string)transport.Requests[1]["outcome"]["status"]);
        Assert.AreEqual(JTokenType.Null, transport.Requests[0]["previous_outcome"].Type);
        Assert.IsTrue(game.Log.Exists(line => line.Contains("OUTCOME")));
        transport.X = 2; transport.Y = 2;
        game.StepOnce();
        yield return Idle();
        Assert.AreEqual("moved", (string)transport.Requests[2]["previous_outcome"]["status"]);
        Assert.AreEqual(2, game.Turn);
    }

    [UnityTest]
    public IEnumerator WallRequestReportsBlockedWithoutMoving()
    {
        var transport = new FakeTransport { X = 2, Y = 1 };
        game.Transport = transport;
        game.StepOnce();
        yield return Idle();
        Assert.AreEqual(MazeBoard.Start, game.Board.Position);
        Assert.AreEqual("blocked", (string)transport.Requests[1]["outcome"]["status"]);
        Assert.IsFalse((bool)transport.Requests[1]["outcome"]["success"]);
    }

    [UnityTest]
    public IEnumerator FailureStopsWithoutInventingMove()
    {
        game.Transport = new FakeTransport { Offline = true };
        game.ToggleRun();
        yield return Idle();
        Assert.IsTrue(game.Failed);
        Assert.IsFalse(game.Running);
        Assert.AreEqual(MazeBoard.Start, game.Board.Position);
        Assert.AreEqual(0, game.Turn);
        Assert.IsTrue(game.Status.Contains("Reset"));
        game.ResetRun();
        Assert.IsFalse(game.Failed);
    }

    [UnityTest]
    public IEnumerator BrokenResponseStopsWithoutMoving()
    {
        game.Transport = new FakeTransport { Broken = true };
        game.StepOnce();
        yield return Idle();
        Assert.IsTrue(game.Failed);
        Assert.AreEqual(MazeBoard.Start, game.Board.Position);
    }

    [UnityTest]
    public IEnumerator FailedOutcomeReportDoesNotUndoAnActualMove()
    {
        game.Transport = new FakeTransport { OutcomeFails = true };
        game.StepOnce();
        yield return Idle();
        Assert.IsTrue(game.Failed);
        Assert.AreEqual(new Vector2Int(1, 2), game.Board.Position);
        Assert.AreEqual(0, game.Turn);
    }

    [UnityTest]
    public IEnumerator ResetIgnoresLateResponseFromOldSession()
    {
        var old = new FakeTransport { Delayed = true };
        game.Transport = old;
        string session = game.SessionId;
        game.StepOnce();
        yield return null;
        Assert.IsTrue(game.Busy);
        game.ResetRun();
        Assert.AreNotEqual(session, game.SessionId);
        game.Transport = new FakeTransport { X = 2, Y = 1 };
        game.StepOnce();
        old.Release = true;
        yield return Idle();
        yield return null;
        Assert.AreEqual(MazeBoard.Start, game.Board.Position);
        Assert.AreEqual(1, game.Turn);
        CollectionAssert.AreEqual(new[] { "/decide" }, old.Paths);
    }

    [UnityTest]
    public IEnumerator PauseCompletesPendingTurnAndDoesNotQueueAnother()
    {
        var transport = new FakeTransport { Delayed = true };
        game.Transport = transport;
        game.ToggleRun();
        yield return null;
        game.ToggleRun();
        transport.Release = true;
        yield return Idle();
        Assert.AreEqual(1, game.Turn);
        Assert.IsFalse(game.Running);
        Assert.AreEqual(2, transport.Paths.Count);
    }

    [UnityTest]
    public IEnumerator IllegalServerDestinationIsRejectedReportedAndStopped()
    {
        var transport = new FakeTransport { X = 8, Y = 8 };
        game.Transport = transport;
        game.StepOnce();
        yield return Idle();
        Assert.AreEqual(MazeBoard.Start, game.Board.Position);
        Assert.AreEqual("invalid_move", (string)transport.Requests[1]["outcome"]["status"]);
        Assert.IsTrue(game.Failed);
    }

    [UnityTest]
    public IEnumerator GoalIsReportedBeforeStoppingAndCannotStepAgain()
    {
        // Fixture positioning only. No test route exists in the game assembly.
        foreach (var p in new[] {
            new Vector2Int(1,2), new Vector2Int(2,2), new Vector2Int(3,2),
            new Vector2Int(3,3), new Vector2Int(3,4), new Vector2Int(2,4),
            new Vector2Int(1,4), new Vector2Int(1,5), new Vector2Int(1,6),
            new Vector2Int(1,7), new Vector2Int(1,8), new Vector2Int(2,8),
            new Vector2Int(3,8), new Vector2Int(3,7), new Vector2Int(3,6),
            new Vector2Int(4,6), new Vector2Int(5,6), new Vector2Int(5,7),
            new Vector2Int(5,8), new Vector2Int(6,8), new Vector2Int(7,8)
        }) Assert.IsTrue((bool)game.Board.Apply(p)["success"]);
        var transport = new FakeTransport { X = 8, Y = 8 };
        game.Transport = transport;
        game.ToggleRun();
        yield return Idle();
        Assert.IsTrue(game.Board.AtGoal);
        Assert.IsFalse(game.Running);
        Assert.AreEqual("goal_reached", (string)transport.Requests[1]["outcome"]["status"]);
        game.StepOnce();
        Assert.AreEqual(2, transport.Paths.Count);
        Assert.IsTrue(game.Status.Contains("Goal reached"));
    }

    [UnityTest]
    public IEnumerator RealBackendAndModelMakeProgress()
    {
        if (Environment.GetEnvironmentVariable("MAZE_LIVE_TEST") != "1")
            Assert.Ignore("Opt in with MAZE_LIVE_TEST=1 and start the real backend/Ollama first.");
        UnityEngine.Object.DestroyImmediate(root);
        yield return SceneManager.LoadSceneAsync("SampleScene");
        game = UnityEngine.Object.FindFirstObjectByType<MazeGame>();
        Assert.IsNotNull(game, "SampleScene must have the game controller wired in");
        root = game.gameObject;
        string testBackend = Environment.GetEnvironmentVariable("MAZE_BACKEND_URL");
        if (!string.IsNullOrEmpty(testBackend)) game.Transport = new MazeTransport(testBackend);
        game.CheckHealth();
        yield return Idle(15);
        Assert.IsFalse(game.Failed, game.Status);
        var visited = new HashSet<Vector2Int> { game.Board.Position };
        for (int step = 0; step < 3; ++step)
        {
            game.StepOnce();
            yield return Idle(90);
            Assert.IsFalse(game.Failed, game.Status);
            Assert.AreEqual(step + 1, game.Turn);
            visited.Add(game.Board.Position);
            Debug.Log("LIVE UNITY PROGRESS: " + game.Status);
        }
        Assert.GreaterOrEqual(visited.Count, 3, "Real model must demonstrate movement, not just blocked requests.");
        Assert.IsTrue(game.Log.Exists(line => line.Contains("REQUEST")));
        Assert.IsTrue(game.Log.Exists(line => line.Contains("OUTCOME")));
        Debug.Log("LIVE UNITY/BACKEND/OLLAMA: " + game.Status);
        string screenshot = Environment.GetEnvironmentVariable("MAZE_SCREENSHOT");
        if (!string.IsNullOrEmpty(screenshot))
        {
            DateTime captureStarted = DateTime.UtcNow;
            ScreenCapture.CaptureScreenshot(screenshot);
            // CaptureScreenshot writes after the current frame is rendered.
            float until = Time.realtimeSinceStartup + 10;
            while ((!System.IO.File.Exists(screenshot) || System.IO.File.GetLastWriteTimeUtc(screenshot) < captureStarted)
                    && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.IsTrue(System.IO.File.Exists(screenshot), "Expected an actual rendered screenshot");
            Assert.GreaterOrEqual(System.IO.File.GetLastWriteTimeUtc(screenshot), captureStarted,
                "Screenshot must be from this run, not a previous image");
        }
    }
}

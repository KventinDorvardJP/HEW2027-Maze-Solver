using System;
using System.Linq;
using MazeSolver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class MazeRulesTests
{
    [Test]
    public void FixedMapAndObservationHaveCorrectOrientation()
    {
        var board = new MazeBoard();
        Assert.AreEqual(new Vector2Int(1, 1), board.Position);
        Assert.AreEqual("wall", MazeBoard.Tile(new Vector2Int(2, 1)));
        Assert.AreEqual("floor", MazeBoard.Tile(new Vector2Int(1, 2)));
        Assert.AreEqual("goal", MazeBoard.Tile(MazeBoard.Goal));
        var request = MazeProtocol.Observation("test", 0, board, null);
        Assert.AreEqual(JTokenType.Null, request["previous_outcome"].Type);
        Assert.AreEqual(4, ((JArray)request["nearby"]).Count);
        Assert.AreEqual(7, request.Properties().Count());
        Assert.IsNull(request["map"]);
    }

    [Test]
    public void LegalMoveWallRejectionAndReset()
    {
        var board = new MazeBoard();
        Assert.AreEqual("blocked", (string)board.Apply(new Vector2Int(2, 1))["status"]);
        Assert.AreEqual(MazeBoard.Start, board.Position);
        var result = board.Apply(new Vector2Int(1, 2));
        Assert.IsTrue((bool)result["success"]);
        Assert.AreEqual(JTokenType.Null, result["reason"].Type);
        Assert.AreEqual(new Vector2Int(1, 2), board.Position);
        board.Reset();
        Assert.AreEqual(MazeBoard.Start, board.Position);
    }

    [TestCase(2, 2, "not_adjacent")]
    [TestCase(1, 1, "not_adjacent")]
    [TestCase(1, 4, "not_adjacent")]
    [TestCase(-1, 1, "out_of_bounds")]
    [TestCase(10, 1, "out_of_bounds")]
    [TestCase(int.MinValue, 1, "out_of_bounds")]
    public void IllegalDestinationDoesNotMove(int x, int y, string reason)
    {
        var board = new MazeBoard();
        var result = board.Apply(new Vector2Int(x, y));
        Assert.AreEqual("invalid_move", (string)result["status"]);
        Assert.AreEqual(reason, (string)result["reason"]);
        Assert.AreEqual(MazeBoard.Start, board.Position);
    }

    [Test]
    public void MapHasReachableGoalAndGoalOutcomeIsAccurate()
    {
        // Explicit test inputs verify this fixed map; production never uses a
        // route or automated move selector. Only the LLM supplies game moves.
        var board = new MazeBoard();
        var inputs = new[] {
            new Vector2Int(1,2), new Vector2Int(2,2), new Vector2Int(3,2),
            new Vector2Int(3,3), new Vector2Int(3,4), new Vector2Int(2,4),
            new Vector2Int(1,4), new Vector2Int(1,5), new Vector2Int(1,6),
            new Vector2Int(1,7), new Vector2Int(1,8), new Vector2Int(2,8),
            new Vector2Int(3,8), new Vector2Int(3,7), new Vector2Int(3,6),
            new Vector2Int(4,6), new Vector2Int(5,6), new Vector2Int(5,7),
            new Vector2Int(5,8), new Vector2Int(6,8), new Vector2Int(7,8), new Vector2Int(8,8)
        };
        JObject outcome = null;
        foreach (var p in inputs)
        {
            outcome = board.Apply(p);
            Assert.IsTrue((bool)outcome["success"], "Move to " + p);
        }
        Assert.IsTrue(board.AtGoal);
        Assert.AreEqual("goal_reached", (string)outcome["status"]);
    }

    [Test]
    public void ResponseIdentityAndAcknowledgementAreChecked()
    {
        Assert.AreEqual(new Vector2Int(1, 2), MazeProtocol.Decision(
            "{\"session_id\":\"s\",\"turn\":0,\"move\":{\"action\":\"move\",\"x\":1,\"y\":2}}", "s", 0));
        Assert.Throws<FormatException>(() => MazeProtocol.Decision(
            "{\"session_id\":\"old\",\"turn\":0,\"move\":{\"action\":\"move\",\"x\":1,\"y\":2}}", "s", 0));
        MazeProtocol.Acknowledgement("{\"session_id\":\"s\",\"turn\":0,\"recorded\":true}", "s", 0);
        Assert.Throws<FormatException>(() => MazeProtocol.Acknowledgement(
            "{\"session_id\":\"s\",\"turn\":0,\"recorded\":false}", "s", 0));
    }

    [TestCase("{}")]
    [TestCase("{\"action\":\"move\",\"x\":\"1\",\"y\":2}")]
    [TestCase("{\"action\":\"move\",\"x\":true,\"y\":2}")]
    [TestCase("{\"action\":\"move\",\"x\":1.5,\"y\":2}")]
    [TestCase("{\"action\":\"jump\",\"x\":1,\"y\":2}")]
    [TestCase("{\"action\":\"move\",\"x\":1,\"y\":2,\"extra\":0}")]
    public void MalformedMoveIsAnError(string move)
    {
        Assert.Throws<FormatException>(() => MazeProtocol.Decision(
            "{\"session_id\":\"s\",\"turn\":0,\"move\":" + move + "}", "s", 0));
    }

    [Test]
    public void DuplicateAndBrokenJsonAreRejected()
    {
        Assert.Throws<JsonReaderException>(() => MazeProtocol.ParseObject("{\"x\":1,\"x\":2}"));
        Assert.Throws<JsonReaderException>(() => MazeProtocol.ParseObject("not json"));
    }
}

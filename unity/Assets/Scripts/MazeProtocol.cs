using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MazeSolver
{
    public static class MazeProtocol
    {
        public static JObject Observation(string session, int turn, MazeBoard board, JObject previous)
        {
            return new JObject {
                ["session_id"] = session, ["turn"] = turn,
                ["start"] = MazeBoard.Point(MazeBoard.Start), ["goal"] = MazeBoard.Point(MazeBoard.Goal),
                ["position"] = MazeBoard.Point(board.Position), ["nearby"] = board.Observe(),
                ["previous_outcome"] = previous == null ? JValue.CreateNull() : previous.DeepClone()
            };
        }

        public static JObject ParseObject(string text) => JObject.Parse(text,
            new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });

        private static void Keys(JObject obj, params string[] expected)
        {
            if (obj == null || !obj.Properties().Select(p => p.Name).OrderBy(n => n)
                    .SequenceEqual(expected.OrderBy(n => n)))
                throw new FormatException("Response fields do not match the protocol.");
        }

        private static void Identity(JObject body, string session, int turn)
        {
            if (body["session_id"]?.Type != JTokenType.String || (string)body["session_id"] != session
                || body["turn"]?.Type != JTokenType.Integer || body["turn"].Value<long>() != turn)
                throw new FormatException("Response session or turn does not match this request.");
        }

        public static Vector2Int Decision(string text, string session, int turn)
        {
            var body = ParseObject(text);
            Keys(body, "session_id", "turn", "move");
            Identity(body, session, turn);
            var move = body["move"] as JObject;
            Keys(move, "action", "x", "y");
            if (move["action"]?.Type != JTokenType.String || (string)move["action"] != "move"
                || move["x"]?.Type != JTokenType.Integer || move["y"]?.Type != JTokenType.Integer)
                throw new FormatException("Response must contain a move with integer coordinates.");
            // Bounds/adjacency are checked by MazeBoard, which reports an actual
            // invalid_move outcome if a server ever violates those rules.
            return new Vector2Int(move["x"].Value<int>(), move["y"].Value<int>());
        }

        public static void Acknowledgement(string text, string session, int turn)
        {
            var body = ParseObject(text);
            Keys(body, "session_id", "turn", "recorded");
            Identity(body, session, turn);
            if (body["recorded"]?.Type != JTokenType.Boolean || !(bool)body["recorded"])
                throw new FormatException("Backend did not acknowledge the outcome.");
        }

        public static string Error(string body, string fallback)
        {
            try
            {
                var error = ParseObject(body)["error"] as JObject;
                if (error?["code"]?.Type == JTokenType.String && error["message"]?.Type == JTokenType.String)
                    return (string)error["code"] + ": " + (string)error["message"];
            }
            catch (Exception) { /* A broken error body must still stop the run. */ }
            return fallback;
        }
    }
}

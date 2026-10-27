using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MazeSolver
{
    // Map data and movement only. This class never chooses a move.
    public sealed class MazeBoard
    {
        public const int Size = 10;
        public static readonly Vector2Int Start = new Vector2Int(1, 1);
        public static readonly Vector2Int Goal = new Vector2Int(8, 8);

        // Human-readable rows run from top (y=9) to bottom (y=0).
        private static readonly string[] Rows = {
            "##########",
            "#...#...G#",
            "#.#.#.##.#",
            "#.#...#..#",
            "#.###.#.##",
            "#...#....#",
            "###.####.#",
            "#...#....#",
            "#S#...##.#",
            "##########"
        };

        public Vector2Int Position { get; private set; } = Start;
        public bool AtGoal => Position == Goal;

        public static bool InBounds(Vector2Int p) => p.x >= 0 && p.x < Size && p.y >= 0 && p.y < Size;
        public static bool IsAdjacent(Vector2Int a, Vector2Int b) => Math.Abs((long)a.x - b.x) + Math.Abs((long)a.y - b.y) == 1;

        public static string Tile(Vector2Int p)
        {
            if (!InBounds(p)) return "out_of_bounds";
            if (Rows[Size - 1 - p.y][p.x] == '#') return "wall";
            return p == Goal ? "goal" : "floor";
        }

        public void Reset() => Position = Start;

        public JArray Observe()
        {
            var result = new JArray();
            foreach (var offset in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
            {
                var p = Position + offset;
                result.Add(new JObject { ["x"] = p.x, ["y"] = p.y, ["tile"] = Tile(p) });
            }
            return result;
        }

        public JObject Apply(Vector2Int destination)
        {
            string reason = null;
            string status;
            bool success = false;
            if (!InBounds(destination)) { status = "invalid_move"; reason = "out_of_bounds"; }
            else if (!IsAdjacent(Position, destination)) { status = "invalid_move"; reason = "not_adjacent"; }
            else if (Tile(destination) == "wall") { status = "blocked"; reason = "wall"; }
            else
            {
                Position = destination;
                success = true;
                status = AtGoal ? "goal_reached" : "moved";
            }
            return new JObject {
                ["success"] = success, ["status"] = status,
                ["position"] = Point(Position),
                ["reason"] = reason == null ? JValue.CreateNull() : new JValue(reason)
            };
        }

        public static JObject Point(Vector2Int p) => new JObject { ["x"] = p.x, ["y"] = p.y };
    }
}

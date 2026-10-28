using System;
using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>
    /// One entry of conditions.json (or an inline condition inside another one / inside an ability or move pattern).
    /// The ONLY condition class: everything is data. Four shapes:
    /// <list type="bullet">
    /// <item>comparison — <c>{ "stat": "captures", "of": "knight", "op": ">=", "value": 1 }</c></item>
    /// <item><c>{ "all": [ ... ] }</c> — every sub-condition is true</item>
    /// <item><c>{ "any": [ ... ] }</c> — at least one is true</item>
    /// <item><c>{ "not": ... }</c> — the sub-condition is false</item>
    /// </list>
    /// Sub-conditions are either inline objects or ids of other conditions.
    /// A named condition with <c>"permanent": true</c> stays true for a piece once it has been true after one of its side's moves.
    /// </summary>
    public sealed class Condition
    {
        public enum Shape { Compare, All, Any, Not }

        /// <summary>The id in conditions.json; null for an inline condition.</summary>
        public string Id { get; internal set; }
        public string DisplayName { get; internal set; }
        public string Description { get; internal set; }
        public bool Permanent { get; internal set; }

        public Shape Kind { get; internal set; }

        // Compare
        public string Stat { get; internal set; }
        /// <summary>Piece names the stat counts (null = all). Only for stats that count pieces, e.g. "captures".</summary>
        public IReadOnlyCollection<string> Of { get; internal set; }
        /// <summary>Piece names the stat ignores (null = none).</summary>
        public IReadOnlyCollection<string> Except { get; internal set; }
        public string Op { get; internal set; }
        public int Value { get; internal set; }

        // All / Any / Not (Not has exactly one child)
        public IReadOnlyList<Condition> Children { get; internal set; } = Array.Empty<Condition>();

        internal ConditionStats.StatDef StatDef;
        /// <summary>Where it was defined, for error messages.</summary>
        internal string Where;

        /// <summary>Is the condition true for this piece right now? (A permanent one that was ever reached stays true.)</summary>
        public bool Evaluate(Piece piece)
        {
            if (Permanent && piece.HasLatched(this)) return true;
            return EvaluateNow(piece);
        }

        /// <summary>The value ignoring a permanent latch — used to decide when to latch.</summary>
        internal bool EvaluateNow(Piece piece)
        {
            switch (Kind)
            {
                case Shape.Compare:
                    return Compare(StatDef.Read(piece, this), Op, Value);
                case Shape.All:
                    foreach (var c in Children) if (!c.Evaluate(piece)) return false;
                    return true;
                case Shape.Any:
                    foreach (var c in Children) if (c.Evaluate(piece)) return true;
                    return false;
                case Shape.Not:
                    return !Children[0].Evaluate(piece);
            }
            return false;
        }

        /// <summary>Does this condition (or anything it refers to) read <paramref name="stat"/>?</summary>
        public bool UsesStat(string stat)
        {
            if (Kind == Shape.Compare) return Stat == stat;
            foreach (var c in Children) if (c.UsesStat(stat)) return true;
            return false;
        }

        /// <summary>This condition and every condition it refers to, each once.</summary>
        internal void CollectAll(HashSet<Condition> into)
        {
            if (!into.Add(this)) return;
            foreach (var c in Children) c.CollectAll(into);
        }

        public static readonly string[] Operators = { "==", "!=", ">", ">=", "<", "<=" };

        static bool Compare(int a, string op, int b)
        {
            switch (op)
            {
                case "==": return a == b;
                case "!=": return a != b;
                case ">": return a > b;
                case ">=": return a >= b;
                case "<": return a < b;
                case "<=": return a <= b;
            }
            return false;
        }

        public override string ToString() => Id ?? $"({Kind})";
    }

    /// <summary>
    /// The numbers a condition can test ("stat" in conditions.json). This is the only place that needs C# to grow the
    /// condition language: register a new stat here and every JSON condition can use it.
    /// </summary>
    public static class ConditionStats
    {
        public const string Moves = "moves";
        public const string Captures = "captures";
        public const string MoveStreak = "moveStreak";

        internal sealed class StatDef
        {
            public Func<Piece, Condition, int> Read;
            /// <summary>Accepts "of" / "except" piece-name filters.</summary>
            public bool CountsPieces;
            public string Description;
        }

        static readonly Dictionary<string, StatDef> Stats = new Dictionary<string, StatDef>
        {
            [Moves] = new StatDef
            {
                Read = (p, c) => p.MoveCount,
                Description = "times this piece has moved",
            },
            [Captures] = new StatDef
            {
                Read = (p, c) => p.CapturesMatching(c.Of, c.Except),
                CountsPieces = true,
                Description = "pieces this piece has captured (by name; a broken wall counts as a captured \"wall\")",
            },
            [MoveStreak] = new StatDef
            {
                Read = (p, c) => p.MoveStreak,
                Description = "how many of its side's turns in a row this piece has moved (0 once its side moves another piece)",
            },
        };

        public static IEnumerable<string> KnownStats => Stats.Keys;

        public static bool IsKnown(string stat) => Stats.ContainsKey(stat);

        internal static bool TryGet(string stat, out StatDef def) => Stats.TryGetValue(stat, out def);

        /// <summary>
        /// Adds (or replaces) a stat. <paramref name="read"/> gets the piece and the condition (for its Of / Except filters).
        /// Call before loading the data. The value must only depend on the piece's own state, so undo and the
        /// position hash stay correct.
        /// </summary>
        public static void Register(string stat, Func<Piece, Condition, int> read, bool countsPieces = false, string description = "")
        {
            if (string.IsNullOrWhiteSpace(stat)) throw new ArgumentException("stat name required", nameof(stat));
            Stats[stat] = new StatDef { Read = read ?? throw new ArgumentNullException(nameof(read)), CountsPieces = countsPieces, Description = description };
        }
    }
}

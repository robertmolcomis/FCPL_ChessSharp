using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>
    /// A piece instance. Carries its own state (move count, what it captured, move streak, reached conditions)
    /// so per-piece mechanics work without touching the board.
    /// </summary>
    public sealed class Piece
    {
        static int _nextId;

        /// <summary>Stable id so the view can track the same piece across moves (kept through promotion / transformation).</summary>
        public int Id { get; }
        public PieceDefinition Definition { get; internal set; }
        public PieceColor Color { get; }
        public int MoveCount { get; internal set; }

        /// <summary>Pieces captured, walls not included (a broken wall isn't a kill).</summary>
        public int CaptureCount { get; internal set; }

        /// <summary>How many of its side's turns in a row this piece has moved (0 once its side moves another piece).</summary>
        public int MoveStreak { get; internal set; }

        // Captures by the captured piece's pieces.json name (walls included, as "wall" or whatever the data calls them).
        readonly Dictionary<string, int> _captures = new Dictionary<string, int>();
        // Permanent conditions this piece has reached.
        HashSet<Condition> _latched;

        public bool HasMoved => MoveCount > 0;
        public bool IsRoyal => Definition.IsRoyal;
        public bool IsWall => Definition.IsWall;

        /// <summary>Permanently berserk once it has met the condition (only for pieces with the BerserkPawn ability).</summary>
        public bool IsBerserk => Definition.Get<BerserkPawnAbility>()?.IsActive(this) ?? false;

        /// <summary>Movement primitives currently in effect (used by move generation AND attack detection).</summary>
        public IReadOnlyList<MovePrimitive> Moves => Definition.MovesFor(this);

        public Piece(PieceDefinition definition, PieceColor color)
        {
            Id = System.Threading.Interlocked.Increment(ref _nextId);
            Definition = definition;
            Color = color;
        }

        // ───────── captures by name ─────────

        /// <summary>Captures by piece name, e.g. { "knight": 1, "wall": 2 }.</summary>
        public IReadOnlyDictionary<string, int> Captures => _captures;

        /// <summary>How many pieces called <paramref name="pieceName"/> this piece has captured.</summary>
        public int CapturesOf(string pieceName) => _captures.TryGetValue(pieceName, out var n) ? n : 0;

        /// <summary>Captures whose name is in <paramref name="of"/> (null = any) and not in <paramref name="except"/>.</summary>
        public int CapturesMatching(IReadOnlyCollection<string> of, IReadOnlyCollection<string> except)
        {
            int total = 0;
            foreach (var kv in _captures)
            {
                if (of != null && !Contains(of, kv.Key)) continue;
                if (except != null && Contains(except, kv.Key)) continue;
                total += kv.Value;
            }
            return total;
        }

        static bool Contains(IReadOnlyCollection<string> names, string name)
        {
            foreach (var n in names) if (n == name) return true;
            return false;
        }

        internal void AddCapture(Piece captured)
        {
            var name = captured.Definition.Id;
            _captures[name] = CapturesOf(name) + 1;
            if (!captured.IsWall) CaptureCount++;
        }

        /// <summary>Save files: sets how many pieces of type <paramref name="of"/> this piece has captured.</summary>
        internal void RestoreCaptures(PieceDefinition of, int count)
        {
            int old = CapturesOf(of.Id);
            if (count > 0) _captures[of.Id] = count; else _captures.Remove(of.Id);
            if (!of.IsWall) CaptureCount += count - old;
        }

        internal void RemoveCapture(Piece captured)
        {
            var name = captured.Definition.Id;
            int n = CapturesOf(name) - 1;
            if (n > 0) _captures[name] = n; else _captures.Remove(name);
            if (!captured.IsWall) CaptureCount--;
        }

        // ───────── permanent conditions ─────────

        public bool HasLatched(Condition c) => _latched != null && _latched.Contains(c);

        internal void Latch(Condition c)
        {
            if (_latched == null) _latched = new HashSet<Condition>();
            _latched.Add(c);
        }

        internal void Unlatch(Condition c) => _latched?.Remove(c);

        // ───────── hashing / display ─────────

        /// <summary>Extra state that must be part of the position hash (each ability's StateHash hook). 0 = none.</summary>
        public ulong StateHash()
        {
            ulong h = 0;
            foreach (var a in Definition.Abilities) h ^= a.StateHash(this);
            return h;
        }

        /// <summary>Name shown for this piece: the definition name, or the symbol + "!" when berserk (a berserk pawn is "P!").</summary>
        public string DisplayName => IsBerserk ? Definition.Symbol + "!" : Definition.DisplayName;

        public override string ToString() => $"{Color} {DisplayName}";
    }
}

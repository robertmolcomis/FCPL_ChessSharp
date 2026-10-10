using System;
using System.Collections.Generic;

namespace Chess.Core
{
    public enum PieceColor { White, Black }

    public static class PieceColorExt
    {
        public static PieceColor Opponent(this PieceColor c) => c == PieceColor.White ? PieceColor.Black : PieceColor.White;
        /// <summary>+1 for White (forward = up), -1 for Black.</summary>
        public static int Forward(this PieceColor c) => c == PieceColor.White ? 1 : -1;
    }

    [Flags]
    public enum MoveFlags
    {
        None = 0,
        MoveOnly = 1 << 0,      // can't capture with this primitive (pawn push)
        CaptureOnly = 1 << 1,   // only captures (pawn diagonal)
        FirstMoveOnly = 1 << 2, // only if the piece hasn't moved yet (pawn double step)
    }

    /// <summary>
    /// One movement primitive. Direction is written from White's point of view (dy > 0 = forward);
    /// it is mirrored automatically for Black.
    /// MaxRange = 1 is a leap/step (knight, king), 0 = unlimited slide (rook, bishop, nightrider...).
    /// Steps of range 1 are never blocked by intermediate squares (the knight "jumps").
    /// </summary>
    public readonly struct MovePrimitive
    {
        public readonly int Dx;
        public readonly int Dy;
        public readonly int MaxRange;
        public readonly MoveFlags Flags;

        public MovePrimitive(int dx, int dy, int maxRange, MoveFlags flags = MoveFlags.None)
        {
            Dx = dx; Dy = dy; MaxRange = maxRange; Flags = flags;
        }
    }

    /// <summary>Special-rule abilities. Kept as data so fairy pieces / power-ups can opt in or out.</summary>
    [Flags]
    public enum PieceAbilities
    {
        None = 0,
        Castles = 1 << 0,              // royal piece that can castle (king)
        CastlePartner = 1 << 1,        // piece the king castles with (rook)
        CreatesEnPassant = 1 << 2,     // a 2-square straight move leaves an en-passant square (pawn)
        CapturesEnPassant = 1 << 3,    // capture-only steps may land on the en-passant square (pawn)
        ResetsFiftyMoveClock = 1 << 4, // moving it is irreversible (pawn)
        Berserks = 1 << 5,             // after BerserkCaptureThreshold captures, permanently gains BerserkMoves (pawn)
    }

    /// <summary>Data-only description of a piece type. No hardcoded "bishop moves diagonally" anywhere in the engine.</summary>
    public sealed class PieceDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public char Symbol { get; }           // FEN-style letter, uppercase
        public bool IsRoyal { get; }
        public bool PromotesOnLastRank { get; }
        public PieceAbilities Abilities { get; }
        public IReadOnlyList<MovePrimitive> Moves { get; }
        /// <summary>Full move set once berserk (Moves + extras). Same as Moves for pieces that can't go berserk.</summary>
        public IReadOnlyList<MovePrimitive> BerserkMoves { get; }
        /// <summary>Captures needed to go berserk (permanently).</summary>
        public int BerserkCaptureThreshold { get; }

        public PieceDefinition(string id, string displayName, char symbol, IEnumerable<MovePrimitive> moves,
                               bool isRoyal = false, bool promotesOnLastRank = false,
                               PieceAbilities abilities = PieceAbilities.None,
                               IEnumerable<MovePrimitive> berserkExtraMoves = null, int berserkCaptureThreshold = 2)
        {
            Abilities = abilities;
            Id = id; DisplayName = displayName; Symbol = char.ToUpperInvariant(symbol);
            Moves = new List<MovePrimitive>(moves);
            var berserk = new List<MovePrimitive>(Moves);
            if (berserkExtraMoves != null) berserk.AddRange(berserkExtraMoves);
            BerserkMoves = berserk;
            BerserkCaptureThreshold = berserkCaptureThreshold;
            IsRoyal = isRoyal; PromotesOnLastRank = promotesOnLastRank;
        }

        public bool Has(PieceAbilities a) => (Abilities & a) == a;
        public override string ToString() => DisplayName;
    }

    /// <summary>Builders for the common Betza-style building blocks.</summary>
    public static class Patterns
    {
        static readonly (int, int)[] Orthogonal = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        static readonly (int, int)[] Diagonal = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

        public static IEnumerable<MovePrimitive> Rook(int range = 0) { foreach (var (x, y) in Orthogonal) yield return new MovePrimitive(x, y, range); }
        public static IEnumerable<MovePrimitive> Bishop(int range = 0) { foreach (var (x, y) in Diagonal) yield return new MovePrimitive(x, y, range); }

        /// <summary>All 8 symmetric (a,b) leaps, e.g. (1,2) = knight, (1,3) = camel. range 0 = rider.</summary>
        public static IEnumerable<MovePrimitive> Leaper(int a, int b, int range = 1)
        {
            var seen = new HashSet<(int, int)>();
            foreach (var (x, y) in new[] { (a, b), (b, a) })
                foreach (var sx in new[] { 1, -1 })
                    foreach (var sy in new[] { 1, -1 })
                        if (seen.Add((x * sx, y * sy)))
                            yield return new MovePrimitive(x * sx, y * sy, range);
        }

        public static IEnumerable<MovePrimitive> Concat(params IEnumerable<MovePrimitive>[] parts)
        {
            foreach (var p in parts) foreach (var m in p) yield return m;
        }
    }
}

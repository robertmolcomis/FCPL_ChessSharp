namespace Chess.Core
{
    /// <summary>
    /// Zobrist-style position hash. Keys are derived deterministically from (piece type id, colour, square, state),
    /// so any new piece type or upgrade gets keys automatically — no fixed key table.
    /// </summary>
    public static class Zobrist
    {
        const ulong SideKey = 0x9E3779B97F4A7C15UL;

        public static ulong Compute(Game game)
        {
            var board = game.Board;
            ulong h = game.SideToMove == PieceColor.Black ? SideKey : 0UL;

            foreach (var (sq, p) in board.Pieces())
            {
                ulong k = Mix(StringHash(p.Definition.Id));
                k = Mix(k ^ (ulong)(p.Color == PieceColor.White ? 1 : 2));
                k = Mix(k ^ ((ulong)sq.File << 8) ^ (ulong)sq.Rank);

                // Castling rights are part of the position: an unmoved king/rook differs from a moved one.
                if (!p.HasMoved && (p.Definition.Has(AbilityIds.Castles) || p.Definition.Has(AbilityIds.CastlePartner)))
                    k = Mix(k ^ 0xC0FFEEUL);

                // Hook for M3: per-piece upgrades/modifiers must change the hash.
                ulong state = p.StateHash();
                if (state != 0) k = Mix(k ^ state);

                h ^= k;
            }

            // En passant only counts when a capture is actually possible (FIDE repetition rule).
            if (game.EnPassantSquare is Square ep && game.HasEnPassantCapture())
                h ^= Mix(0xE9UL ^ ((ulong)ep.File << 8) ^ (ulong)ep.Rank);

            return h;
        }

        /// <summary>SplitMix64 finaliser.</summary>
        static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Stable across runs/platforms (unlike string.GetHashCode).</summary>
        static ulong StringHash(string s)
        {
            ulong h = 14695981039346656037UL; // FNV-1a
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h;
        }
    }
}

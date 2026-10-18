using System;
using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>
    /// Generic, data-driven pseudo-legal move generation and attack detection from movement primitives.
    /// Legality (own royal not attacked) is filtered by Game via make → test → unmake.
    /// </summary>
    public static class MoveGenerator
    {
        public static void GeneratePseudoLegal(Game game, Square from, Piece piece, List<Move> output)
        {
            var board = game.Board;
            int fwd = piece.Color.Forward();
            int start = output.Count;

            foreach (var prim in piece.Moves)
            {
                if ((prim.Flags & MoveFlags.FirstMoveOnly) != 0 && piece.HasMoved) continue;

                bool moveOnly = (prim.Flags & MoveFlags.MoveOnly) != 0;
                bool captureOnly = (prim.Flags & MoveFlags.CaptureOnly) != 0;
                int dx = prim.Dx, dy = prim.Dy * fwd;
                int maxRange = prim.MaxRange <= 0 ? int.MaxValue : prim.MaxRange;

                if (maxRange == 1 && LeapBlockedByWall(board, from, dx, dy)) continue;

                var sq = from;
                for (int step = 1; step <= maxRange; step++)
                {
                    sq = sq.Offset(dx, dy);
                    if (!board.IsValid(sq)) break; // off-board and holes stop movement

                    var target = board[sq];
                    var wall = board.WallAt(sq);
                    if (wall != null)
                    {
                        // Nothing goes over a wall: every case below ends this ray.
                        if (board.OccupantAt(sq) == null && wall.Color == piece.Color)
                        {
                            if (!captureOnly) Add(game, from, sq, piece, MoveKind.Normal, default, output, start); // climb own wall
                        }
                        else if (target.Color != piece.Color && !moveOnly)
                        {
                            if (board.OccupantAt(sq) != null) AddBounce(from, sq, output, start); // destroys the wall only
                            else Add(game, from, sq, piece, MoveKind.Normal, default, output, start); // empty enemy wall
                        }
                        break;
                    }

                    if (target == null)
                    {
                        if (!captureOnly)
                        {
                            var kind = MoveKind.Normal;
                            if (step == 2 && dx == 0 && piece.Definition.Has(PieceAbilities.CreatesEnPassant))
                                kind = MoveKind.DoubleStep;
                            Add(game, from, sq, piece, kind, default, output, start);
                        }
                        else if (step == 1 && dx != 0 && game.EnPassantSquare == sq &&
                                 piece.Definition.Has(PieceAbilities.CapturesEnPassant))
                        {
                            var victim = board[game.EnPassantVictim];
                            if (victim != null && victim.Color != piece.Color)
                                Add(game, from, sq, piece, MoveKind.EnPassant, game.EnPassantVictim, output, start);
                        }
                        continue;
                    }

                    if (target.Color != piece.Color && !moveOnly)
                        Add(game, from, sq, piece, MoveKind.Normal, default, output, start);
                    break; // any piece blocks further sliding
                }
            }

            if (piece.Definition.Has(PieceAbilities.Castles) && !piece.HasMoved)
                GenerateCastling(game, from, piece, output);
        }

        /// <summary>
        /// Generic castling: the unmoved castling piece moves 2 squares toward an unmoved partner on the same rank
        /// with only empty squares between them; the partner lands on the square it passed.
        /// Can't castle out of, through, or into check.
        /// </summary>
        static void GenerateCastling(Game game, Square kingSq, Piece king, List<Move> output)
        {
            var board = game.Board;
            var enemy = king.Color.Opponent();
            bool inCheckChecked = false;

            foreach (int dir in new[] { 1, -1 })
            {
                // find the first piece in this direction
                var sq = kingSq.Offset(dir, 0);
                while (board.IsValid(sq) && board[sq] == null) sq = sq.Offset(dir, 0);
                if (!board.IsValid(sq)) continue;

                var partner = board[sq];
                if (partner.Color != king.Color || partner.HasMoved || !partner.Definition.Has(PieceAbilities.CastlePartner)) continue;

                var kingTo = kingSq.Offset(2 * dir, 0);
                var pass = kingSq.Offset(dir, 0);
                if (!board.IsValid(kingTo)) continue;
                // king's destination must be empty or the partner itself (partner moves away)
                var atDest = board[kingTo];
                if (atDest != null && atDest != partner) continue;

                if (!inCheckChecked)
                {
                    if (IsAttacked(board, kingSq, enemy)) return; // can't castle out of check
                    inCheckChecked = true;
                }
                if (IsAttacked(board, pass, enemy)) continue; // through check
                // "into check" is filtered by the normal legality test

                output.Add(new Move(kingSq, kingTo, '\0', MoveKind.Castle, sq));
            }
        }

        /// <summary>
        /// Leapers (range-1 jumps longer than one square, e.g. the knight) can't jump over a wall. The path is the
        /// long leg walked from the origin: b1→c3 passes b2 and b3.
        /// </summary>
        public static bool LeapBlockedByWall(Board board, Square from, int dx, int dy)
        {
            int ax = Math.Abs(dx), ay = Math.Abs(dy);
            if (Math.Max(ax, ay) < 2) return false; // one-square steps don't pass over anything
            bool vertical = ay >= ax;
            int len = vertical ? ay : ax, sx = Math.Sign(dx), sy = Math.Sign(dy);
            for (int i = 1; i <= len; i++)
            {
                var p = vertical ? from.Offset(0, i * sy) : from.Offset(i * sx, 0);
                if (p == from.Offset(dx, dy)) break; // destination itself isn't "over"
                if (board.WallAt(p) != null) return true;
            }
            return false;
        }

        /// <summary>Capture of a piece standing on a wall: no promotion choices, the mover doesn't actually move.</summary>
        static void AddBounce(Square from, Square to, List<Move> output, int start)
        {
            for (int i = start; i < output.Count; i++)
                if (output[i].To == to && output[i].Kind != MoveKind.Castle) return;
            output.Add(new Move(from, to));
        }

        static void Add(Game game, Square from, Square to, Piece piece, MoveKind kind, Square aux, List<Move> output, int start)
        {
            // de-duplicate (e.g. pawn single-step and double-step primitives both reach the next square)
            for (int i = start; i < output.Count; i++)
                if (output[i].To == to && output[i].Kind != MoveKind.Castle) return;

            var board = game.Board;
            if (piece.Definition.PromotesOnLastRank)
            {
                int lastRank = piece.Color == PieceColor.White ? board.Height - 1 : 0;
                if (to.Rank == lastRank)
                {
                    foreach (var p in PieceLibrary.PromotionChoices)
                        output.Add(new Move(from, to, p, kind, aux));
                    return;
                }
            }
            output.Add(new Move(from, to, '\0', kind, aux));
        }

        /// <summary>Is <paramref name="target"/> attacked by any piece of <paramref name="by"/>? Allocation-free.</summary>
        public static bool IsAttacked(Board board, Square target, PieceColor by)
        {
            for (int x = 0; x < board.Width; x++)
                for (int y = 0; y < board.Height; y++)
                {
                    var from = new Square(x, y);
                    var p = board[from];
                    if (p == null || p.Color != by) continue;
                    if (Attacks(board, from, p, target)) return true;
                }
            return false;
        }

        /// <summary>
        /// Adds to <paramref name="output"/> the square of every piece of <paramref name="by"/> that attacks
        /// <paramref name="target"/> (uses capture patterns only — same test as <see cref="IsAttacked"/>).
        /// Covers double and discovered check since the whole board is scanned.
        /// </summary>
        public static void AttackersOf(Board board, Square target, PieceColor by, List<Square> output)
        {
            for (int x = 0; x < board.Width; x++)
                for (int y = 0; y < board.Height; y++)
                {
                    var from = new Square(x, y);
                    var p = board[from];
                    if (p == null || p.Color != by) continue;
                    if (Attacks(board, from, p, target) && !output.Contains(from)) output.Add(from);
                }
        }

        /// <summary>Could the piece on <paramref name="from"/> capture on <paramref name="target"/> (ignoring what is there)?</summary>
        public static bool Attacks(Board board, Square from, Piece piece, Square target)
        {
            int fwd = piece.Color.Forward();
            int tx = target.File - from.File, ty = target.Rank - from.Rank;
            if (tx == 0 && ty == 0) return false;

            foreach (var prim in piece.Moves)
            {
                if ((prim.Flags & MoveFlags.MoveOnly) != 0) continue;
                if ((prim.Flags & MoveFlags.FirstMoveOnly) != 0 && piece.HasMoved) continue;
                int dx = prim.Dx, dy = prim.Dy * fwd;

                // target must equal k * (dx, dy) for some k >= 1
                int k;
                if (dx != 0) { if (tx % dx != 0) continue; k = tx / dx; if (dy * k != ty) continue; }
                else { if (tx != 0 || dy == 0 || ty % dy != 0) continue; k = ty / dy; }
                if (k < 1) continue;
                int maxRange = prim.MaxRange <= 0 ? int.MaxValue : prim.MaxRange;
                if (k > maxRange) continue;
                if (maxRange == 1 && LeapBlockedByWall(board, from, dx, dy)) continue;

                bool blocked = false;
                var sq = from;
                for (int i = 1; i < k; i++)
                {
                    sq = sq.Offset(dx, dy);
                    if (!board.IsValid(sq) || board[sq] != null) { blocked = true; break; }
                }
                if (!blocked) return true;
            }
            return false;
        }
    }
}

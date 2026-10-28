using System;
using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>
    /// The board-state file: a bit-packed snapshot of a position, complete enough to keep playing from it
    /// (castling, en passant, the 50-move counter and every power-up counter are all included).
    ///
    /// Layout after the header (T = Huffman code of a piece type, 2-5 bits, see SaveTables; γ = Elias-gamma number):
    /// <code>
    /// squares      64 bits      1 = something on the square (a1, b1 … h1, a2 … h8)
    /// per occupied square       colour (1) + type (T) of the top piece
    /// walls under pieces        γ(count+1), then each: which occupied square + wall colour (1) [+ wall type]
    /// side to move  1
    /// en passant    1 [+3 file]   (the rank follows from the side to move)
    /// 50-move clock γ(n+1)
    /// move number   γ(n)
    /// outcome       2 [+1]       00 ongoing / checkmate / draw (worked out again), 01 resigned + loser, 10 draw agreed
    /// draw offer    1 [+1]       pending offer + who offered
    /// per piece     1            has state? if 1: γ(moves+1), γ(capture kinds+1) × (type + γ(count)), 1 bit per permanent condition
    /// move streaks  per side: 1 [+ which piece + γ(streak)]
    /// </code>
    /// The start position is 37 bytes; a middlegame 45-50 bytes.
    /// </summary>
    public static class PositionCodec
    {
        // ───────────────────────── board file (header + position) ─────────────────────────

        /// <summary>The whole board-state file for the current position of <paramref name="game"/>.</summary>
        public static byte[] WriteBoardFile(Game game)
        {
            var w = new BitWriter();
            SaveFormat.WriteHeader(w, SaveFormat.FileKind.Board);
            Write(w, game, includeOutcome: true);
            return w.ToArray();
        }

        /// <summary>Rebuilds the game from a board-state file. The matching rules must be loaded. Throws <see cref="SaveFormatException"/>.</summary>
        public static Game ReadBoardFile(byte[] file)
        {
            var r = new BitReader(file);
            SaveFormat.ReadHeader(r, SaveFormat.FileKind.Board);
            var game = Read(r);
            SaveFormat.ExpectEnd(r);
            return game;
        }

        // ───────────────────────── position body ─────────────────────────

        /// <param name="includeOutcome">false writes "ongoing, no draw offer" (used for the start position of a move history).</param>
        internal static void Write(BitWriter w, Game game, bool includeOutcome)
        {
            var t = SaveFormat.SaveTables.Current;
            var board = game.Board;
            CheckBoardShape(board);

            // squares + top pieces
            var pieces = new List<Piece>();           // order used by the per-piece state and the streaks
            var shielded = new List<(int occupiedIndex, Piece wall)>();
            for (int i = 0; i < SaveFormat.SquareCount; i++) w.Write(board[ToSquare(i)] != null);
            for (int i = 0; i < SaveFormat.SquareCount; i++)
            {
                var sq = ToSquare(i);
                var top = board[sq];
                if (top == null) continue;
                if (board.IsShielded(sq)) shielded.Add((pieces.Count, board.WallAt(sq)));
                w.Write(top.Color == PieceColor.Black);
                t.WriteType(w, top.Definition);
                pieces.Add(top);
            }

            // walls with a piece standing on them
            int occupied = pieces.Count;
            w.WriteGamma((uint)shielded.Count + 1);
            foreach (var (index, wall) in shielded)
            {
                w.Write((ulong)index, BitWriter.BitsFor((ulong)occupied));
                w.Write(wall.Color == PieceColor.Black);
                w.Write((ulong)t.WallIndex[wall.Definition], t.WallBits);
                pieces.Add(wall);
            }

            // game state
            w.Write(game.SideToMove == PieceColor.Black);
            if (game.EnPassantSquare is Square ep)
            {
                if (ep.Rank != EnPassantRank(game.SideToMove)) throw new InvalidOperationException($"Unexpected en-passant square {ep}");
                w.Write(true);
                w.Write((ulong)ep.File, 3);
            }
            else w.Write(false);
            w.WriteGamma((uint)game.HalfmoveClock + 1);
            w.WriteGamma((uint)Math.Max(1, game.FullmoveNumber));

            if (includeOutcome) WriteOutcome(w, game);
            else { w.Write(0, 2); w.Write(false); }

            // per-piece state
            foreach (var p in pieces) WritePieceState(w, p, t);

            // move streaks (at most one piece per side has one: the piece that made that side's last move)
            foreach (var color in new[] { PieceColor.White, PieceColor.Black })
            {
                int index = pieces.FindIndex(p => p.Color == color && p.MoveStreak > 0);
                w.Write(index >= 0);
                if (index < 0) continue;
                w.Write((ulong)index, BitWriter.BitsFor((ulong)pieces.Count));
                w.WriteGamma((uint)pieces[index].MoveStreak);
            }
        }

        internal static Game Read(BitReader r)
        {
            var t = SaveFormat.SaveTables.Current;
            var board = new Board(SaveFormat.BoardSize, SaveFormat.BoardSize);

            var occupiedSquares = new List<Square>();
            for (int i = 0; i < SaveFormat.SquareCount; i++)
                if (r.ReadBit()) occupiedSquares.Add(ToSquare(i));

            var pieces = new List<Piece>();
            foreach (var sq in occupiedSquares)
            {
                var color = r.ReadBit() ? PieceColor.Black : PieceColor.White;
                var piece = new Piece(t.ReadType(r), color);
                board[sq] = piece;
                pieces.Add(piece);
            }

            int shieldedCount = (int)r.ReadGamma() - 1;
            if (shieldedCount > occupiedSquares.Count) throw new SaveFormatException("Bad wall count in save");
            for (int k = 0; k < shieldedCount; k++)
            {
                int index = r.ReadInt(BitWriter.BitsFor((ulong)occupiedSquares.Count));
                var color = r.ReadBit() ? PieceColor.Black : PieceColor.White;
                int wallType = r.ReadInt(t.WallBits);
                if (index >= occupiedSquares.Count || wallType >= t.WallTypes.Length) throw new SaveFormatException("Bad wall in save");
                var sq = occupiedSquares[index];
                if (pieces[index].IsWall || board.WallAt(sq) != null) throw new SaveFormatException("Bad wall in save");
                var wall = new Piece(t.WallTypes[wallType], color);
                board[sq] = wall; // goes to the wall layer, under the piece
                pieces.Add(wall);
            }

            var side = r.ReadBit() ? PieceColor.Black : PieceColor.White;
            Square? ep = null;
            if (r.ReadBit()) ep = new Square(r.ReadInt(3), EnPassantRank(side));
            int halfmove = (int)r.ReadGamma() - 1;
            int fullmove = (int)r.ReadGamma();
            var outcome = ReadOutcome(r);

            foreach (var p in pieces) ReadPieceState(r, p, t);

            var lastMoved = new Piece[2];
            foreach (var color in new[] { PieceColor.White, PieceColor.Black })
            {
                if (!r.ReadBit()) continue;
                int index = r.ReadInt(BitWriter.BitsFor((ulong)pieces.Count));
                if (index >= pieces.Count || pieces[index].Color != color) throw new SaveFormatException("Bad move streak in save");
                pieces[index].MoveStreak = (int)r.ReadGamma();
                lastMoved[(int)color] = pieces[index];
            }

            var game = new Game(board, side, ep, halfmove, fullmove, lastMoved[0], lastMoved[1]);
            ApplyOutcome(game, outcome);
            return game;
        }

        // ───────────────────────── pieces ─────────────────────────

        static void WritePieceState(BitWriter w, Piece p, SaveFormat.SaveTables t)
        {
            var captures = new List<(int type, int count)>();
            foreach (var kv in p.Captures)
                if (kv.Value > 0) captures.Add((t.TypeIndex[PieceLibrary.Get(kv.Key)], kv.Value));
            captures.Sort((a, b) => a.type.CompareTo(b.type));

            bool anyLatched = false;
            foreach (var c in t.Permanent) anyLatched |= p.HasLatched(c);

            bool hasState = p.MoveCount > 0 || captures.Count > 0 || anyLatched;
            w.Write(hasState);
            if (!hasState) return;

            w.WriteGamma((uint)p.MoveCount + 1);
            w.WriteGamma((uint)captures.Count + 1);
            foreach (var (type, count) in captures)
            {
                t.WriteType(w, t.Types[type]);
                w.WriteGamma((uint)count);
            }
            foreach (var c in t.Permanent) w.Write(p.HasLatched(c));
        }

        static void ReadPieceState(BitReader r, Piece p, SaveFormat.SaveTables t)
        {
            if (!r.ReadBit()) return;
            p.MoveCount = (int)r.ReadGamma() - 1;
            int kinds = (int)r.ReadGamma() - 1;
            if (kinds > t.Types.Length) throw new SaveFormatException("Bad capture list in save");
            for (int k = 0; k < kinds; k++)
            {
                p.RestoreCaptures(t.ReadType(r), (int)r.ReadGamma());
            }
            foreach (var c in t.Permanent)
                if (r.ReadBit()) p.Latch(c);
        }

        // ───────────────────────── outcome (resign / agreed draw aren't visible on the board) ─────────────────────────

        internal struct Outcome
        {
            public GameStatus Status;          // Ongoing, Resignation or DrawAgreed
            public PieceColor Loser;           // who resigned
            public PieceColor? DrawOfferedBy;
        }

        internal static void WriteOutcome(BitWriter w, Game game)
        {
            switch (game.Status)
            {
                case GameStatus.Resignation:
                    w.Write(1, 2);
                    w.Write(game.Winner == PieceColor.White); // loser is Black
                    break;
                case GameStatus.DrawAgreed:
                    w.Write(2, 2);
                    break;
                default:
                    w.Write(0, 2); // checkmate, stalemate and automatic draws follow from the position
                    break;
            }
            w.Write(game.DrawOfferedBy != null);
            if (game.DrawOfferedBy is PieceColor by) w.Write(by == PieceColor.Black);
        }

        internal static Outcome ReadOutcome(BitReader r)
        {
            var o = new Outcome();
            switch (r.ReadInt(2))
            {
                case 0: o.Status = GameStatus.Ongoing; break;
                case 1: o.Status = GameStatus.Resignation; o.Loser = r.ReadBit() ? PieceColor.Black : PieceColor.White; break;
                case 2: o.Status = GameStatus.DrawAgreed; break;
                default: throw new SaveFormatException("Bad game result in save");
            }
            if (r.ReadBit()) o.DrawOfferedBy = r.ReadBit() ? PieceColor.Black : PieceColor.White;
            return o;
        }

        internal static void ApplyOutcome(Game game, Outcome o)
        {
            if (o.Status != GameStatus.Ongoing && game.IsOver)
                throw new SaveFormatException("The saved result doesn't match the position");
            if (o.Status == GameStatus.Resignation) game.Resign(o.Loser);
            else if (o.Status == GameStatus.DrawAgreed) { game.OfferDraw(game.SideToMove); game.AcceptDraw(); }
            game.RestoreDrawOffer(o.DrawOfferedBy);
        }

        // ───────────────────────── helpers ─────────────────────────

        static Square ToSquare(int i) => new Square(i % SaveFormat.BoardSize, i / SaveFormat.BoardSize);

        /// <summary>The en-passant square lies on rank 6 when White is to move, rank 3 when Black is.</summary>
        static int EnPassantRank(PieceColor sideToMove) => sideToMove == PieceColor.White ? SaveFormat.BoardSize - 3 : 2;

        static void CheckBoardShape(Board board)
        {
            if (board.Width != SaveFormat.BoardSize || board.Height != SaveFormat.BoardSize)
                throw new NotSupportedException($"Save files support only {SaveFormat.BoardSize}×{SaveFormat.BoardSize} boards");
            for (int i = 0; i < SaveFormat.SquareCount; i++)
                if (!board.IsValid(ToSquare(i))) throw new NotSupportedException("Save files don't support boards with holes yet");
        }
    }
}

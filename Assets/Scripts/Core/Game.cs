using System;
using System.Collections.Generic;
using System.Text;

namespace Chess.Core
{
    /// <summary>
    /// The rules engine entry point. Pure C# — the Unity layer only reads state and calls TryMakeMove / Undo / Resign.
    /// Legal moves are generic: generate pseudo-legal → make → reject if own royal attacked → unmake.
    /// No special-case pin logic, so future power-ups interact with check correctly.
    /// </summary>
    public sealed class Game
    {
        /// <summary>
        /// Default start: walls on c3/f3 (White) and c6/f6 (Black), d/e pawns one square forward,
        /// one archbishop in front of each king (e2 / e7).
        /// </summary>
        public const string StartFen = "rnbqkbnr/ppp1appp/2wppw2/8/8/2WPPW2/PPP1APPP/RNBQKBNR w KQkq - 0 1";
        /// <summary>Classic chess start position (no walls).</summary>
        public const string ClassicFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        public Board Board { get; }
        public PieceColor SideToMove { get; private set; } = PieceColor.White;
        public Square? EnPassantSquare { get; private set; }
        public Square EnPassantVictim { get; private set; }
        public int HalfmoveClock { get; private set; }
        public int FullmoveNumber { get; private set; } = 1;

        public GameStatus Status { get; private set; } = GameStatus.Ongoing;
        public PieceColor? Winner { get; private set; }
        public bool IsOver => Status.IsOver();

        /// <summary>Side that has offered a draw (hotseat: the other side may accept on their turn).</summary>
        public PieceColor? DrawOfferedBy { get; private set; }

        public IReadOnlyList<MoveRecord> History => _history;
        public MoveRecord LastMove => _history.Count > 0 ? _history[_history.Count - 1] : null;
        public ulong Hash => _hashes[_hashes.Count - 1];

        /// <summary>Raised after a move is applied (not for internal search moves).</summary>
        public event Action<MoveRecord> MoveMade;
        /// <summary>Raised after Undo, Resign, draw offer/accept — anything the view should redraw for.</summary>
        public event Action StateChanged;

        readonly List<MoveRecord> _history = new List<MoveRecord>();
        readonly List<ulong> _hashes = new List<ulong>(); // hash of every position so far; [0] = initial

        public Game(string fen = StartFen)
        {
            Board = LoadFen(fen);
            _hashes.Add(Zobrist.Compute(this));
            UpdateStatus();
        }

        // ───────────────────────── Queries ─────────────────────────

        /// <summary>Legal moves for the piece on <paramref name="from"/> (empty if no piece, not its turn, or game over).</summary>
        public List<Move> GetMovesFrom(Square from)
        {
            var result = new List<Move>();
            if (IsOver) return result;
            var piece = Board[from];
            if (piece == null || piece.Color != SideToMove) return result;

            var pseudo = new List<Move>();
            MoveGenerator.GeneratePseudoLegal(this, from, piece, pseudo);
            foreach (var m in pseudo) if (IsLegal(m)) result.Add(m);
            return result;
        }

        public List<Move> GetLegalMoves()
        {
            var result = new List<Move>();
            if (IsOver) return result;
            foreach (var m in GeneratePseudoLegalAll()) if (IsLegal(m)) result.Add(m);
            return result;
        }

        public bool IsInCheck(PieceColor color)
        {
            foreach (var (sq, p) in Board.Pieces())
                if (p.IsRoyal && p.Color == color && !Board.IsShielded(sq) && MoveGenerator.IsAttacked(Board, sq, color.Opponent()))
                    return true;
            return false;
        }

        /// <summary>Squares of <paramref name="color"/>'s royal pieces that are currently attacked (for the check highlight).</summary>
        public List<Square> RoyalsInCheck(PieceColor color)
        {
            var list = new List<Square>();
            foreach (var (sq, p) in Board.Pieces())
                if (p.IsRoyal && p.Color == color && !Board.IsShielded(sq) && MoveGenerator.IsAttacked(Board, sq, color.Opponent()))
                    list.Add(sq);
            return list;
        }

        /// <summary>True if the side to move has a legal en-passant capture (used by the position hash).</summary>
        public bool HasEnPassantCapture()
        {
            if (EnPassantSquare == null) return false;
            var pseudo = new List<Move>();
            foreach (var (sq, p) in Board.Pieces())
            {
                if (p.Color != SideToMove || !p.Definition.Has(PieceAbilities.CapturesEnPassant)) continue;
                pseudo.Clear();
                MoveGenerator.GeneratePseudoLegal(this, sq, p, pseudo);
                foreach (var m in pseudo) if (m.Kind == MoveKind.EnPassant && IsLegal(m)) return true;
            }
            return false;
        }

        /// <summary>Does this move take something (a piece, an empty enemy wall, or the wall under a piece)?</summary>
        public bool IsCapture(Move m)
        {
            if (m.Kind == MoveKind.EnPassant) return true;
            if (m.Kind == MoveKind.Castle) return false;
            var mover = Board[m.From];
            var target = Board[m.To];
            return mover != null && target != null && target.Color != mover.Color;
        }

        // ───────────────────────── Commands ─────────────────────────

        /// <summary>
        /// Validates and applies a move. A promotion move must name its piece (Promotion = 'Q','R','B','N').
        /// Returns null if illegal.
        /// </summary>
        public MoveRecord TryMakeMove(Move move)
        {
            if (IsOver) return null;
            Move? match = null;
            foreach (var m in GetMovesFrom(move.From))
                if (m.Equals(move)) { match = m; break; }
            if (match == null) return null;

            var record = MakeInternal(match.Value);
            _hashes.Add(Zobrist.Compute(this));
            record.GaveCheck = IsInCheck(SideToMove);
            DrawOfferedBy = DrawOfferedBy == record.Mover.Color ? DrawOfferedBy : null; // making a move declines an offer
            UpdateStatus();
            MoveMade?.Invoke(record);
            return record;
        }

        /// <summary>Takes back the last move (also un-ends a finished game). Returns false if there is nothing to undo.</summary>
        public bool Undo()
        {
            // Resignation / agreed draw: "undo" just reopens the game without taking back a move.
            if (Status == GameStatus.Resignation || Status == GameStatus.DrawAgreed)
            {
                Status = GameStatus.Ongoing; Winner = null;
                UpdateStatus();
                StateChanged?.Invoke();
                return true;
            }
            if (_history.Count == 0) return false;
            UnmakeInternal(_history[_history.Count - 1]);
            _hashes.RemoveAt(_hashes.Count - 1);
            DrawOfferedBy = null;
            Status = GameStatus.Ongoing; Winner = null;
            UpdateStatus();
            StateChanged?.Invoke();
            return true;
        }

        public void Resign(PieceColor who)
        {
            if (IsOver) return;
            Status = GameStatus.Resignation;
            Winner = who.Opponent();
            StateChanged?.Invoke();
        }

        /// <summary>Hotseat draw offer: <paramref name="who"/> offers; the opponent calls AcceptDraw or just moves (declines).</summary>
        public void OfferDraw(PieceColor who)
        {
            if (IsOver) return;
            DrawOfferedBy = who;
            StateChanged?.Invoke();
        }

        public void AcceptDraw()
        {
            if (IsOver || DrawOfferedBy == null) return;
            Status = GameStatus.DrawAgreed;
            Winner = null;
            DrawOfferedBy = null;
            StateChanged?.Invoke();
        }

        public void DeclineDraw()
        {
            if (DrawOfferedBy == null) return;
            DrawOfferedBy = null;
            StateChanged?.Invoke();
        }

        // ───────────────────────── Perft (validation) ─────────────────────────

        /// <summary>Counts leaf nodes of the legal move tree to <paramref name="depth"/>. Ignores game-end rules (standard perft).</summary>
        public long Perft(int depth)
        {
            if (depth == 0) return 1;
            long nodes = 0;
            foreach (var m in GeneratePseudoLegalAll())
            {
                var rec = MakeInternal(m);
                if (!IsInCheck(rec.Mover.Color)) nodes += depth == 1 ? 1 : Perft(depth - 1);
                UnmakeInternal(rec);
            }
            return nodes;
        }

        /// <summary>Perft split by root move ("divide") — handy for finding move-generation bugs.</summary>
        public Dictionary<string, long> PerftDivide(int depth)
        {
            var result = new Dictionary<string, long>();
            foreach (var m in GeneratePseudoLegalAll())
            {
                var rec = MakeInternal(m);
                if (!IsInCheck(rec.Mover.Color)) result[m.ToString()] = Perft(depth - 1);
                UnmakeInternal(rec);
            }
            return result;
        }

        // ───────────────────────── Make / unmake ─────────────────────────

        List<Move> GeneratePseudoLegalAll()
        {
            var moves = new List<Move>(64);
            for (int x = 0; x < Board.Width; x++)
                for (int y = 0; y < Board.Height; y++)
                {
                    var sq = new Square(x, y);
                    var p = Board[sq];
                    if (p != null && p.Color == SideToMove) MoveGenerator.GeneratePseudoLegal(this, sq, p, moves);
                }
            return moves;
        }

        bool IsLegal(Move m)
        {
            var rec = MakeInternal(m);
            bool legal = !IsInCheck(rec.Mover.Color);
            UnmakeInternal(rec);
            return legal;
        }

        MoveRecord MakeInternal(Move m)
        {
            var mover = Board[m.From];
            var before = mover.Definition;
            Piece captured = null, partner = null;
            Square capSq = m.To;
            bool bounced = false;

            if (m.Kind == MoveKind.Castle)
            {
                partner = Board[m.Aux];
                int dir = Math.Sign(m.To.File - m.From.File);
                Board[m.From] = null;
                Board[m.Aux] = null;
                Board[m.To] = mover;
                Board[m.To.Offset(-dir, 0)] = partner;
                partner.MoveCount++;
            }
            else if (Board.IsShielded(m.To) && Board.OccupantAt(m.To).Color != mover.Color)
            {
                // "Second life": the wall is destroyed instead, the attacker goes back where it came from (never left).
                bounced = true;
                captured = Board.WallAt(m.To);
                Board.RemoveWall(m.To);
            }
            else
            {
                if (m.Kind == MoveKind.EnPassant) capSq = m.Aux;
                var occupant = Board.OccupantAt(capSq);
                var wall = Board.WallAt(capSq);
                if (occupant != null && occupant.Color != mover.Color) { captured = occupant; Board[capSq] = null; }
                else if (wall != null && wall.Color != mover.Color) { captured = wall; Board.RemoveWall(capSq); }
                // (an own wall with nobody on it is simply climbed)
                Board[m.From] = null;
                Board[m.To] = mover;
            }

            mover.MoveCount++;
            if (captured != null && !captured.IsWall) mover.CaptureCount++; // walls aren't kills (berserk)
            if (!bounced && m.IsPromotion && PieceLibrary.BySymbol.TryGetValue(m.Promotion, out var promo))
                mover.Definition = promo;

            var rec = new MoveRecord(m, mover, captured, capSq, before, EnPassantSquare, EnPassantVictim, HalfmoveClock, partner, bounced);

            if (m.Kind == MoveKind.DoubleStep)
            {
                EnPassantSquare = new Square(m.From.File, (m.From.Rank + m.To.Rank) / 2);
                EnPassantVictim = m.To;
            }
            else EnPassantSquare = null;

            HalfmoveClock = captured != null || before.Has(PieceAbilities.ResetsFiftyMoveClock) ? 0 : HalfmoveClock + 1;
            if (SideToMove == PieceColor.Black) FullmoveNumber++;
            SideToMove = SideToMove.Opponent();
            _history.Add(rec);
            return rec;
        }

        void UnmakeInternal(MoveRecord rec)
        {
            var m = rec.Move;
            var mover = rec.Mover;
            _history.RemoveAt(_history.Count - 1);

            SideToMove = SideToMove.Opponent();
            if (SideToMove == PieceColor.Black) FullmoveNumber--;
            HalfmoveClock = rec.PrevHalfmoveClock;
            EnPassantSquare = rec.PrevEnPassant;
            EnPassantVictim = rec.PrevEnPassantVictim;

            mover.Definition = rec.DefinitionBefore;
            mover.MoveCount--;
            if (rec.Captured != null && !rec.Captured.IsWall) mover.CaptureCount--;

            if (m.Kind == MoveKind.Castle)
            {
                int dir = Math.Sign(m.To.File - m.From.File);
                Board[m.To] = null;
                Board[m.To.Offset(-dir, 0)] = null;
                Board[m.From] = mover;
                Board[m.Aux] = rec.CastlePartner;
                rec.CastlePartner.MoveCount--;
            }
            else if (rec.Bounced)
            {
                Board[rec.CapturedSquare] = rec.Captured; // put the wall back under its piece
            }
            else
            {
                Board[m.To] = null;                        // clears the piece layer only; an own wall stays
                Board[m.From] = mover;
                if (rec.Captured != null) Board[rec.CapturedSquare] = rec.Captured;
            }
        }

        // ───────────────────────── Game end ─────────────────────────

        void UpdateStatus()
        {
            if (IsOver) return;

            bool anyLegal = false;
            foreach (var m in GeneratePseudoLegalAll())
                if (IsLegal(m)) { anyLegal = true; break; }

            if (!anyLegal)
            {
                if (IsInCheck(SideToMove)) { Status = GameStatus.Checkmate; Winner = SideToMove.Opponent(); }
                else Status = GameStatus.Stalemate;
                return;
            }
            if (IsInsufficientMaterial()) { Status = GameStatus.DrawInsufficientMaterial; return; }
            if (HalfmoveClock >= 100) { Status = GameStatus.DrawFiftyMove; return; }
            if (RepetitionCount() >= 3) { Status = GameStatus.DrawRepetition; return; }
        }

        /// <summary>How many times the current position has occurred (only positions since the last irreversible move can match).</summary>
        public int RepetitionCount()
        {
            ulong h = Hash;
            int count = 0;
            int first = Math.Max(0, _hashes.Count - 1 - HalfmoveClock);
            for (int i = _hashes.Count - 1; i >= first; i--)
                if (_hashes[i] == h) count++;
            return count;
        }

        /// <summary>
        /// K vs K, K+minor vs K, and kings + bishops all on one square colour. Any other (incl. fairy) piece = sufficient.
        /// </summary>
        bool IsInsufficientMaterial()
        {
            int minors = 0, knights = 0;
            int bishopColours = 0; // bit 0 = light, bit 1 = dark
            foreach (var (sq, p) in Board.Pieces())
            {
                if (p.IsRoyal || p.IsWall) continue;
                if (p.Definition == PieceLibrary.Knight) { knights++; minors++; }
                else if (p.Definition == PieceLibrary.Bishop) { minors++; bishopColours |= (sq.File + sq.Rank) % 2 == 0 ? 2 : 1; }
                else return false;
            }
            if (minors == 0) return true;
            if (minors == 1) return true;                 // single bishop or knight
            return knights == 0 && bishopColours != 3;    // only bishops, all on the same colour
        }

        // ───────────────────────── FEN ─────────────────────────

        Board LoadFen(string fen)
        {
            var parts = fen.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var board = Board.FromFen(parts[0]);
            SideToMove = parts.Length > 1 && parts[1] == "b" ? PieceColor.Black : PieceColor.White;
            string castling = parts.Length > 2 ? parts[2] : "-";
            if (parts.Length > 4 && int.TryParse(parts[4], out var hm)) HalfmoveClock = hm;
            if (parts.Length > 5 && int.TryParse(parts[5], out var fm)) FullmoveNumber = fm;

            foreach (var (sq, p) in board.Pieces())
            {
                bool white = p.Color == PieceColor.White;
                int backRank = white ? 0 : board.Height - 1;

                // Pieces with first-move-only primitives (pawns) count as moved unless on their starting rank.
                foreach (var prim in p.Definition.Moves)
                    if ((prim.Flags & MoveFlags.FirstMoveOnly) != 0)
                    {
                        int startRank = white ? 1 : board.Height - 2;
                        if (sq.Rank != startRank) p.MoveCount = 1;
                        break;
                    }

                if (p.Definition.Has(PieceAbilities.Castles))
                {
                    bool any = white ? castling.IndexOfAny(new[] { 'K', 'Q' }) >= 0 : castling.IndexOfAny(new[] { 'k', 'q' }) >= 0;
                    if (!any) p.MoveCount = 1;
                }
                else if (p.Definition.Has(PieceAbilities.CastlePartner))
                {
                    char k = white ? 'K' : 'k', q = white ? 'Q' : 'q';
                    bool unmoved = sq.Rank == backRank &&
                                   ((sq.File == board.Width - 1 && castling.IndexOf(k) >= 0) ||
                                    (sq.File == 0 && castling.IndexOf(q) >= 0));
                    if (!unmoved) p.MoveCount = 1;
                }
            }

            if (parts.Length > 3 && parts[3] != "-" && parts[3].Length >= 2)
            {
                var ep = new Square(parts[3][0] - 'a', parts[3][1] - '1');
                EnPassantSquare = ep;
                EnPassantVictim = ep.Offset(0, SideToMove == PieceColor.White ? -1 : 1);
            }
            return board;
        }

        public string ToFen()
        {
            var sb = new StringBuilder();
            for (int r = Board.Height - 1; r >= 0; r--)
            {
                int empty = 0;
                for (int f = 0; f < Board.Width; f++)
                {
                    var p = Board[new Square(f, r)];
                    if (p == null) { empty++; continue; }
                    if (empty > 0) { sb.Append(empty); empty = 0; }
                    sb.Append(p.Color == PieceColor.White ? p.Definition.Symbol : char.ToLowerInvariant(p.Definition.Symbol));
                }
                if (empty > 0) sb.Append(empty);
                if (r > 0) sb.Append('/');
            }
            sb.Append(SideToMove == PieceColor.White ? " w " : " b ");

            string castle = "";
            foreach (var color in new[] { PieceColor.White, PieceColor.Black })
            {
                int back = color == PieceColor.White ? 0 : Board.Height - 1;
                Square? king = null;
                foreach (var (sq, p) in Board.Pieces())
                    if (p.Color == color && p.Definition.Has(PieceAbilities.Castles) && !p.HasMoved) king = sq;
                if (king == null) continue;
                var hs = Board[new Square(Board.Width - 1, back)];
                var qs = Board[new Square(0, back)];
                bool w = color == PieceColor.White;
                if (hs != null && hs.Color == color && hs.Definition.Has(PieceAbilities.CastlePartner) && !hs.HasMoved) castle += w ? "K" : "k";
                if (qs != null && qs.Color == color && qs.Definition.Has(PieceAbilities.CastlePartner) && !qs.HasMoved) castle += w ? "Q" : "q";
            }
            sb.Append(castle.Length > 0 ? castle : "-");
            sb.Append(' ').Append(EnPassantSquare?.ToString() ?? "-");
            sb.Append(' ').Append(HalfmoveClock).Append(' ').Append(FullmoveNumber);
            return sb.ToString();
        }
    }
}

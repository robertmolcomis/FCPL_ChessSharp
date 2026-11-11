using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>One piece as it stood in a replay frame (everything the view needs to draw it).</summary>
    public readonly struct PieceSnapshot
    {
        /// <summary>The same piece keeps its id in every frame, so the view can slide it instead of redrawing it.</summary>
        public readonly int Id;
        public readonly PieceDefinition Definition;
        public readonly PieceColor Color;
        public readonly Square Square;
        public readonly bool IsBerserk;
        public readonly int CaptureCount;

        public bool IsWall => Definition.IsWall;

        internal PieceSnapshot(Piece p, Square square)
        {
            Id = p.Id; Definition = p.Definition; Color = p.Color; Square = square;
            IsBerserk = p.IsBerserk; CaptureCount = p.CaptureCount;
        }
    }

    /// <summary>The board after <see cref="Ply"/> moves.</summary>
    public sealed class ReplayFrame
    {
        /// <summary>0 = start position, n = after the n-th move.</summary>
        public int Ply { get; internal set; }
        public IReadOnlyList<PieceSnapshot> Pieces { get; internal set; }
        /// <summary>The move that led here (null on the start frame).</summary>
        public Move? LastMove { get; internal set; }
        public PieceColor SideToMove { get; internal set; }
        /// <summary>Royal pieces in check and the pieces giving check.</summary>
        public IReadOnlyList<Square> CheckedRoyals { get; internal set; }
        public IReadOnlyList<Square> Checkers { get; internal set; }
        public GameStatus Status { get; internal set; }
        public PieceColor? Winner { get; internal set; }
        /// <summary>"Start", "12. e2e4" (White's move) or "12... e7e5" (Black's move).</summary>
        public string Label { get; internal set; }
    }

    /// <summary>
    /// A finished (or paused) match turned into one frame per move, for the Replay screen.
    /// All frames are built once up front, so jumping to any move is just drawing a stored frame —
    /// dragging the slider fast costs nothing, no matter how long the game was.
    /// </summary>
    public sealed class ReplayTimeline
    {
        readonly List<ReplayFrame> _frames = new List<ReplayFrame>();

        /// <summary>Frame 0 is the start position; frame <see cref="MoveCount"/> is the final position.</summary>
        public IReadOnlyList<ReplayFrame> Frames => _frames;
        public int MoveCount => _frames.Count - 1;

        /// <summary>Builds the timeline from a move-history file. The matching rules must be loaded. Throws <see cref="SaveFormatException"/>.</summary>
        public static ReplayTimeline FromMovesFile(byte[] movesFile) => FromSavedMoves(MatchRecorder.ReadMovesFile(movesFile));

        public static ReplayTimeline FromSavedMoves(MatchRecorder.SavedMoves saved)
        {
            var timeline = new ReplayTimeline();
            var game = MatchRecorder.BuildStart(saved.Kind, saved.StartPosition);
            timeline._frames.Add(Capture(game, 0, null, "Start"));
            for (int i = 0; i < saved.Moves.Count; i++)
            {
                var move = saved.Moves[i];
                string label = game.SideToMove == PieceColor.White ? $"{game.FullmoveNumber}. {move}" : $"{game.FullmoveNumber}... {move}";
                if (game.TryMakeMove(move) == null) throw new SaveFormatException($"Move {move} in the save isn't legal");
                timeline._frames.Add(Capture(game, i + 1, move, label));
            }

            // A resignation or agreed draw isn't a move: it shows on the last frame.
            if (saved.Outcome.Status != GameStatus.Ongoing)
            {
                PositionCodec.ApplyOutcome(game, saved.Outcome);
                int last = timeline._frames.Count - 1;
                timeline._frames[last] = Capture(game, last, timeline._frames[last].LastMove, timeline._frames[last].Label);
            }
            return timeline;
        }

        static ReplayFrame Capture(Game game, int ply, Move? lastMove, string label)
        {
            var pieces = new List<PieceSnapshot>();
            foreach (var (sq, p) in game.Board.Pieces()) pieces.Add(new PieceSnapshot(p, sq));
            return new ReplayFrame
            {
                Ply = ply,
                Pieces = pieces,
                LastMove = lastMove,
                SideToMove = game.SideToMove,
                CheckedRoyals = game.RoyalsInCheck(game.SideToMove),
                Checkers = game.CheckingPieces(game.SideToMove),
                Status = game.Status,
                Winner = game.Winner,
                Label = label,
            };
        }
    }
}

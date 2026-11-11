using System;

namespace Chess.Core
{
    /// <summary>What <see cref="MatchSave.Continue"/> gives back.</summary>
    public sealed class ContinuedMatch
    {
        public Game Game { get; internal set; }
        /// <summary>Keeps saving the match (the move history goes on from where the files left off).</summary>
        public MatchRecorder Recorder { get; internal set; }
        /// <summary>
        /// True when the move-history file matched the board file, so the game has its full move list (undo, repetition).
        /// False: the board came from the board file alone and undo starts at this position.
        /// </summary>
        public bool HistoryRestored { get; internal set; }
    }

    /// <summary>Continue = build the last match again from its two save files.</summary>
    public static class MatchSave
    {
        /// <summary>
        /// Rebuilds the last match. The board comes from <paramref name="boardFile"/>; if <paramref name="movesFile"/> is there and
        /// replaying it gives exactly the same board, that replayed game is used instead, so undo and the repetition rule work as before.
        /// Load the rules for <see cref="SaveFormat.IsClassic"/> first. Throws <see cref="SaveFormatException"/> when the board file is unusable.
        /// </summary>
        public static ContinuedMatch Continue(byte[] boardFile, byte[] movesFile)
        {
            if (boardFile == null) throw new ArgumentNullException(nameof(boardFile));
            var fromBoard = PositionCodec.ReadBoardFile(boardFile);

            if (movesFile != null)
            {
                try
                {
                    var recorder = MatchRecorder.Resume(MatchRecorder.ReadMovesFile(movesFile));
                    if (SameBytes(recorder.WriteBoardFile(), boardFile))
                        return new ContinuedMatch { Game = recorder.Game, Recorder = recorder, HistoryRestored = true };
                }
                catch (SaveFormatException) { } // damaged or from another match: fall back to the board file
            }

            return new ContinuedMatch { Game = fromBoard, Recorder = MatchRecorder.Start(fromBoard), HistoryRestored = false };
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}

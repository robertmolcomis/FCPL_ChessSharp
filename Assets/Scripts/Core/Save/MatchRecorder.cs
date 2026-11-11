using System;
using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>
    /// Keeps the save files of one match up to date: the move history (for Replay, and so Continue can bring back undo)
    /// and the board state (for Continue).
    ///
    /// Move-history file, after the header:
    /// <code>
    /// start      2 bits      0 = Game.StartFen, 1 = Game.ClassicFen, 2 = a position follows (PositionCodec layout)
    /// moves      γ(count+1)
    /// each move  ⌈log2(legal moves)⌉ bits: the move's index in Game.GetLegalMoves() of that position
    ///            (20 legal moves → 5 bits, 35 → 6 bits, a forced move → 0 bits)
    /// outcome    2 [+1], draw offer 1 [+1]   (same as the board file)
    /// </code>
    /// About 5-6 bits per move, so a 40-move game (80 moves by both sides) is ~60 bytes.
    /// Storing an index instead of from/to squares needs the move generator to replay the game, which is why the
    /// header carries a fingerprint of the rules.
    /// </summary>
    public sealed class MatchRecorder
    {
        internal enum StartKind { Standard = 0, Classic = 1, Position = 2 }

        public Game Game { get; }

        readonly StartKind _startKind;
        readonly BitWriter _startPosition; // only for StartKind.Position

        // A second copy of the game that follows Game move by move, so each new move's index can be found in its position.
        readonly Game _mirror;
        readonly List<(int index, int bits)> _encoded = new List<(int, int)>();

        MatchRecorder(Game game, StartKind kind, BitWriter startPosition)
        {
            Game = game;
            _startKind = kind;
            _startPosition = startPosition;
            _mirror = BuildStart(kind, startPosition);
        }

        /// <summary>
        /// Starts recording <paramref name="game"/>, which must have no moves yet (a new game, or one rebuilt from a board file).
        /// </summary>
        public static MatchRecorder Start(Game game)
        {
            if (game.History.Count > 0) throw new ArgumentException("Start recording before the first move", nameof(game));
            if (game.InitialFen == Game.StartFen) return new MatchRecorder(game, StartKind.Standard, null);
            if (game.InitialFen == Game.ClassicFen) return new MatchRecorder(game, StartKind.Classic, null);

            var position = new BitWriter();
            PositionCodec.Write(position, game, includeOutcome: false);
            return new MatchRecorder(game, StartKind.Position, position);
        }

        // ───────────────────────── writing ─────────────────────────

        /// <summary>Moves encoded so far are reused; only new moves (or undone ones) cost work.</summary>
        public byte[] WriteMovesFile()
        {
            Sync();
            var w = new BitWriter();
            SaveFormat.WriteHeader(w, SaveFormat.FileKind.Moves);
            w.Write((ulong)_startKind, 2);
            if (_startKind == StartKind.Position) w.Append(_startPosition);
            w.WriteGamma((uint)_encoded.Count + 1);
            foreach (var (index, bits) in _encoded) w.Write((ulong)index, bits);
            PositionCodec.WriteOutcome(w, Game);
            return w.ToArray();
        }

        public byte[] WriteBoardFile() => PositionCodec.WriteBoardFile(Game);

        /// <summary>Brings the mirror game to the same moves as <see cref="Game"/> (undoing what was taken back).</summary>
        void Sync()
        {
            var history = Game.History;
            int common = 0;
            while (common < _encoded.Count && common < history.Count &&
                   _mirror.History[common].Move.Equals(history[common].Move))
                common++;

            while (_encoded.Count > common)
            {
                _mirror.Undo();
                _encoded.RemoveAt(_encoded.Count - 1);
            }

            for (int i = common; i < history.Count; i++)
            {
                var legal = _mirror.GetLegalMoves();
                int index = legal.IndexOf(history[i].Move);
                if (index < 0) throw new InvalidOperationException($"Move {history[i].Move} isn't legal in the recorded position");
                _encoded.Add((index, BitWriter.BitsFor((ulong)legal.Count)));
                _mirror.TryMakeMove(legal[index]);
            }
        }

        // ───────────────────────── reading ─────────────────────────

        /// <summary>What a move-history file holds: where the match started and every move, in order.</summary>
        public sealed class SavedMoves
        {
            /// <summary>A fresh game at the start position (no moves made).</summary>
            public Game Start { get; internal set; }
            public List<Move> Moves { get; } = new List<Move>();
            internal PositionCodec.Outcome Outcome;
            internal StartKind Kind;
            internal BitWriter StartPosition;

            /// <summary>
            /// The same match cut after <paramref name="ply"/> moves. A resignation / agreed draw belongs to the end of the
            /// match, so it is kept only when nothing is cut off.
            /// </summary>
            internal SavedMoves Truncated(int ply)
            {
                var cut = new SavedMoves { Kind = Kind, StartPosition = StartPosition };
                cut.Moves.AddRange(Moves.GetRange(0, ply));
                if (ply == Moves.Count) cut.Outcome = Outcome;
                return cut;
            }

            /// <summary>Plays every move on a new copy of the start position (and applies a resignation / agreed draw).</summary>
            public Game PlayOut()
            {
                var game = BuildStart(Kind, StartPosition);
                foreach (var m in Moves)
                    if (game.TryMakeMove(m) == null) throw new SaveFormatException($"Move {m} in the save isn't legal");
                PositionCodec.ApplyOutcome(game, Outcome);
                return game;
            }
        }

        /// <summary>Decodes a move-history file. The matching rules must be loaded. Throws <see cref="SaveFormatException"/>.</summary>
        public static SavedMoves ReadMovesFile(byte[] file)
        {
            var r = new BitReader(file);
            SaveFormat.ReadHeader(r, SaveFormat.FileKind.Moves);

            var history = new SavedMoves { Kind = (StartKind)r.ReadInt(2) };
            if (history.Kind == StartKind.Position)
            {
                // Re-encode the start position so it can be written out again unchanged.
                int from = r.Position;
                var start = PositionCodec.Read(r);
                history.StartPosition = new BitWriter();
                PositionCodec.Write(history.StartPosition, start, includeOutcome: false);
                if (history.StartPosition.BitCount != r.Position - from) throw new SaveFormatException("Bad start position in save");
            }
            else if (history.Kind != StartKind.Standard && history.Kind != StartKind.Classic)
                throw new SaveFormatException("Bad start position in save");

            history.Start = BuildStart(history.Kind, history.StartPosition);
            var game = BuildStart(history.Kind, history.StartPosition);
            int count = (int)r.ReadGamma() - 1;
            for (int i = 0; i < count; i++)
            {
                var legal = game.GetLegalMoves();
                if (legal.Count == 0) throw new SaveFormatException("The save has moves after the game ended");
                int index = r.ReadInt(BitWriter.BitsFor((ulong)legal.Count));
                if (index >= legal.Count) throw new SaveFormatException("Bad move in save");
                history.Moves.Add(legal[index]);
                game.TryMakeMove(legal[index]);
            }
            history.Outcome = PositionCodec.ReadOutcome(r);
            SaveFormat.ExpectEnd(r);
            return history;
        }

        /// <summary>Rebuilds a match from its move history (with full undo) and keeps recording it.</summary>
        public static MatchRecorder Resume(SavedMoves history)
        {
            var game = history.PlayOut();
            return new MatchRecorder(game, history.Kind, history.StartPosition);
        }

        internal static Game BuildStart(StartKind kind, BitWriter startPosition)
        {
            switch (kind)
            {
                case StartKind.Standard: return new Game(Game.StartFen);
                case StartKind.Classic: return new Game(Game.ClassicFen);
                default: return PositionCodec.Read(new BitReader(startPosition.ToArray()));
            }
        }
    }
}

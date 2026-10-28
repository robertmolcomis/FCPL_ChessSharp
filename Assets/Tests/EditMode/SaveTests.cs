using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Chess.Core.Tests
{
    public class SaveTests
    {
        static Square S(string s) => new Square(s[0] - 'a', s[1] - '1');

        static void Play(Game g, params string[] moves)
        {
            foreach (var m in moves)
                Assert.IsNotNull(g.TryMakeMove(new Move(S(m.Substring(0, 2)), S(m.Substring(2, 2)), m.Length > 4 ? char.ToUpper(m[4]) : '\0')),
                                 $"illegal: {m}");
        }

        /// <summary>Saves both files, continues from them, and checks the result is the same game.</summary>
        static ContinuedMatch RoundTrip(MatchRecorder rec, bool expectHistory = true)
        {
            var board = rec.WriteBoardFile();
            var moves = rec.WriteMovesFile();
            var c = MatchSave.Continue(board, moves);
            Assert.AreEqual(expectHistory, c.HistoryRestored);
            AssertSameGame(rec.Game, c.Game);
            if (expectHistory) Assert.AreEqual(rec.Game.History.Count, c.Game.History.Count);
            // Saving the continued game gives the very same files.
            Assert.AreEqual(board, c.Recorder.WriteBoardFile());
            if (expectHistory) Assert.AreEqual(moves, c.Recorder.WriteMovesFile());
            return c;
        }

        static void AssertSameGame(Game expected, Game actual)
        {
            Assert.AreEqual(expected.ToFen(), actual.ToFen());
            Assert.AreEqual(expected.Hash, actual.Hash);
            Assert.AreEqual(expected.Status, actual.Status);
            Assert.AreEqual(expected.Winner, actual.Winner);
            Assert.AreEqual(expected.DrawOfferedBy, actual.DrawOfferedBy);
            Assert.AreEqual(PositionCodec.WriteBoardFile(expected), PositionCodec.WriteBoardFile(actual));
            var a = new List<string>(); foreach (var m in expected.GetLegalMoves()) a.Add(m.ToString());
            var b = new List<string>(); foreach (var m in actual.GetLegalMoves()) b.Add(m.ToString());
            Assert.AreEqual(a, b, "legal moves differ");
        }

        // ───────────────────────── bit stream ─────────────────────────

        [Test]
        public void GammaRoundTrip()
        {
            var w = new BitWriter();
            for (uint n = 1; n < 300; n++) w.WriteGamma(n);
            w.Write(5, 3);
            var r = new BitReader(w.ToArray());
            for (uint n = 1; n < 300; n++) Assert.AreEqual(n, r.ReadGamma());
            Assert.AreEqual(5, r.ReadInt(3));
            Assert.IsTrue(r.AtPaddingOnly());
        }

        // ───────────────────────── board file ─────────────────────────

        [Test]
        public void StartPositionIsSmall()
        {
            var g = new Game();
            var bytes = PositionCodec.WriteBoardFile(g);
            Assert.LessOrEqual(bytes.Length, 40, $"board file is {bytes.Length} bytes");
            AssertSameGame(g, PositionCodec.ReadBoardFile(bytes));
        }

        [Test]
        public void BoardFileKeepsCastlingEnPassantAndClocks()
        {
            var g = new Game(Game.ClassicFen);
            Play(g, "e2e4", "a7a6", "e4e5", "d7d5", "g1f3", "h7h6", "f1e2", "b7b6", "e1g1", "f7f5");
            // f5 just double-stepped: e5xf6 en passant is available.
            var copy = PositionCodec.ReadBoardFile(PositionCodec.WriteBoardFile(g));
            AssertSameGame(g, copy);
            Assert.IsNotNull(copy.TryMakeMove(new Move(S("e5"), S("f6"))), "en passant after loading");
        }

        [Test]
        public void PieceStandingOnItsWall()
        {
            var g = new Game();
            Play(g, "b1c3"); // the knight climbs White's own wall on c3
            Assert.IsTrue(g.Board.IsShielded(S("c3")));
            var copy = PositionCodec.ReadBoardFile(PositionCodec.WriteBoardFile(g));
            AssertSameGame(g, copy);
            Assert.IsTrue(copy.Board.IsShielded(S("c3")));
        }

        [Test]
        public void KeepsCapturesAndBerserk()
        {
            // White pawn on e4 takes twice (d5, then c6) → berserk.
            var g = new Game("4k3/8/2p5/3p4/4P3/8/8/4K3 w - - 0 1");
            Play(g, "e4d5", "e8e7", "d5c6");
            var pawn = g.Board[S("c6")];
            Assert.IsTrue(pawn.IsBerserk);
            var copy = PositionCodec.ReadBoardFile(PositionCodec.WriteBoardFile(g));
            AssertSameGame(g, copy);
            Assert.IsTrue(copy.Board[S("c6")].IsBerserk);
            Assert.AreEqual(2, copy.Board[S("c6")].CaptureCount);
        }

        [Test]
        public void KeepsMoveStreak()
        {
            var g = new Game(Game.ClassicFen);
            Play(g, "g1f3", "g8f6", "f3g5", "b8c6");
            var copy = PositionCodec.ReadBoardFile(PositionCodec.WriteBoardFile(g));
            Assert.AreEqual(2, copy.Board[S("g5")].MoveStreak);
            Assert.AreEqual(1, copy.Board[S("c6")].MoveStreak);
            Assert.AreEqual(0, copy.Board[S("f6")].MoveStreak);
            // The streak goes on after loading exactly as in the original.
            Play(g, "g5e4"); Play(copy, "g5e4");
            Assert.AreEqual(3, copy.Board[S("e4")].MoveStreak);
            AssertSameGame(g, copy);
        }

        // ───────────────────────── continue ─────────────────────────

        [Test]
        public void ContinueRestoresHistoryAndUndo()
        {
            var g = new Game();
            var rec = MatchRecorder.Start(g);
            Play(g, "g1f3", "g8f6", "b1c3", "b8c6", "f3g5", "c6b4");
            var c = RoundTrip(rec);
            for (int i = 0; i < 6; i++) Assert.IsTrue(c.Game.Undo());
            AssertSameGame(new Game(), c.Game);
        }

        [Test]
        public void ContinueWithoutMovesFileUsesBoardFile()
        {
            var g = new Game(Game.ClassicFen);
            var rec = MatchRecorder.Start(g);
            Play(g, "e2e4", "e7e5", "g1f3");
            var c = MatchSave.Continue(rec.WriteBoardFile(), null);
            Assert.IsFalse(c.HistoryRestored);
            AssertSameGame(g, c.Game);
            Assert.IsFalse(c.Game.Undo(), "no history before the continue point");

            // The new recording starts at the continued position and round-trips too.
            Play(c.Game, "b8c6", "f1b5");
            RoundTrip(c.Recorder);
        }

        [Test]
        public void MismatchedMovesFileFallsBackToBoardFile()
        {
            var a = new Game(Game.ClassicFen); var recA = MatchRecorder.Start(a); Play(a, "e2e4");
            var b = new Game(Game.ClassicFen); var recB = MatchRecorder.Start(b); Play(b, "d2d4");
            var c = MatchSave.Continue(recA.WriteBoardFile(), recB.WriteMovesFile());
            Assert.IsFalse(c.HistoryRestored);
            AssertSameGame(a, c.Game);
        }

        [Test]
        public void ResignationAndDrawOfferAreSaved()
        {
            var g = new Game(); var rec = MatchRecorder.Start(g);
            Play(g, "e3e4");
            g.OfferDraw(PieceColor.Black);
            RoundTrip(rec);
            g.Resign(PieceColor.Black);
            var c = RoundTrip(rec);
            Assert.AreEqual(GameStatus.Resignation, c.Game.Status);
            Assert.AreEqual(PieceColor.White, c.Game.Winner);
            Assert.IsTrue(c.Game.Undo()); // un-resigns, like in a live game
            Assert.AreEqual(GameStatus.Ongoing, c.Game.Status);
        }

        [Test]
        public void AgreedDrawIsSaved()
        {
            var g = new Game(); var rec = MatchRecorder.Start(g);
            Play(g, "e3e4");
            g.OfferDraw(PieceColor.Black); g.AcceptDraw();
            Assert.AreEqual(GameStatus.DrawAgreed, RoundTrip(rec).Game.Status);
        }

        [Test]
        public void CheckmateIsSaved()
        {
            var g = new Game(Game.ClassicFen); var rec = MatchRecorder.Start(g);
            Play(g, "f2f3", "e7e5", "g2g4", "d8h4");
            Assert.AreEqual(GameStatus.Checkmate, RoundTrip(rec).Game.Status);
        }

        [Test]
        public void UndoneMovesLeaveTheHistory()
        {
            var g = new Game(); var rec = MatchRecorder.Start(g);
            Play(g, "g1f3", "g8f6", "f3g5");
            rec.WriteMovesFile();
            g.Undo(); g.Undo();
            Play(g, "g8h6");
            var c = RoundTrip(rec);
            Assert.AreEqual(2, c.Game.History.Count);
            Assert.AreEqual("g8h6", c.Game.History[1].Move.ToString());
        }

        [Test]
        public void CustomStartPositionIsSaved()
        {
            var g = new Game("4k3/8/8/8/8/8/4P3/4K2R w K - 0 1"); var rec = MatchRecorder.Start(g);
            Play(g, "e1g1", "e8d7", "e2e4");
            var c = RoundTrip(rec);
            for (int i = 0; i < 3; i++) c.Game.Undo();
            AssertSameGame(new Game("4k3/8/8/8/8/8/4P3/4K2R w K - 0 1"), c.Game);
        }

        [Test]
        public void MoveHistoryIsSmall()
        {
            var g = new Game(); var rec = MatchRecorder.Start(g);
            PlayRandom(g, new Random(7), 80);
            int bytes = rec.WriteMovesFile().Length;
            // header 3 bytes + under 7 bits per move
            Assert.LessOrEqual(bytes, 3 + g.History.Count * 7 / 8 + 2, $"{g.History.Count} moves took {bytes} bytes");
        }

        // ───────────────────────── damaged / foreign files ─────────────────────────

        [Test]
        public void RejectsOtherRules()
        {
            var g = new Game(); var rec = MatchRecorder.Start(g); Play(g, "e3e4");
            var board = rec.WriteBoardFile();
            board[1] ^= 0x10; // a fingerprint bit
            Assert.Throws<SaveFormatException>(() => MatchSave.Continue(board, null));
        }

        [Test]
        public void RejectsTruncatedAndSwappedFiles()
        {
            var g = new Game(); var rec = MatchRecorder.Start(g); Play(g, "e3e4", "e6e5");
            var board = rec.WriteBoardFile();
            var moves = rec.WriteMovesFile();
            Assert.Throws<SaveFormatException>(() => PositionCodec.ReadBoardFile(new byte[] { board[0], board[1], board[2], board[3] }));
            Assert.Throws<SaveFormatException>(() => MatchSave.Continue(moves, board));
            Assert.Throws<SaveFormatException>(() => MatchSave.Continue(new byte[0], null));
        }

        [Test]
        public void ClassicFlagIsReadableBeforeLoadingRules()
        {
            try
            {
                TestData.Install(includePowerUps: false);
                var g = new Game(Game.ClassicFen); var rec = MatchRecorder.Start(g); Play(g, "e2e4");
                var board = rec.WriteBoardFile();
                Assert.IsTrue(SaveFormat.IsClassic(board, SaveFormat.FileKind.Board));
                RoundTrip(rec);

                TestData.Install(includePowerUps: true);
                Assert.Throws<SaveFormatException>(() => MatchSave.Continue(board, null), "classic save with power-up rules");
            }
            finally { TestData.Install(); }
        }

        // ───────────────────────── random games ─────────────────────────

        static void PlayRandom(Game g, Random rng, int plies)
        {
            for (int i = 0; i < plies && !g.IsOver; i++)
            {
                var legal = g.GetLegalMoves();
                // Prefer captures now and then so power-ups (berserk, paladin) and walls get exercised.
                var captures = legal.FindAll(g.IsCapture);
                var pick = captures.Count > 0 && rng.Next(3) == 0 ? captures[rng.Next(captures.Count)] : legal[rng.Next(legal.Count)];
                Assert.IsNotNull(g.TryMakeMove(pick));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RandomGamesRoundTrip(bool powerUps)
        {
            try
            {
                TestData.Install(powerUps);
                var rng = new Random(powerUps ? 1234 : 4321);
                for (int game = 0; game < 8; game++)
                {
                    var g = new Game(powerUps ? Game.StartFen : Game.ClassicFen);
                    var rec = MatchRecorder.Start(g);
                    int length = rng.Next(1, 160);
                    for (int done = 0; done < length && !g.IsOver; done += 20)
                    {
                        PlayRandom(g, rng, 20);
                        if (rng.Next(4) == 0 && g.History.Count > 2) { g.Undo(); g.Undo(); }
                        RoundTrip(rec);
                    }

                    // From the board file alone, and keep playing both copies identically.
                    var c = MatchSave.Continue(rec.WriteBoardFile(), null);
                    AssertSameGame(g, c.Game);
                    var seed = rng.Next();
                    PlayRandom(g, new Random(seed), 20);
                    PlayRandom(c.Game, new Random(seed), 20);
                    AssertSameGame(g, c.Game);
                    RoundTrip(c.Recorder);
                }
            }
            finally { TestData.Install(); }
        }
    }
}

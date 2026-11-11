using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Chess.Core.Tests
{
    public class ReplayTests
    {
        static Square S(string s) => new Square(s[0] - 'a', s[1] - '1');

        static void Play(Game g, params string[] moves)
        {
            foreach (var m in moves)
                Assert.IsNotNull(g.TryMakeMove(new Move(S(m.Substring(0, 2)), S(m.Substring(2, 2)), m.Length > 4 ? char.ToUpper(m[4]) : '\0')),
                                 $"illegal: {m}");
        }

        static ReplayTimeline TimelineOf(MatchRecorder rec) => ReplayTimeline.FromMovesFile(rec.WriteMovesFile());

        /// <summary>"e4:white pawn" for every piece, sorted — compares a frame with a live board.</summary>
        static List<string> Describe(IEnumerable<(Square sq, PieceDefinition def, PieceColor color)> pieces)
        {
            var list = new List<string>();
            foreach (var (sq, def, color) in pieces) list.Add($"{sq}:{color} {def.Id}");
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static List<string> Describe(ReplayFrame f)
        {
            var items = new List<(Square, PieceDefinition, PieceColor)>();
            foreach (var p in f.Pieces) items.Add((p.Square, p.Definition, p.Color));
            return Describe(items);
        }

        static List<string> Describe(Game g)
        {
            var items = new List<(Square, PieceDefinition, PieceColor)>();
            foreach (var (sq, p) in g.Board.Pieces()) items.Add((sq, p.Definition, p.Color));
            return Describe(items);
        }

        [Test]
        public void OneFramePerMovePlusStart()
        {
            var g = new Game(); var rec = MatchRecorder.Start(g);
            var positions = new List<List<string>> { Describe(g) };
            foreach (var m in new[] { "g1f3", "g8f6", "b1c3", "e6e5", "f3e5" })
            {
                Play(g, m);
                positions.Add(Describe(g));
            }

            var t = TimelineOf(rec);
            Assert.AreEqual(5, t.MoveCount);
            Assert.AreEqual(6, t.Frames.Count);
            for (int i = 0; i < t.Frames.Count; i++)
            {
                Assert.AreEqual(i, t.Frames[i].Ply);
                Assert.AreEqual(positions[i], Describe(t.Frames[i]), $"frame {i}");
            }
            Assert.IsNull(t.Frames[0].LastMove);
            Assert.AreEqual("f3e5", t.Frames[5].LastMove.ToString());
        }

        [Test]
        public void PiecesKeepTheirIdAcrossFrames()
        {
            var g = new Game(Game.ClassicFen); var rec = MatchRecorder.Start(g);
            Play(g, "e2e4", "e7e5", "g1f3");
            var t = TimelineOf(rec);
            int IdAt(ReplayFrame f, string sq) { foreach (var p in f.Pieces) if (p.Square == S(sq)) return p.Id; return -1; }
            Assert.AreEqual(IdAt(t.Frames[0], "g1"), IdAt(t.Frames[3], "f3"), "the knight is the same piece before and after its move");
            Assert.AreEqual(IdAt(t.Frames[0], "e2"), IdAt(t.Frames[1], "e4"));
        }

        [Test]
        public void CastlingFrameMovesTheRook()
        {
            var g = new Game("4k3/8/8/8/8/8/8/4K2R w K - 0 1"); var rec = MatchRecorder.Start(g);
            Play(g, "e1g1");
            var last = TimelineOf(rec).Frames[1];
            CollectionAssert.Contains(Describe(last), "f1:White rook");
            CollectionAssert.Contains(Describe(last), "g1:White king");
        }

        [Test]
        public void CheckmateShowsOnLastFrame()
        {
            var g = new Game(Game.ClassicFen); var rec = MatchRecorder.Start(g);
            Play(g, "f2f3", "e7e5", "g2g4", "d8h4");
            var t = TimelineOf(rec);
            var last = t.Frames[t.MoveCount];
            Assert.AreEqual(GameStatus.Checkmate, last.Status);
            Assert.AreEqual(PieceColor.Black, last.Winner);
            CollectionAssert.Contains(last.CheckedRoyals, S("e1"));
            CollectionAssert.Contains(last.Checkers, S("h4"));
            Assert.AreEqual(GameStatus.Ongoing, t.Frames[3].Status);
        }

        [Test]
        public void ResignationShowsOnLastFrame()
        {
            var g = new Game(); var rec = MatchRecorder.Start(g);
            Play(g, "e3e4");
            g.Resign(PieceColor.Black);
            var t = TimelineOf(rec);
            Assert.AreEqual(GameStatus.Resignation, t.Frames[1].Status);
            Assert.AreEqual(PieceColor.White, t.Frames[1].Winner);
            Assert.AreEqual(GameStatus.Ongoing, t.Frames[0].Status);
        }

        [Test]
        public void PromotionAndBerserkAreInTheFrames()
        {
            var g = new Game("4k3/1P6/2p5/3p4/4P3/8/8/4K3 w - - 0 1"); var rec = MatchRecorder.Start(g);
            Play(g, "e4d5", "e8e7", "d5c6", "e7e6", "b7b8q");
            var t = TimelineOf(rec);
            PieceSnapshot At(ReplayFrame f, string sq) { foreach (var p in f.Pieces) if (p.Square == S(sq)) return p; throw new Exception(sq); }
            Assert.AreEqual(1, At(t.Frames[1], "d5").CaptureCount);
            Assert.IsFalse(At(t.Frames[1], "d5").IsBerserk);
            Assert.IsTrue(At(t.Frames[3], "c6").IsBerserk);
            Assert.AreEqual("pawn", At(t.Frames[4], "b7").Definition.Id);
            Assert.AreEqual("queen", At(t.Frames[5], "b8").Definition.Id);
            Assert.AreEqual(At(t.Frames[4], "b7").Id, At(t.Frames[5], "b8").Id, "promotion keeps the piece id");
        }

        [Test]
        public void Labels()
        {
            var g = new Game(Game.ClassicFen); var rec = MatchRecorder.Start(g);
            Play(g, "e2e4", "e7e5", "g1f3");
            var t = TimelineOf(rec);
            Assert.AreEqual("Start", t.Frames[0].Label);
            Assert.AreEqual("1. e2e4", t.Frames[1].Label);
            Assert.AreEqual("1... e7e5", t.Frames[2].Label);
            Assert.AreEqual("2. g1f3", t.Frames[3].Label);
        }

        [Test]
        public void EmptyMatchHasOnlyTheStartFrame()
        {
            var g = new Game(); var rec = MatchRecorder.Start(g);
            var t = TimelineOf(rec);
            Assert.AreEqual(0, t.MoveCount);
            Assert.AreEqual(Describe(g), Describe(t.Frames[0]));
        }

        [Test]
        public void LongRandomGameMatchesEveryPosition()
        {
            var g = new Game(); var rec = MatchRecorder.Start(g);
            var rng = new Random(11);
            var positions = new List<List<string>> { Describe(g) };
            for (int i = 0; i < 150 && !g.IsOver; i++)
            {
                var legal = g.GetLegalMoves();
                g.TryMakeMove(legal[rng.Next(legal.Count)]);
                positions.Add(Describe(g));
            }
            var t = TimelineOf(rec);
            Assert.AreEqual(positions.Count, t.Frames.Count);
            for (int i = 0; i < positions.Count; i++) Assert.AreEqual(positions[i], Describe(t.Frames[i]), $"frame {i}");
        }
    }
}

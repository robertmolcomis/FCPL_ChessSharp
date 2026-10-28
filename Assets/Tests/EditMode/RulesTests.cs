using NUnit.Framework;

namespace Chess.Core.Tests
{
    public class RulesTests
    {
        static Square S(string s) => new Square(s[0] - 'a', s[1] - '1');
        static Move M(string from, string to, char promo = '\0') => new Move(S(from), S(to), promo);

        static void Play(Game g, params string[] moves)
        {
            foreach (var m in moves)
                Assert.IsNotNull(g.TryMakeMove(M(m.Substring(0, 2), m.Substring(2, 2), m.Length > 4 ? char.ToUpper(m[4]) : '\0')), $"illegal: {m}");
        }

        [Test]
        public void FoolsMate()
        {
            var g = new Game(Game.ClassicFen);
            Play(g, "f2f3", "e7e5", "g2g4", "d8h4");
            Assert.AreEqual(GameStatus.Checkmate, g.Status);
            Assert.AreEqual(PieceColor.Black, g.Winner);
        }

        [Test] public void Stalemate() => Assert.AreEqual(GameStatus.Stalemate, new Game("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1").Status);

        [Test] public void KingVsKing() => Assert.AreEqual(GameStatus.DrawInsufficientMaterial, new Game("8/8/4k3/8/8/4K3/8/8 w - - 0 1").Status);
        [Test] public void SameColourBishops() => Assert.AreEqual(GameStatus.DrawInsufficientMaterial, new Game("8/8/4k3/2b5/8/4K3/3B4/8 w - - 0 1").Status);
        [Test] public void OppositeColourBishopsPlayOn() => Assert.AreEqual(GameStatus.Ongoing, new Game("8/8/4k3/3b4/8/4K3/3B4/8 w - - 0 1").Status);

        [Test]
        public void FiftyMoveRule()
        {
            var g = new Game("8/8/4k3/8/8/4K3/4R3/8 w - - 99 80");
            Play(g, "e2a2");
            Assert.AreEqual(GameStatus.DrawFiftyMove, g.Status);
        }

        [Test]
        public void ThreefoldRepetition()
        {
            var g = new Game(Game.ClassicFen);
            Play(g, "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8");
            Assert.AreEqual(GameStatus.DrawRepetition, g.Status);
        }

        [Test]
        public void EnPassant()
        {
            var g = new Game(Game.ClassicFen);
            Play(g, "e2e4", "a7a6", "e4e5", "d7d5");
            var rec = g.TryMakeMove(M("e5", "d6"));
            Assert.IsNotNull(rec);
            Assert.IsNotNull(rec.Captured);
            Assert.IsNull(g.Board[S("d5")]);
        }

        [Test]
        public void CastlingMovesRook()
        {
            var g = new Game("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            Play(g, "e1c1");
            Assert.AreEqual(PieceLibrary.Rook, g.Board[S("d1")].Definition);
            Assert.AreEqual(PieceLibrary.King, g.Board[S("c1")].Definition);
        }

        [Test]
        public void CannotCastleThroughCheck()
        {
            var g = new Game("r3k2r/8/8/8/8/8/5r2/R3K2R w KQkq - 0 1");
            Assert.IsFalse(g.GetMovesFrom(S("e1")).Exists(m => m.To == S("g1")));
        }

        [Test]
        public void PromotionRequiresChoiceAndUndoRestoresPawn()
        {
            const string fen = "8/4P1k1/8/8/8/8/6K1/8 w - - 0 1";
            var g = new Game(fen);
            Assert.IsNull(g.TryMakeMove(M("e7", "e8")));
            Assert.IsNotNull(g.TryMakeMove(M("e7", "e8", 'N')));
            Assert.AreEqual(PieceLibrary.Knight, g.Board[S("e8")].Definition);
            g.Undo();
            Assert.AreEqual(fen, g.ToFen());
        }

        [Test]
        public void ResignAndDraw()
        {
            var g = new Game(Game.ClassicFen);
            g.Resign(PieceColor.White);
            Assert.AreEqual(PieceColor.Black, g.Winner);

            var d = new Game(Game.ClassicFen);
            d.OfferDraw(PieceColor.White);
            d.AcceptDraw();
            Assert.AreEqual(GameStatus.DrawAgreed, d.Status);
        }

        [Test]
        public void PinnedPieceCannotMove()
        {
            var g = new Game("4k3/4r3/8/8/8/8/4B3/4K3 w - - 0 1");
            Assert.IsEmpty(g.GetMovesFrom(S("e2")));
        }
    
        // ───── Berserk pawn (M3) ─────

        const string TwoVictims = "4k3/8/8/2p5/3p4/4P3/8/4K3 w - - 0 1";

        [Test]
        public void PawnGoesBerserkAfterTwoCaptures()
        {
            var g = new Game(TwoVictims);
            Play(g, "e3d4");
            Assert.IsFalse(g.Board[S("d4")].IsBerserk, "one capture is not enough");
            Play(g, "e8e7", "d4c5");
            Assert.IsTrue(g.Board[S("c5")].IsBerserk);
        }

        [Test]
        public void UndoRemovesBerserk()
        {
            var g = new Game(TwoVictims);
            Play(g, "e3d4", "e8e7", "d4c5");
            Assert.IsTrue(g.Undo());
            Assert.IsFalse(g.Board[S("d4")].IsBerserk);
        }

        [Test]
        public void BerserkCapturesStraightAhead()
        {
            var g = new Game("4k3/8/1r6/1p6/2p5/3P4/8/4K3 w - - 0 1");
            Assert.IsFalse(new Game("4k3/8/1r6/1P6/8/8/8/4K3 w - - 0 1").GetMovesFrom(S("b5")).Exists(m => m.To == S("b6")),
                "a normal pawn can't capture forward");
            Play(g, "d3c4", "e8e7", "c4b5", "e7e6");
            Play(g, "b5b6");
            Assert.IsNull(g.Board[S("b5")]);
        }

        [Test]
        public void BerserkCapturesStraightBehind()
        {
            var g = new Game("4k3/8/8/1p6/2p4r/3P4/8/4K3 w - - 0 1");
            Play(g, "d3c4", "e8e7", "c4b5", "h4b4");
            Play(g, "b5b4");
            Assert.IsTrue(g.Board[S("b4")].IsBerserk, "berserk is permanent");
        }

        [Test]
        public void BerserkStillCannotMoveBackwardToEmptySquare()
        {
            var g = new Game(TwoVictims);
            Play(g, "e3d4", "e8e7", "d4c5", "e7e6");
            Assert.IsFalse(g.GetMovesFrom(S("c5")).Exists(m => m.To == S("c4")));
        }

        [Test]
        public void BerserkPawnGivesCheckStraightAhead()
        {
            var g = new Game("8/1k6/8/2p5/3p4/4P3/8/4K3 w - - 0 1");
            Play(g, "e3d4", "b7c6", "d4c5");
            Assert.IsTrue(g.IsInCheck(PieceColor.Black));
        }

        [Test]
        public void KingCannotStepInFrontOfOrBehindBerserkPawn()
        {
            var g = new Game("8/8/8/1kp5/3p4/4P3/8/4K3 w - - 0 1");
            Play(g, "e3d4", "b5a5", "d4c5", "a5b4");
            Assert.IsFalse(g.IsInCheck(PieceColor.Black));
            Play(g, "e1e2");
            var kingMoves = g.GetMovesFrom(S("b4"));
            Assert.IsFalse(kingMoves.Exists(m => m.To == S("c4")), "c4 is behind the berserk pawn");
        }

        [Test]
        public void BerserkForwardAttackGivesCheckmate()
        {
            // c5 pawn takes b6 then a7 → berserk on a7, attacking a8 straight ahead. b8 (Nd7), b7 (Kc6), a7 (Be3) covered.
            var g = new Game("k7/b2N3p/1nK5/2P5/8/4B3/8/8 w - - 0 1");
            Play(g, "c5b6", "h7h6", "b6a7");
            Assert.AreEqual(GameStatus.Checkmate, g.Status);
            Assert.AreEqual(PieceColor.White, g.Winner);
        }

        // ───── Walls (M3) ─────

        [Test]
        public void StartLayoutHasWallsAndAdvancedCentrePawns()
        {
            var g = new Game();
            foreach (var w in new[] { "c3", "f3", "c6", "f6" }) Assert.IsTrue(g.Board[S(w)].IsWall, w);
            Assert.AreEqual(PieceColor.White, g.Board[S("c3")].Color);
            Assert.AreEqual(PieceColor.Black, g.Board[S("f6")].Color);
            foreach (var p in new[] { "d3", "e3", "d6", "e6" }) Assert.AreEqual(PieceLibrary.Pawn, g.Board[S(p)].Definition, p);
            Assert.IsNull(g.Board[S("d2")]);
            Assert.IsNull(g.Board[S("d7")]);
        }

        [Test]
        public void WallsNeverMove() => Assert.IsEmpty(new Game().GetMovesFrom(S("c3")));

        [Test]
        public void PieceCanClimbOwnWallButNotPassIt()
        {
            var g = new Game();
            var pawn = g.GetMovesFrom(S("c2"));
            Assert.IsTrue(pawn.Exists(m => m.To == S("c3")), "pawn steps onto its wall");
            Assert.IsFalse(pawn.Exists(m => m.To == S("c4")), "no double step over the wall");
            Play(g, "c2c3");
            Assert.IsTrue(g.Board.IsShielded(S("c3")));
            Assert.AreEqual(PieceLibrary.Pawn, g.Board[S("c3")].Definition);
            Assert.IsTrue(g.Board.WallAt(S("c3")) != null);
        }

        [Test]
        public void SliderStopsOnWall()
        {
            // White rook a3: can climb own wall c3 but not go past it.
            var g = new Game("4k3/8/8/8/8/R1W5/8/4K3 w - - 0 1");
            var moves = g.GetMovesFrom(S("a3"));
            Assert.IsTrue(moves.Exists(m => m.To == S("c3")));
            Assert.IsFalse(moves.Exists(m => m.To == S("d3")));
        }

        [Test]
        public void KnightCannotJumpOverWall()
        {
            // b1→c3 passes b2,b3. Wall on b2 blocks it; b1→a3 also passes b2,b3.
            var g = new Game("r3k3/8/8/8/8/8/1W6/1N2K3 w - - 0 1");
            var moves = g.GetMovesFrom(S("b1"));
            Assert.IsFalse(moves.Exists(m => m.To == S("c3") || m.To == S("a3")));
            Assert.IsTrue(moves.Exists(m => m.To == S("d2")), "b1→d2 passes c1,d1 — not blocked");
        }

        [Test]
        public void KnightBehindWallGivesNoCheck()
        {
            // black knight d3 → e1 would pass d2? (long leg vertical: d2, d1) — wall on d2 blocks the check.
            Assert.IsFalse(new Game("4k3/8/8/8/8/3n4/3W4/4K3 w - - 0 1").IsInCheck(PieceColor.White));
            Assert.IsTrue(new Game("4k3/8/8/8/8/3n4/8/4K3 w - - 0 1").IsInCheck(PieceColor.White));
        }

        [Test]
        public void EmptyEnemyWallIsCapturedNormally()
        {
            var g = new Game("4k3/8/8/8/8/2w5/8/2R1K3 w - - 0 1");
            Play(g, "c1c3");
            Assert.AreEqual(PieceLibrary.Rook, g.Board[S("c3")].Definition);
            Assert.IsNull(g.Board.WallAt(S("c3")));
            Assert.AreEqual(0, g.Board[S("c3")].CaptureCount, "a wall is not a kill");
        }

        [Test]
        public void CapturingPieceOnWallDestroysWallAndBounces()
        {
            // black knight stands on its wall c6; white rook c1 "captures" it.
            var g2 = new Game("4k3/8/2w5/8/3n4/8/8/2R1K3 b - - 0 1");
            Play(g2, "d4c6");
            Assert.IsTrue(g2.Board.IsShielded(S("c6")));
            var rec = g2.TryMakeMove(M("c1", "c6"));
            Assert.IsNotNull(rec);
            Assert.IsTrue(rec.Bounced);
            Assert.AreEqual(PieceLibrary.Rook, g2.Board[S("c1")].Definition, "rook went back");
            Assert.AreEqual(PieceLibrary.Knight, g2.Board[S("c6")].Definition, "knight survives");
            Assert.IsNull(g2.Board.WallAt(S("c6")), "wall destroyed");
            Assert.IsTrue(g2.Undo());
            Assert.IsTrue(g2.Board.IsShielded(S("c6")), "undo restores the wall");
        }

        [Test]
        public void KingOnWallIsNotInCheck()
        {
            // White king on its wall e2, attacked by black rook e8 — it can't be captured, so no check.
            var g2 = new Game("4r2k/8/8/8/8/8/3KW3/8 w - - 0 1");
            Play(g2, "d2e2");
            Assert.IsTrue(g2.Board.IsShielded(S("e2")));
            Play(g2, "h8g8");
            Assert.IsFalse(g2.IsInCheck(PieceColor.White));
            Assert.AreNotEqual(GameStatus.Checkmate, g2.Status);
        }

        [Test]
        public void BerserkIgnoresWallKills()
        {
            var g = new Game("4k3/8/8/8/8/3w4/4P3/4K3 w - - 0 1");
            Play(g, "e2d3");
            Assert.AreEqual(0, g.Board[S("d3")].CaptureCount);
        }

        [Test]
        public void BreakingWallUnderPieceIsNotAKill()
        {
            // black knight on its wall c6; the white pawn b5 "captures" it: the wall breaks, the pawn stays on b5.
            var g = new Game("4k3/8/2w5/1P6/3n4/8/8/4K3 b - - 0 1");
            Play(g, "d4c6", "b5c6");
            Assert.IsNull(g.Board.WallAt(S("c6")));
            Assert.AreEqual(0, g.Board[S("b5")].CaptureCount);
        }

        // ───── Archbishop ─────

        [Test]
        public void OneArchbishopInFrontOfEachKing()
        {
            var g = new Game();
            Assert.AreEqual(PieceLibrary.Archbishop, g.Board[S("e2")].Definition);
            Assert.AreEqual(PieceColor.White, g.Board[S("e2")].Color);
            Assert.AreEqual(PieceLibrary.Archbishop, g.Board[S("e7")].Definition);
            Assert.AreEqual(PieceColor.Black, g.Board[S("e7")].Color);
            int count = 0;
            foreach (var (_, p) in g.Board.Pieces()) if (p.Definition == PieceLibrary.Archbishop) count++;
            Assert.AreEqual(2, count, "one per side");
        }

        [Test]
        public void ArchbishopMovesLikeBishopAndKnight()
        {
            var g = new Game("4k3/8/8/8/3A4/8/8/4K3 w - - 0 1");
            var moves = g.GetMovesFrom(S("d4"));
            Assert.IsTrue(moves.Exists(m => m.To == S("h8")), "bishop slide");
            Assert.IsTrue(moves.Exists(m => m.To == S("a1")), "bishop slide");
            Assert.IsTrue(moves.Exists(m => m.To == S("e6")), "knight jump");
            Assert.IsTrue(moves.Exists(m => m.To == S("b3")), "knight jump");
            Assert.IsFalse(moves.Exists(m => m.To == S("d5")), "no rook moves");
            Assert.AreEqual(13 + 8, moves.Count);
        }

        [Test]
        public void ArchbishopGivesKnightCheck()
        {
            Assert.IsTrue(new Game("8/8/8/4k3/8/3A4/8/4K3 b - - 0 1").IsInCheck(PieceColor.Black));
        }

        [Test]
        public void ArchbishopKnightJumpBlockedByWall()
        {
            // e2→d4 passes e3,e4: blocked by a wall on e3. Same jump without the wall works.
            Assert.IsFalse(new Game("4k3/8/8/8/8/4W3/4A3/4K3 w - - 0 1").GetMovesFrom(S("e2")).Exists(m => m.To == S("d4")));
            Assert.IsTrue(new Game("4k3/8/8/8/8/8/4A3/4K3 w - - 0 1").GetMovesFrom(S("e2")).Exists(m => m.To == S("d4")));
        }

        // ───────── Check signal: pieces giving check ─────────

        [Test]
        public void CheckingPiecesSingleCheck()
        {
            var g = new Game("4k3/8/8/8/8/8/8/R3K2r w - - 0 1"); // black rook h1 checks e1
            CollectionAssert.AreEquivalent(new[] { S("h1") }, g.CheckingPieces(PieceColor.White));
        }

        [Test]
        public void CheckingPiecesEmptyWhenNotInCheck()
        {
            Assert.IsEmpty(new Game(Game.ClassicFen).CheckingPieces(PieceColor.White));
        }

        [Test]
        public void CheckingPiecesDoubleAndDiscoveredCheck()
        {
            // The knight on e3 blocks the rook on e8; moving it to g2 checks e1 and uncovers the rook.
            var g = new Game("4r1k1/8/8/8/8/4n3/8/4K3 b - - 0 1");
            Play(g, "e3g2");
            CollectionAssert.AreEquivalent(new[] { S("g2"), S("e8") }, g.CheckingPieces(PieceColor.White));
        }

        [Test]
        public void CheckingPiecesUseCapturePatternNotMoves()
        {
            // A pawn straight in front of the king doesn't check; a diagonal one does.
            Assert.IsEmpty(new Game("8/8/8/8/8/4p3/4K3/7k w - - 0 1").CheckingPieces(PieceColor.White));
            CollectionAssert.AreEquivalent(new[] { S("d3") },
                new Game("8/8/8/8/8/3p4/4K3/7k w - - 0 1").CheckingPieces(PieceColor.White));
        }
}
}

using NUnit.Framework;

namespace Chess.Core.Tests
{
    /// <summary>
    /// Perft = number of leaf nodes in the legal move tree. Matching the reference numbers proves
    /// move generation (incl. castling, en passant, promotion, pins, checks) is correct.
    /// Reference values: https://www.chessprogramming.org/Perft_Results
    /// </summary>
    public class PerftTests
    {
        const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
        const string Position3 = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";
        const string Position4 = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1";
        const string Position5 = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";

        [TestCase(1, 20)]
        [TestCase(2, 400)]
        [TestCase(3, 8902)]
        [TestCase(4, 197281)]
        public void StartPosition(int depth, long expected) => Assert.AreEqual(expected, new Game().Perft(depth));

        [Test, Explicit("Slow (~10-30 s in the editor)")]
        public void StartPosition_Depth5() => Assert.AreEqual(4865609, new Game().Perft(5));

        [TestCase(Kiwipete, 1, 48)]
        [TestCase(Kiwipete, 2, 2039)]
        [TestCase(Kiwipete, 3, 97862)]
        [TestCase(Position3, 1, 14)]
        [TestCase(Position3, 2, 191)]
        [TestCase(Position3, 3, 2812)]
        [TestCase(Position3, 4, 43238)]
        [TestCase(Position4, 1, 6)]
        [TestCase(Position4, 2, 264)]
        [TestCase(Position4, 3, 9467)]
        [TestCase(Position5, 1, 44)]
        [TestCase(Position5, 2, 1486)]
        [TestCase(Position5, 3, 62379)]
        public void TestPositions(string fen, int depth, long expected) => Assert.AreEqual(expected, new Game(fen).Perft(depth));

        [Test]
        public void PerftLeavesPositionUnchanged()
        {
            var g = new Game(Kiwipete);
            g.Perft(3);
            Assert.AreEqual(Kiwipete, g.ToFen());
        }
    }
}

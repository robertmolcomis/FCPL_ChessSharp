using System.Linq;
using NUnit.Framework;

namespace Chess.Core.Tests
{
    /// <summary>pieces.json / abilities.json loading and validation. These tests never install the data they load.</summary>
    public class RulesDataTests
    {
        static RulesData Load(string pieces = null, string abilities = null) =>
            RulesDataLoader.Load(pieces ?? TestData.PiecesJson, abilities ?? TestData.AbilitiesJson);

        static void AssertError(string expectedPart, string pieces = null, string abilities = null)
        {
            var e = Assert.Throws<RulesDataException>(() => Load(pieces, abilities));
            StringAssert.Contains(expectedPart, e.Message);
        }

        static string Pieces(string from, string to)
        {
            var s = TestData.PiecesJson;
            Assert.IsTrue(s.Contains(from), $"test setup: pieces.json no longer contains {from}");
            return s.Replace(from, to);
        }

        static string Abilities(string from, string to)
        {
            var s = TestData.AbilitiesJson;
            Assert.IsTrue(s.Contains(from), $"test setup: abilities.json no longer contains {from}");
            return s.Replace(from, to);
        }

        // ───────── the real files ─────────

        [Test]
        public void DefaultFilesLoad()
        {
            var d = Load();
            CollectionAssert.IsSupersetOf(d.PiecesByName.Keys,
                new[] { "pawn", "knight", "bishop", "rook", "queen", "king", "archbishop", "chancellor", "wall" });
            CollectionAssert.AreEqual(new[] { 'Q', 'R', 'B', 'N' }, d.PromotionChoices.ToArray());
            Assert.IsTrue(d.PiecesByName["king"].IsRoyal);
            Assert.IsTrue(d.PiecesByName["pawn"].PromotesOnLastRank);
            Assert.IsTrue(d.PiecesByName["pawn"].AbilityIds.Contains("BerserkPawn"));
            Assert.AreEqual(AbilityCategory.PowerUp, d.Abilities["BerserkPawn"].Category);
            Assert.AreEqual(AbilityCategory.Rule, d.Abilities["Castles"].Category);
        }

        [Test]
        public void RawJsonIsKeptForSaveSnapshots()
        {
            var d = Load();
            Assert.AreEqual(TestData.PiecesJson, d.PiecesJson);
            Assert.AreEqual(TestData.AbilitiesJson, d.AbilitiesJson);
        }

        [Test]
        public void PawnMovesDifferFromCaptures()
        {
            var pawn = Load().PiecesByName["pawn"];
            Assert.IsTrue(pawn.Moves.Where(m => m.Dx == 0).All(m => (m.Flags & MoveFlags.MoveOnly) != 0));
            Assert.IsTrue(pawn.Moves.Where(m => m.Dx != 0).All(m => (m.Flags & MoveFlags.CaptureOnly) != 0));
        }

        [Test]
        public void BerserkUsesParamsFromAbilitiesJson()
        {
            var pawn = Load(abilities: Abilities("\"threshold\": 2", "\"threshold\": 3")).PiecesByName["pawn"];
            Assert.AreEqual(3, pawn.BerserkCaptureThreshold);

            // "replace": berserk capture set = exactly the ability's pattern; pushes are kept.
            var caps = pawn.BerserkMoves.Where(m => (m.Flags & MoveFlags.CaptureOnly) != 0).Select(m => (m.Dx, m.Dy)).ToArray();
            CollectionAssert.AreEquivalent(new[] { (-1, 1), (0, 1), (1, 1), (0, -1) }, caps);
            Assert.AreEqual(2, pawn.BerserkMoves.Count(m => (m.Flags & MoveFlags.MoveOnly) != 0));
        }

        // ───────── validation errors ─────────

        [Test] public void InvalidJson() => AssertError("invalid JSON", pieces: "{ not json");
        [Test] public void WrongVersion() => AssertError("version 2", pieces: Pieces("\"version\": 1", "\"version\": 2"));

        [Test] public void UnknownFlag() =>
            AssertError("flag 'Flying' is not defined", pieces: Pieces("\"flags\": [\"CastlePartner\"]", "\"flags\": [\"Flying\"]"));

        [Test] public void AbilityNotAllowedOnPiece() =>
            AssertError("can't be given to 'rook'", pieces: Pieces("\"flags\": [\"CastlePartner\"]", "\"flags\": [\"CastlePartner\", \"BerserkPawn\"]"));

        [Test] public void AbilityWithoutEngineBehaviour() =>
            AssertError("no behaviour", abilities: Abilities("\"id\": \"CastlePartner\"", "\"id\": \"Teleport\""));

        [Test] public void BadBerserkThreshold() =>
            AssertError("threshold", abilities: Abilities("\"threshold\": 2", "\"threshold\": \"two\""));

        [Test] public void UnknownPromotionChoice() =>
            AssertError("unknown piece 'dragon'", abilities: Abilities("\"choices\": [\"queen\"", "\"choices\": [\"dragon\""));

        [Test] public void DuplicateSymbol() =>
            AssertError("already used", pieces: Pieces("\"symbol\": \"C\"", "\"symbol\": \"A\""));

        [Test] public void ZeroDirection() =>
            AssertError("[0, 0]", pieces: Pieces("{ \"dirs\": [[0, 1]] },", "{ \"dirs\": [[0, 0]] },"));

        [Test] public void BadCapturesValue() =>
            AssertError("\"same\"", pieces: Pieces("\"captures\": \"same\"", "\"captures\": \"foo\""));

        [Test] public void AppliesToUnknownPiece() =>
            AssertError("unknown piece 'dragon'", abilities: Abilities("\"appliesTo\": [\"rook\"]", "\"appliesTo\": [\"dragon\"]"));
    }
}

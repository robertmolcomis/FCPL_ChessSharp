using System.Linq;
using NUnit.Framework;

namespace Chess.Core.Tests
{
    /// <summary>pieces.json / abilities.json / conditions.json loading and validation. These tests never install the data they load.</summary>
    public class RulesDataTests
    {
        static RulesData Load(string pieces = null, string abilities = null, string conditions = null) =>
            RulesDataLoader.Load(pieces ?? TestData.PiecesJson, abilities ?? TestData.AbilitiesJson, conditions ?? TestData.ConditionsJson);

        static void AssertError(string expectedPart, string pieces = null, string abilities = null, string conditions = null)
        {
            var e = Assert.Throws<RulesDataException>(() => Load(pieces, abilities, conditions));
            StringAssert.Contains(expectedPart, e.Message);
        }

        static string Conditions(string from, string to)
        {
            var s = TestData.ConditionsJson;
            Assert.IsTrue(s.Contains(from), $"test setup: conditions.json no longer contains {from}");
            return s.Replace(from, to);
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
                new[] { "pawn", "knight", "bishop", "rook", "queen", "king", "archbishop", "chancellor", "paladin", "wall" });
            CollectionAssert.AreEqual(new[] { 'Q', 'R', 'B', 'N' }, d.PromotionChoices.ToArray());
            Assert.IsTrue(d.PiecesByName["king"].IsRoyal);
            Assert.IsTrue(d.PiecesByName["pawn"].PromotesOnLastRank);
            Assert.IsTrue(d.PiecesByName["pawn"].Has(AbilityIds.BerserkPawn));
            Assert.AreEqual(AbilityCategory.PowerUp, d.Abilities["BerserkPawn"].Category);
            Assert.AreEqual(AbilityCategory.Rule, d.Abilities["Castles"].Category);
        }

        [Test]
        public void RawJsonIsKeptForSaveSnapshots()
        {
            var d = Load();
            Assert.AreEqual(TestData.PiecesJson, d.PiecesJson);
            Assert.AreEqual(TestData.AbilitiesJson, d.AbilitiesJson);
            Assert.AreEqual(TestData.ConditionsJson, d.ConditionsJson);
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
            var d = Load();
            var pawn = d.PiecesByName["pawn"];
            Assert.AreSame(d.Conditions["captureTwoPieces"], pawn.Get<BerserkPawnAbility>().Condition, "when = the condition");

            // "replace": berserk capture set = exactly the ability's pattern; pushes are kept.
            var caps = pawn.ModifiedMoves.Where(m => (m.Flags & MoveFlags.CaptureOnly) != 0).Select(m => (m.Dx, m.Dy)).ToArray();
            CollectionAssert.AreEquivalent(new[] { (-1, 1), (0, 1), (1, 1), (0, -1) }, caps);
            Assert.AreEqual(2, pawn.ModifiedMoves.Count(m => (m.Flags & MoveFlags.MoveOnly) != 0));
        }

        [Test]
        public void BerserkAddModeKeepsDiagonals()
        {
            var pawn = Load(abilities: Abilities("\"mode\": \"replace\"", "\"mode\": \"add\"")).PiecesByName["pawn"];
            // base diagonals (2) + the ability's 4 (diagonals listed again) = 6 capture primitives
            Assert.AreEqual(6, pawn.ModifiedMoves.Count(m => (m.Flags & MoveFlags.CaptureOnly) != 0));
        }

        [Test]
        public void EachAbilityIsBackedByItsClass()
        {
            var d = Load();
            Assert.IsInstanceOf<CastlesAbility>(d.Abilities[AbilityIds.Castles].Behaviour);
            Assert.IsInstanceOf<PromotesAbility>(d.Abilities[AbilityIds.Promotes].Behaviour);
            Assert.IsInstanceOf<BerserkPawnAbility>(d.Abilities[AbilityIds.BerserkPawn].Behaviour);
            // parameters are global: every piece shares the same instance
            Assert.AreSame(d.Abilities[AbilityIds.BerserkPawn].Behaviour, d.PiecesByName["pawn"].Get<BerserkPawnAbility>());
        }

        [Test]
        public void PromotionChoicesComeFromPromotesParams()
        {
            var d = Load(abilities: Abilities("\"choices\": [\"queen\", \"rook\", \"bishop\", \"knight\"]", "\"choices\": [\"knight\", \"archbishop\"]"));
            CollectionAssert.AreEqual(new[] { 'N', 'A' }, d.PiecesByName["pawn"].Promotes.Choices.ToArray());
        }

        // ───────── Classic: rules only ─────────

        [Test]
        public void ClassicLeavesOutPowerUps()
        {
            var d = RulesDataLoader.Load(TestData.PiecesJson, TestData.AbilitiesJson, TestData.ConditionsJson, includePowerUps: false);
            var pawn = d.PiecesByName["pawn"];
            Assert.IsFalse(pawn.Has(AbilityIds.BerserkPawn), "power-up removed");
            Assert.IsTrue(pawn.Has(AbilityIds.CreatesEnPassant), "rules kept");
            Assert.IsTrue(pawn.PromotesOnLastRank);
            Assert.IsTrue(d.PiecesByName["king"].Has(AbilityIds.Castles));
            Assert.IsTrue(d.Abilities.ContainsKey(AbilityIds.BerserkPawn), "still defined (shop/tooltips), just not given to pieces");
        }

        [Test]
        public void ClassicPawnNeverGoesBerserk()
        {
            try
            {
                TestData.Install(includePowerUps: false);
                var g = new Game("4k3/8/8/2p5/3p4/4P3/8/4K3 w - - 0 1");
                foreach (var m in new[] { ("e3", "d4"), ("e8", "e7"), ("d4", "c5") })
                    Assert.IsNotNull(g.TryMakeMove(new Move(Sq(m.Item1), Sq(m.Item2))));
                Assert.AreEqual(2, g.Board[Sq("c5")].CaptureCount);
                Assert.IsFalse(g.Board[Sq("c5")].IsBerserk);
            }
            finally
            {
                TestData.Install(); // restore for the other tests
            }
        }

        static Square Sq(string s) => new Square(s[0] - 'a', s[1] - '1');

        // ───────── validation errors ─────────

        [Test] public void InvalidJson() => AssertError("invalid JSON", pieces: "{ not json");
        [Test] public void WrongVersion() => AssertError("version 2", pieces: Pieces("\"version\": 1", "\"version\": 2"));

        [Test] public void UnknownFlag() =>
            AssertError("flag 'Flying' is not defined", pieces: Pieces("\"flags\": [\"CastlePartner\"]", "\"flags\": [\"Flying\"]"));

        [Test] public void AbilityNotAllowedOnPiece() =>
            AssertError("can't be given to 'rook'", pieces: Pieces("\"flags\": [\"CastlePartner\"]", "\"flags\": [\"CastlePartner\", \"BerserkPawn\"]"));

        [Test] public void AbilityWithoutEngineBehaviour() =>
            AssertError("no behaviour", abilities: Abilities("\"id\": \"CastlePartner\"", "\"id\": \"Teleport\""));

        [Test] public void OldBerserkThresholdPointsToConditions() =>
            AssertError("moved to conditions.json", abilities: Abilities("\"mode\": \"replace\"", "\"threshold\": 2, \"mode\": \"replace\""));

        [Test] public void UnknownPromotionChoice() =>
            AssertError("unknown piece 'dragon'", abilities: Abilities("\"choices\": [\"queen\"", "\"choices\": [\"dragon\""));

        [Test] public void DuplicateSymbol() =>
            AssertError("already used", pieces: Pieces("\"symbol\": \"C\"", "\"symbol\": \"A\""));

        [Test] public void ZeroDirection() =>
            AssertError("[0, 0]", pieces: Pieces("{ \"dirs\": [[0, 1]] },", "{ \"dirs\": [[0, 0]] },"));

        [Test] public void BadCapturesValue() =>
            AssertError("\"same\"", pieces: Pieces("\"captures\": \"same\"", "\"captures\": \"foo\""));

        [Test] public void BadBerserkMode() =>
            AssertError("mode", abilities: Abilities("\"mode\": \"replace\"", "\"mode\": \"double\""));

        [Test] public void PromotionToKingNotAllowed() =>
            AssertError("can't promote to 'king'", abilities: Abilities("\"choices\": [\"queen\"", "\"choices\": [\"king\""));

        [Test] public void AppliesToUnknownPiece() =>
            AssertError("unknown piece 'dragon'", abilities: Abilities("\"appliesTo\": [\"rook\"]", "\"appliesTo\": [\"dragon\"]"));
    }
}

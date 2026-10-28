using System.Linq;
using NUnit.Framework;

namespace Chess.Core.Tests
{
    /// <summary>conditions.json: captures by piece name, move streaks, permanent conditions, TransformsInto, validation.</summary>
    public class ConditionTests
    {
        static Square S(string s) => new Square(s[0] - 'a', s[1] - '1');

        static void Play(Game g, params string[] moves)
        {
            foreach (var m in moves)
                Assert.IsNotNull(g.TryMakeMove(new Move(S(m.Substring(0, 2)), S(m.Substring(2, 2)), m.Length > 4 ? char.ToUpper(m[4]) : '\0')), $"illegal: {m}");
        }

        /// <summary>Installs edited data for one test, then puts the real files back.</summary>
        static void WithData(string abilities, string conditions, System.Action test)
        {
            try
            {
                PieceLibrary.LoadFromJson(TestData.PiecesJson, abilities ?? TestData.AbilitiesJson, conditions ?? TestData.ConditionsJson);
                test();
            }
            finally { TestData.Install(); }
        }

        static string Replace(string json, string from, string to)
        {
            Assert.IsTrue(json.Contains(from), $"test setup: data no longer contains {from}");
            return json.Replace(from, to);
        }

        static RulesDataException LoadError(string pieces = null, string abilities = null, string conditions = null) =>
            Assert.Throws<RulesDataException>(() => RulesDataLoader.Load(pieces ?? TestData.PiecesJson,
                abilities ?? TestData.AbilitiesJson, conditions ?? TestData.ConditionsJson));

        // ───────── captures by name ─────────

        [Test]
        public void CapturesAreCountedByPieceName()
        {
            var g = new Game("4k3/8/8/1b6/8/2p5/8/Q3K3 w - - 0 1");
            Play(g, "a1c3", "e8d8", "c3b4", "d8e8", "b4b5");
            var q = g.Board[S("b5")];
            Assert.AreEqual(1, q.CapturesOf("pawn"));
            Assert.AreEqual(1, q.CapturesOf("bishop"));
            Assert.AreEqual(0, q.CapturesOf("knight"));
            Assert.AreEqual(2, q.CaptureCount);

            Assert.IsTrue(g.Undo());
            Assert.AreEqual(0, g.Board[S("b4")].CapturesOf("bishop"), "undo removes the capture");
            Assert.AreEqual(1, g.Board[S("b4")].CapturesOf("pawn"));
        }

        [Test]
        public void BrokenWallCountsAsWallButNotAsKill()
        {
            var g = new Game("4k3/8/8/8/8/2w5/8/2R1K3 w - - 0 1");
            Play(g, "c1c3");
            var rook = g.Board[S("c3")];
            Assert.AreEqual(1, rook.CapturesOf("wall"));
            Assert.AreEqual(0, rook.CaptureCount);
        }

        [Test]
        public void OfAndExceptFilter()
        {
            var g = new Game("4k3/8/8/1b6/8/2p5/8/Q3K3 w - - 0 1");
            Play(g, "a1c3", "e8d8", "c3b4", "d8e8", "b4b5");
            var q = g.Board[S("b5")];
            Assert.AreEqual(2, q.CapturesMatching(null, null));
            Assert.AreEqual(1, q.CapturesMatching(new[] { "bishop", "knight" }, null));
            Assert.AreEqual(1, q.CapturesMatching(null, new[] { "pawn" }));
        }

        // ───────── knight → paladin ─────────

        const string KnightVsKnight = "4k3/8/8/8/3n4/8/4N3/4K3 w - - 0 1";

        [Test]
        public void KnightThatTakesAKnightBecomesPaladin()
        {
            var g = new Game(KnightVsKnight);
            var knight = g.Board[S("e2")];
            var rec = g.TryMakeMove(new Move(S("e2"), S("d4")));
            Assert.IsNotNull(rec);
            Assert.AreSame(knight, g.Board[S("d4")], "same piece, same id");
            Assert.AreEqual("paladin", knight.Definition.Id);
            Assert.IsTrue(rec.Promoted, "the view redraws the mover");
            Assert.AreEqual(1, rec.Transformations.Count);
            Assert.AreEqual("knight", rec.Transformations[0].before.Id);
            Assert.AreEqual(1, knight.CapturesOf("knight"), "history carries over");
        }

        [Test]
        public void UndoTurnsPaladinBackIntoKnight()
        {
            var g = new Game(KnightVsKnight);
            ulong before = g.Hash;
            Play(g, "e2d4");
            Assert.IsTrue(g.Undo());
            Assert.AreEqual("knight", g.Board[S("e2")].Definition.Id);
            Assert.AreEqual(before, g.Hash);
        }

        [Test]
        public void KnightTakingSomethingElseStaysKnight()
        {
            var g = new Game("4k3/8/8/8/3b4/8/4N3/4K3 w - - 0 1");
            Play(g, "e2d4");
            Assert.AreEqual("knight", g.Board[S("d4")].Definition.Id);
        }

        [Test]
        public void ClassicHasNoPaladins()
        {
            try
            {
                TestData.Install(includePowerUps: false);
                var g = new Game(KnightVsKnight);
                Play(g, "e2d4");
                Assert.AreEqual("knight", g.Board[S("d4")].Definition.Id);
            }
            finally { TestData.Install(); }
        }

        [Test]
        public void PaladinsAppearInFen()
        {
            var g = new Game(KnightVsKnight);
            Play(g, "e2d4");
            StringAssert.StartsWith("4k3/8/8/8/3D4/8/8/4K3 b", g.ToFen());
        }

        // ───────── move streak ─────────

        [Test]
        public void StreakCountsOwnConsecutiveMovesOnly()
        {
            var g = new Game("4k3/8/8/8/8/8/8/R3K3 w - - 0 1");
            var rook = g.Board[S("a1")];
            Play(g, "a1a2", "e8d8", "a2a3", "d8e8", "a3a4");
            Assert.AreEqual(3, rook.MoveStreak, "the opponent's moves in between don't break it");
            Assert.IsTrue(PieceLibrary.Conditions["onARoll"].Evaluate(rook));

            Play(g, "e8d8", "e1e2");
            Assert.AreEqual(0, rook.MoveStreak, "moving another piece ends it");
            Assert.AreEqual(1, g.Board[S("e2")].MoveStreak);
            Assert.IsFalse(PieceLibrary.Conditions["onARoll"].Evaluate(rook));

            Assert.IsTrue(g.Undo());
            Assert.AreEqual(3, rook.MoveStreak, "undo restores it");
        }

        [Test]
        public void CastlingCountsForTheKingOnly()
        {
            var g = new Game("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1");
            Play(g, "e1g1");
            Assert.AreEqual(1, g.Board[S("g1")].MoveStreak);
            Assert.AreEqual(0, g.Board[S("f1")].MoveStreak);
        }

        // ───────── permanent ─────────

        const string LonePawn = "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1";

        static void BerserkOnARoll(bool permanent) => WithData(
            Replace(TestData.AbilitiesJson, "\"condition\": \"captureTwoPieces\"", "\"condition\": \"onARoll\""),
            Replace(TestData.ConditionsJson, "\"permanent\": false", $"\"permanent\": {(permanent ? "true" : "false")}"),
            () =>
            {
                var g = new Game(LonePawn);
                var pawn = g.Board[S("e2")];
                Play(g, "e2e3", "e8d8", "e3e4", "d8e8", "e4e5");
                Assert.IsTrue(pawn.IsBerserk, "3 moves in a row");

                Play(g, "e8d8", "e1e2");
                Assert.AreEqual(permanent, pawn.IsBerserk, permanent ? "permanent: stays on" : "live: off once the streak ends");

                Assert.IsTrue(g.Undo());
                Assert.IsTrue(g.Undo());
                Assert.IsTrue(g.Undo());
                Assert.IsFalse(pawn.IsBerserk, "undo before it was reached removes it");
                Assert.IsFalse(pawn.HasLatched(PieceLibrary.Conditions["onARoll"]));
            });

        [Test] public void PermanentConditionStaysTrue() => BerserkOnARoll(permanent: true);
        [Test] public void LiveConditionTurnsOff() => BerserkOnARoll(permanent: false);

        // ───────── combinators / inline ─────────

        [Test]
        public void AllAnyNotAndReferences()
        {
            var conditions = Replace(TestData.ConditionsJson, "\"conditions\": [", @"""conditions"": [
    { ""id"": ""movedTwice"", ""stat"": ""moves"", ""op"": "">="", ""value"": 2 },
    { ""id"": ""both"", ""all"": [ ""movedTwice"", { ""not"": ""neverMoved"" } ] },
    { ""id"": ""either"", ""any"": [ ""slewAKnight"", ""movedTwice"" ] },");
            WithData(null, conditions, () =>
            {
                var c = PieceLibrary.Conditions;
                var g = new Game("4k3/8/8/8/8/8/8/R3K3 w - - 0 1");
                var rook = g.Board[S("a1")];
                Assert.IsFalse(c["both"].Evaluate(rook));
                Assert.IsFalse(c["either"].Evaluate(rook));
                Play(g, "a1a2", "e8d8", "a2a3");
                Assert.IsTrue(c["both"].Evaluate(rook));
                Assert.IsTrue(c["either"].Evaluate(rook));
            });
        }

        [Test]
        public void AbilityCanUseAnInlineCondition()
        {
            var abilities = Replace(TestData.AbilitiesJson, "\"condition\": \"captureTwoPieces\"",
                "\"condition\": { \"stat\": \"captures\", \"op\": \">=\", \"value\": 1 }");
            WithData(abilities, null, () =>
            {
                var g = new Game("4k3/8/8/8/3p4/4P3/8/4K3 w - - 0 1");
                Play(g, "e3d4");
                Assert.IsTrue(g.Board[S("d4")].IsBerserk);
            });
        }

        // ───────── validation ─────────

        static void AssertLoadError(string expected, string pieces = null, string abilities = null, string conditions = null) =>
            StringAssert.Contains(expected, LoadError(pieces, abilities, conditions).Message);

        [Test] public void UnknownConditionInAbility() =>
            AssertLoadError("unknown condition 'captureTenPieces'",
                abilities: Replace(TestData.AbilitiesJson, "\"condition\": \"captureTwoPieces\"", "\"condition\": \"captureTenPieces\""));

        [Test] public void UnknownConditionInPattern() =>
            AssertLoadError("unknown condition 'firstMove'",
                pieces: Replace(TestData.PiecesJson, "\"condition\": \"neverMoved\"", "\"condition\": \"firstMove\""));

        [Test] public void UnknownStat() =>
            AssertLoadError("unknown stat 'kills'",
                conditions: Replace(TestData.ConditionsJson, "\"stat\": \"moveStreak\"", "\"stat\": \"kills\""));

        [Test] public void BadOperator() =>
            AssertLoadError("op must be one of",
                conditions: Replace(TestData.ConditionsJson, "\"op\": \"==\"", "\"op\": \"=\""));

        [Test] public void MisspelledPieceName() =>
            AssertLoadError("unknown piece 'knigth'",
                conditions: Replace(TestData.ConditionsJson, "\"of\": \"knight\"", "\"of\": \"knigth\""));

        [Test] public void OfOnAStatThatDoesntCountPieces() =>
            AssertLoadError("can't use \"of\"",
                conditions: Replace(TestData.ConditionsJson, "\"stat\": \"moves\",", "\"stat\": \"moves\", \"of\": \"pawn\","));

        [Test] public void TwoShapesInOneCondition() =>
            AssertLoadError("exactly one of",
                conditions: Replace(TestData.ConditionsJson, "\"stat\": \"moves\",", "\"stat\": \"moves\", \"not\": \"onARoll\","));

        [Test] public void ConditionThatRefersToItself() =>
            AssertLoadError("refers to itself",
                conditions: Replace(TestData.ConditionsJson, "\"stat\": \"moveStreak\", \"op\": \">=\", \"value\": 3,", "\"all\": [\"onARoll\"],"));

        [Test] public void DuplicateConditionId() =>
            AssertLoadError("duplicate id",
                conditions: Replace(TestData.ConditionsJson, "\"id\": \"onARoll\"", "\"id\": \"neverMoved\""));

        [Test] public void TypoInConditionField() =>
            AssertLoadError("unknown field 'vaule'",
                conditions: Replace(TestData.ConditionsJson, "\"value\": 3", "\"vaule\": 3"));

        [Test] public void PermanentOnlyOnNamedConditions() =>
            AssertLoadError("only allowed on named conditions",
                abilities: Replace(TestData.AbilitiesJson, "\"condition\": \"captureTwoPieces\"",
                    "\"condition\": { \"stat\": \"moves\", \"op\": \">\", \"value\": 1, \"permanent\": true }"));

        [Test] public void TransformIntoUnknownPiece() =>
            AssertLoadError("unknown piece 'dragon'",
                abilities: Replace(TestData.AbilitiesJson, "\"into\": \"paladin\"", "\"into\": \"dragon\""));

        [Test] public void TransformNeedsACondition() =>
            AssertLoadError("needs a \"condition\"",
                abilities: Replace(TestData.AbilitiesJson, "\"condition\": \"slewAKnight\",", ""));

        [Test] public void UnknownAbilityType() =>
            AssertLoadError("unknown type 'Teleports'",
                abilities: Replace(TestData.AbilitiesJson, "\"type\": \"TransformsInto\"", "\"type\": \"Teleports\""));

        [Test]
        public void NewUpgradePathIsDataOnly()
        {
            // A second TransformsInto under a new id — no C#: a bishop that captures 2 pawns becomes an archbishop.
            var abilities = Replace(TestData.AbilitiesJson, "\"params\": { \"into\": \"paladin\" }\n    }",
                "\"params\": { \"into\": \"paladin\" }\n    },\n" +
                "    { \"id\": \"BishopAscends\", \"category\": \"powerup\", \"appliesTo\": [\"bishop\"], \"type\": \"TransformsInto\",\n" +
                "      \"condition\": { \"stat\": \"captures\", \"of\": \"pawn\", \"op\": \">=\", \"value\": 2 }, \"params\": { \"into\": \"archbishop\" } }");
            var pieces = Replace(TestData.PiecesJson,
                "\"name\": \"bishop\", \"displayName\": \"Bishop\", \"symbol\": \"B\",",
                "\"name\": \"bishop\", \"displayName\": \"Bishop\", \"symbol\": \"B\", \"flags\": [\"BishopAscends\"],");
            pieces = Replace(pieces, "\"captures\": \"same\",\n      \"flags\": []\n    },\n    {\n      \"name\": \"rook\"",
                "\"captures\": \"same\"\n    },\n    {\n      \"name\": \"rook\"");
            try
            {
                PieceLibrary.LoadFromJson(pieces, abilities, TestData.ConditionsJson);
                var g = new Game("4k3/8/8/8/8/2p5/1p6/B3K3 w - - 0 1");
                Play(g, "a1b2", "e8d8", "b2c3");
                Assert.AreEqual("archbishop", g.Board[S("c3")].Definition.Id);
            }
            finally { TestData.Install(); }
        }
    }
}

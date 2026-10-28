using System.IO;
using NUnit.Framework;

namespace Chess.Core.Tests
{
    /// <summary>The game's real data files (EditMode tests run with the project folder as working directory).</summary>
    public static class TestData
    {
        public const string Folder = "Assets/StreamingAssets/Data";
        public static string PiecesJson => File.ReadAllText(Path.Combine(Folder, "pieces.json"));
        public static string AbilitiesJson => File.ReadAllText(Path.Combine(Folder, "abilities.json"));
        public static string ConditionsJson => File.ReadAllText(Path.Combine(Folder, "conditions.json"));

        /// <summary>Installs the real data (optionally without power-ups).</summary>
        public static void Install(bool includePowerUps = true) =>
            PieceLibrary.LoadFromJson(PiecesJson, AbilitiesJson, ConditionsJson, includePowerUps);
    }

    /// <summary>Installs the real piece data once before any test in this namespace runs.</summary>
    [SetUpFixture]
    public class RulesDataSetup
    {
        [OneTimeSetUp]
        public void LoadPieceData() => TestData.Install();
    }
}

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
    }

    /// <summary>Installs the real piece data once before any test in this namespace runs.</summary>
    [SetUpFixture]
    public class RulesDataSetup
    {
        [OneTimeSetUp]
        public void LoadPieceData() => PieceLibrary.LoadFromJson(TestData.PiecesJson, TestData.AbilitiesJson);
    }
}

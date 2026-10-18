using System;
using System.IO;
using Chess.Core;
using UnityEngine;

namespace Chess.Unity
{
    /// <summary>
    /// Loads pieces.json + abilities.json from StreamingAssets/Data into the engine at startup.
    /// Files are re-read every time Load is called, so edits apply on the next Play without a domain reload.
    /// </summary>
    public static class RulesBootstrap
    {
        public const string DataFolder = "Data";
        public const string PiecesFile = "pieces.json";
        public const string AbilitiesFile = "abilities.json";

        public static string DataPath => Path.Combine(Application.streamingAssetsPath, DataFolder);

        /// <summary>Returns false (and logs the reason) if the files are missing or invalid.</summary>
        public static bool Load()
        {
            string piecesPath = Path.Combine(DataPath, PiecesFile);
            string abilitiesPath = Path.Combine(DataPath, AbilitiesFile);
            try
            {
                var data = PieceLibrary.LoadFromJson(File.ReadAllText(piecesPath), File.ReadAllText(abilitiesPath));
                Debug.Log($"[Chess] Loaded {data.PiecesByName.Count} pieces and {data.Abilities.Count} abilities from {DataPath}");
                return true;
            }
            catch (RulesDataException e)
            {
                Debug.LogError($"[Chess] Invalid piece data: {e.Message}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[Chess] Couldn't read piece data from {DataPath}: {e.Message}");
            }
            return false;
        }
    }
}

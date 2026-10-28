using System;
using System.IO;
using Chess.Core;
using UnityEngine;

namespace Chess.Unity
{
    /// <summary>
    /// Loads pieces.json + abilities.json + conditions.json from StreamingAssets/Data into the engine at startup.
    /// Files are re-read every time Load is called, so edits apply on the next Play without a domain reload.
    /// </summary>
    public static class RulesBootstrap
    {
        public const string DataFolder = "Data";
        public const string PiecesFile = "pieces.json";
        public const string AbilitiesFile = "abilities.json";
        public const string ConditionsFile = "conditions.json";

        public static string DataPath => Path.Combine(Application.streamingAssetsPath, DataFolder);

        /// <summary>
        /// Returns false (and logs the reason) if the files are missing or invalid.
        /// <paramref name="includePowerUps"/> = false loads only "rule" abilities (Classic).
        /// </summary>
        public static bool Load(bool includePowerUps = true)
        {
            string piecesPath = Path.Combine(DataPath, PiecesFile);
            string abilitiesPath = Path.Combine(DataPath, AbilitiesFile);
            string conditionsPath = Path.Combine(DataPath, ConditionsFile);
            try
            {
                var data = PieceLibrary.LoadFromJson(File.ReadAllText(piecesPath), File.ReadAllText(abilitiesPath),
                                                     File.ReadAllText(conditionsPath), includePowerUps);
                Debug.Log($"[Chess] Loaded {data.PiecesByName.Count} pieces, {data.Abilities.Count} abilities and " +
                          $"{data.Conditions.Count} conditions from {DataPath}" +
                          (includePowerUps ? "" : " (Classic: power-ups off)"));
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

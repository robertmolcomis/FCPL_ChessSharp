using System;
using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>
    /// The active set of piece and ability definitions, loaded at startup from pieces.json + abilities.json.
    /// Read-only once installed. Install with <see cref="LoadFromJson"/> (or <see cref="Install"/>) before creating a Game.
    /// </summary>
    public static class PieceLibrary
    {
        static RulesData _data;

        public static bool IsLoaded => _data != null;

        /// <summary>The installed rules (also holds the raw JSON, for save-file snapshots).</summary>
        public static RulesData Data => _data ?? throw NotLoaded();

        /// <summary>Parses, validates and installs the rules. Throws <see cref="RulesDataException"/> on bad data.</summary>
        public static RulesData LoadFromJson(string piecesJson, string abilitiesJson)
        {
            var data = RulesDataLoader.Load(piecesJson, abilitiesJson);
            Install(data);
            return data;
        }

        public static void Install(RulesData data) => _data = data ?? throw new ArgumentNullException(nameof(data));

        /// <summary>Piece type by its pieces.json name, or null if there is none.</summary>
        public static PieceDefinition Get(string name) =>
            Data.PiecesByName.TryGetValue(name, out var def) ? def : null;

        public static IReadOnlyDictionary<char, PieceDefinition> BySymbol => Data.PiecesBySymbol;
        public static IReadOnlyDictionary<string, PieceDefinition> ByName => Data.PiecesByName;
        public static IReadOnlyDictionary<string, AbilityInfo> Abilities => Data.Abilities;

        /// <summary>What a promoting piece may become, in picker order (from the "Promotes" ability's params).</summary>
        public static IReadOnlyList<char> PromotionChoices => Data.PromotionChoices;

        // Shortcuts for the standard set (null if the data doesn't define them).
        public static PieceDefinition Pawn => Get("pawn");
        public static PieceDefinition Knight => Get("knight");
        public static PieceDefinition Bishop => Get("bishop");
        public static PieceDefinition Rook => Get("rook");
        public static PieceDefinition Queen => Get("queen");
        public static PieceDefinition King => Get("king");
        public static PieceDefinition Archbishop => Get("archbishop");
        public static PieceDefinition Chancellor => Get("chancellor");
        public static PieceDefinition Wall => Get("wall");

        static InvalidOperationException NotLoaded() => new InvalidOperationException(
            "Piece data not loaded. Call PieceLibrary.LoadFromJson(pieces.json, abilities.json) at startup.");
    }
}

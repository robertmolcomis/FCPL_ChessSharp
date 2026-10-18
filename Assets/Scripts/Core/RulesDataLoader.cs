using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chess.Core
{
    /// <summary>Thrown when pieces.json / abilities.json are malformed or inconsistent. The message says what and where.</summary>
    public sealed class RulesDataException : Exception
    {
        public RulesDataException(string message) : base(message) { }
    }

    /// <summary>One entry of abilities.json.</summary>
    public sealed class AbilityInfo
    {
        public string Id { get; internal set; }
        /// <summary>"rule" (standard chess rule) or "powerup".</summary>
        public string Category { get; internal set; }
        public IReadOnlyList<string> AppliesTo { get; internal set; }
        public string DisplayName { get; internal set; }
        public string Description { get; internal set; }
        /// <summary>Raw parameters; read by the ability's C# behaviour.</summary>
        internal JObject Params { get; set; }

        public bool IsPowerUp => Category == AbilityCategory.PowerUp;
    }

    public static class AbilityCategory
    {
        public const string Rule = "rule";
        public const string PowerUp = "powerup";
    }

    /// <summary>Everything loaded from the two JSON files. Install it with <see cref="PieceLibrary.Install"/>.</summary>
    public sealed class RulesData
    {
        public IReadOnlyDictionary<string, PieceDefinition> PiecesByName { get; internal set; }
        public IReadOnlyDictionary<char, PieceDefinition> PiecesBySymbol { get; internal set; }
        public IReadOnlyDictionary<string, AbilityInfo> Abilities { get; internal set; }
        /// <summary>Symbols a promoting piece may choose from, in picker order.</summary>
        public IReadOnlyList<char> PromotionChoices { get; internal set; }
        /// <summary>The source text, kept so save files can snapshot the exact rules a match was played with.</summary>
        public string PiecesJson { get; internal set; }
        public string AbilitiesJson { get; internal set; }
    }

    /// <summary>
    /// Parses and validates pieces.json + abilities.json (format: FEATURES_PLAN.md §2).
    /// Pure C#: callers pass the file contents, so it runs in Unity, tests and headless tools alike.
    /// </summary>
    public static class RulesDataLoader
    {
        public const int SupportedVersion = 1;

        /// <summary>Ability ids the engine knows how to run. A flag/ability outside this list is an error.</summary>
        static readonly Dictionary<string, PieceAbilities> EngineAbilities = new Dictionary<string, PieceAbilities>
        {
            ["Castles"] = PieceAbilities.Castles,
            ["CastlePartner"] = PieceAbilities.CastlePartner,
            ["CreatesEnPassant"] = PieceAbilities.CreatesEnPassant,
            ["CapturesEnPassant"] = PieceAbilities.CapturesEnPassant,
            ["ResetsFiftyMoveClock"] = PieceAbilities.ResetsFiftyMoveClock,
            ["Wall"] = PieceAbilities.Wall,
            ["BerserkPawn"] = PieceAbilities.Berserks,
            ["Promotes"] = PieceAbilities.None, // handled through PieceDefinition.PromotesOnLastRank
        };

        public static RulesData Load(string piecesJson, string abilitiesJson)
        {
            var abilitiesRoot = ParseRoot(abilitiesJson, "abilities.json");
            var piecesRoot = ParseRoot(piecesJson, "pieces.json");

            var abilities = ReadAbilities(abilitiesRoot);
            var rawPieces = ReadPieceEntries(piecesRoot);

            // appliesTo must name real pieces
            var pieceNames = new HashSet<string>();
            foreach (var p in rawPieces) pieceNames.Add(p.Name);
            foreach (var a in abilities.Values)
                foreach (var target in a.AppliesTo)
                    if (!pieceNames.Contains(target))
                        throw Error("abilities.json", $"ability '{a.Id}'", $"appliesTo names unknown piece '{target}'");

            var byName = new Dictionary<string, PieceDefinition>();
            var bySymbol = new Dictionary<char, PieceDefinition>();
            foreach (var raw in rawPieces)
            {
                var def = BuildPiece(raw, abilities);
                byName[def.Id] = def;
                bySymbol[def.Symbol] = def;
            }

            var promotionChoices = ReadPromotionChoices(abilities, byName);

            return new RulesData
            {
                PiecesByName = byName,
                PiecesBySymbol = bySymbol,
                Abilities = abilities,
                PromotionChoices = promotionChoices,
                PiecesJson = piecesJson,
                AbilitiesJson = abilitiesJson,
            };
        }

        // ───────────────────────── abilities.json ─────────────────────────

        static Dictionary<string, AbilityInfo> ReadAbilities(JObject root)
        {
            const string file = "abilities.json";
            var result = new Dictionary<string, AbilityInfo>();
            var list = RequireArray(root, "abilities", file, "root");

            for (int i = 0; i < list.Count; i++)
            {
                string where = $"abilities[{i}]";
                if (!(list[i] is JObject o)) throw Error(file, where, "must be an object");

                string id = RequireString(o, "id", file, where);
                where = $"ability '{id}'";
                if (result.ContainsKey(id)) throw Error(file, where, "duplicate id");
                if (!EngineAbilities.ContainsKey(id))
                    throw Error(file, where, "the engine has no behaviour for this id (known: " + string.Join(", ", EngineAbilities.Keys) + ")");

                string category = RequireString(o, "category", file, where);
                if (category != AbilityCategory.Rule && category != AbilityCategory.PowerUp)
                    throw Error(file, where, $"category must be \"{AbilityCategory.Rule}\" or \"{AbilityCategory.PowerUp}\", got \"{category}\"");

                var appliesTo = new List<string>();
                foreach (var t in RequireArray(o, "appliesTo", file, where))
                {
                    if (t.Type != JTokenType.String) throw Error(file, where, "appliesTo must contain piece names (strings)");
                    appliesTo.Add((string)t);
                }
                if (appliesTo.Count == 0) throw Error(file, where, "appliesTo must list at least one piece");

                var prms = o["params"];
                if (prms != null && prms.Type != JTokenType.Object) throw Error(file, where, "params must be an object");

                result[id] = new AbilityInfo
                {
                    Id = id,
                    Category = category,
                    AppliesTo = appliesTo,
                    DisplayName = OptionalString(o, "displayName", file, where) ?? id,
                    Description = OptionalString(o, "description", file, where) ?? "",
                    Params = (JObject)prms ?? new JObject(),
                };
            }

            ValidateParams(result);
            return result;
        }

        /// <summary>Per-ability parameter checks (types, ranges).</summary>
        static void ValidateParams(Dictionary<string, AbilityInfo> abilities)
        {
            const string file = "abilities.json";
            if (abilities.TryGetValue("BerserkPawn", out var b))
            {
                string where = "ability 'BerserkPawn' params";
                int threshold = RequireInt(b.Params, "threshold", file, where);
                if (threshold < 1) throw Error(file, where, "threshold must be >= 1");
                string mode = RequireString(b.Params, "mode", file, where);
                if (mode != "replace" && mode != "add") throw Error(file, where, "mode must be \"replace\" or \"add\"");
                ReadPatterns(b.Params["captures"], MoveFlags.CaptureOnly, file, where + ".captures", required: true);
            }
            if (abilities.TryGetValue("Promotes", out var p))
            {
                string where = "ability 'Promotes' params";
                var choices = RequireArray(p.Params, "choices", file, where);
                if (choices.Count == 0) throw Error(file, where, "choices must list at least one piece");
            }
        }

        static IReadOnlyList<char> ReadPromotionChoices(Dictionary<string, AbilityInfo> abilities, Dictionary<string, PieceDefinition> pieces)
        {
            var result = new List<char>();
            if (!abilities.TryGetValue("Promotes", out var p)) return result;
            const string file = "abilities.json", where = "ability 'Promotes' params.choices";
            foreach (var t in (JArray)p.Params["choices"])
            {
                if (t.Type != JTokenType.String) throw Error(file, where, "must contain piece names (strings)");
                string name = (string)t;
                if (!pieces.TryGetValue(name, out var def)) throw Error(file, where, $"unknown piece '{name}'");
                if (def.IsRoyal || def.Has(PieceAbilities.Wall)) throw Error(file, where, $"can't promote to '{name}'");
                if (!result.Contains(def.Symbol)) result.Add(def.Symbol);
            }
            return result;
        }

        // ───────────────────────── pieces.json ─────────────────────────

        sealed class RawPiece
        {
            public string Name, DisplayName;
            public char Symbol;
            public bool Royal;
            public JToken Moves, Captures;
            public List<string> Flags;
            public string Where;
        }

        static List<RawPiece> ReadPieceEntries(JObject root)
        {
            const string file = "pieces.json";
            var list = RequireArray(root, "pieces", file, "root");
            var result = new List<RawPiece>();
            var names = new HashSet<string>();
            var symbols = new Dictionary<char, string>();

            for (int i = 0; i < list.Count; i++)
            {
                string where = $"pieces[{i}]";
                if (!(list[i] is JObject o)) throw Error(file, where, "must be an object");

                string name = RequireString(o, "name", file, where);
                where = $"piece '{name}'";
                if (!names.Add(name)) throw Error(file, where, "duplicate name");

                string symbolText = RequireString(o, "symbol", file, where);
                if (symbolText.Length != 1 || !char.IsLetter(symbolText[0]))
                    throw Error(file, where, "symbol must be a single letter (used in FEN)");
                char symbol = char.ToUpperInvariant(symbolText[0]);
                if (symbols.TryGetValue(symbol, out var other))
                    throw Error(file, where, $"symbol '{symbol}' is already used by '{other}'");
                symbols[symbol] = name;

                var royalTok = o["royal"];
                if (royalTok != null && royalTok.Type != JTokenType.Boolean) throw Error(file, where, "royal must be true or false");

                var flags = new List<string>();
                var flagsTok = o["flags"];
                if (flagsTok != null)
                {
                    if (!(flagsTok is JArray fa)) throw Error(file, where, "flags must be an array of ability ids");
                    foreach (var f in fa)
                    {
                        if (f.Type != JTokenType.String) throw Error(file, where, "flags must contain ability ids (strings) only — parameters go in abilities.json");
                        if (flags.Contains((string)f)) throw Error(file, where, $"flag '{f}' listed twice");
                        flags.Add((string)f);
                    }
                }

                if (o["moves"] == null) throw Error(file, where, "missing 'moves'");
                if (o["captures"] == null) throw Error(file, where, "missing 'captures' (use \"same\" to reuse moves)");

                result.Add(new RawPiece
                {
                    Name = name,
                    DisplayName = OptionalString(o, "displayName", file, where) ?? name,
                    Symbol = symbol,
                    Royal = royalTok != null && (bool)royalTok,
                    Moves = o["moves"],
                    Captures = o["captures"],
                    Flags = flags,
                    Where = where,
                });
            }
            return result;
        }

        static PieceDefinition BuildPiece(RawPiece raw, Dictionary<string, AbilityInfo> abilities)
        {
            const string file = "pieces.json";
            string where = raw.Where;

            // Abilities: known, and allowed on this piece.
            var abilityFlags = PieceAbilities.None;
            bool promotes = false;
            AbilityInfo berserk = null;
            foreach (var id in raw.Flags)
            {
                if (!abilities.TryGetValue(id, out var info))
                    throw Error(file, where, $"flag '{id}' is not defined in abilities.json");
                if (!info.AppliesTo.Contains(raw.Name))
                    throw Error(file, where, $"ability '{id}' can't be given to '{raw.Name}' (appliesTo: {string.Join(", ", info.AppliesTo)})");
                abilityFlags |= EngineAbilities[id];
                if (id == "Promotes") promotes = true;
                if (id == "BerserkPawn") berserk = info;
            }

            // Movement: "moves" → move-only primitives, "captures" → capture-only, captures "same" → both.
            var prims = new List<MovePrimitive>();
            bool same = raw.Captures.Type == JTokenType.String;
            if (same && (string)raw.Captures != "same")
                throw Error(file, where, "captures must be a list of patterns or \"same\"");

            prims.AddRange(ReadPatterns(raw.Moves, same ? MoveFlags.None : MoveFlags.MoveOnly, file, where + ".moves", required: false));
            if (!same) prims.AddRange(ReadPatterns(raw.Captures, MoveFlags.CaptureOnly, file, where + ".captures", required: false));

            // Berserk: the capture set is replaced by (or extended with) the ability's pattern.
            List<MovePrimitive> berserkMoves = null;
            int threshold = 0;
            if (berserk != null)
            {
                threshold = (int)berserk.Params["threshold"];
                var extra = ReadPatterns(berserk.Params["captures"], MoveFlags.CaptureOnly, "abilities.json", "BerserkPawn.captures", required: true);
                berserkMoves = new List<MovePrimitive>();
                bool replace = (string)berserk.Params["mode"] == "replace";
                foreach (var m in prims)
                {
                    if (!replace) { berserkMoves.Add(m); continue; }
                    if ((m.Flags & MoveFlags.CaptureOnly) != 0) continue;                  // old capture pattern dropped
                    berserkMoves.Add(new MovePrimitive(m.Dx, m.Dy, m.MaxRange, m.Flags | MoveFlags.MoveOnly)); // keeps its moves
                }
                berserkMoves.AddRange(extra);
            }

            return new PieceDefinition(raw.Name, raw.DisplayName, raw.Symbol, prims,
                isRoyal: raw.Royal, promotesOnLastRank: promotes, abilities: abilityFlags,
                berserkMoves: berserkMoves, berserkCaptureThreshold: threshold, abilityIds: raw.Flags);
        }

        /// <summary>
        /// A pattern list: [{ "dirs": [[dx,dy],...], "slide": bool, "maxSteps": n, "condition": "firstMove" }, ...].
        /// Directions are from White's view. slide = unlimited range; otherwise maxSteps (default 1).
        /// </summary>
        static List<MovePrimitive> ReadPatterns(JToken token, MoveFlags baseFlags, string file, string where, bool required)
        {
            var result = new List<MovePrimitive>();
            if (token == null)
            {
                if (required) throw Error(file, where, "missing pattern list");
                return result;
            }
            if (!(token is JArray arr)) throw Error(file, where, "must be a list of patterns");
            if (required && arr.Count == 0) throw Error(file, where, "must contain at least one pattern");

            for (int i = 0; i < arr.Count; i++)
            {
                string w = $"{where}[{i}]";
                if (!(arr[i] is JObject o)) throw Error(file, w, "pattern must be an object");
                foreach (var prop in o.Properties())
                    if (prop.Name != "dirs" && prop.Name != "slide" && prop.Name != "maxSteps" && prop.Name != "condition")
                        throw Error(file, w, $"unknown field '{prop.Name}' (allowed: dirs, slide, maxSteps, condition)");

                var slideTok = o["slide"];
                if (slideTok != null && slideTok.Type != JTokenType.Boolean) throw Error(file, w, "slide must be true or false");
                bool slide = slideTok != null && (bool)slideTok;

                int range = 1;
                var stepsTok = o["maxSteps"];
                if (stepsTok != null)
                {
                    if (stepsTok.Type != JTokenType.Integer || (int)stepsTok < 1) throw Error(file, w, "maxSteps must be a whole number >= 1");
                    if (slide) throw Error(file, w, "use either slide or maxSteps, not both");
                    range = (int)stepsTok;
                }
                if (slide) range = 0; // engine: 0 = unlimited

                var flags = baseFlags;
                var condTok = o["condition"];
                if (condTok != null)
                {
                    if (condTok.Type != JTokenType.String || (string)condTok != "firstMove")
                        throw Error(file, w, "condition must be \"firstMove\"");
                    flags |= MoveFlags.FirstMoveOnly;
                }

                if (!(o["dirs"] is JArray dirs) || dirs.Count == 0) throw Error(file, w, "dirs must be a non-empty list of [dx, dy] pairs");
                foreach (var d in dirs)
                {
                    if (!(d is JArray pair) || pair.Count != 2 || pair[0].Type != JTokenType.Integer || pair[1].Type != JTokenType.Integer)
                        throw Error(file, w, $"bad direction {d.ToString(Formatting.None)} — expected [dx, dy] with whole numbers");
                    int dx = (int)pair[0], dy = (int)pair[1];
                    if (dx == 0 && dy == 0) throw Error(file, w, "direction [0, 0] is not a move");
                    result.Add(new MovePrimitive(dx, dy, range, flags));
                }
            }
            return result;
        }

        // ───────────────────────── helpers ─────────────────────────

        static JObject ParseRoot(string json, string file)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new RulesDataException($"{file}: file is empty or missing");
            JToken root;
            try { root = JToken.Parse(json); }
            catch (JsonReaderException e) { throw new RulesDataException($"{file}: invalid JSON — {e.Message}"); }
            if (!(root is JObject o)) throw new RulesDataException($"{file}: the root must be an object");

            var v = o["version"];
            if (v == null || v.Type != JTokenType.Integer) throw new RulesDataException($"{file}: missing \"version\"");
            if ((int)v != SupportedVersion) throw new RulesDataException($"{file}: version {(int)v} is not supported (expected {SupportedVersion})");
            return o;
        }

        static RulesDataException Error(string file, string where, string what) => new RulesDataException($"{file}: {where}: {what}");

        static JArray RequireArray(JObject o, string key, string file, string where)
        {
            if (!(o[key] is JArray a)) throw Error(file, where, $"'{key}' must be an array");
            return a;
        }

        static string RequireString(JObject o, string key, string file, string where)
        {
            var t = o[key];
            if (t == null || t.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)t))
                throw Error(file, where, $"'{key}' must be a non-empty string");
            return (string)t;
        }

        static string OptionalString(JObject o, string key, string file, string where)
        {
            var t = o[key];
            if (t == null) return null;
            if (t.Type != JTokenType.String) throw Error(file, where, $"'{key}' must be a string");
            return (string)t;
        }

        static int RequireInt(JObject o, string key, string file, string where)
        {
            var t = o[key];
            if (t == null || t.Type != JTokenType.Integer) throw Error(file, where, $"'{key}' must be a whole number");
            return (int)t;
        }
    }
}

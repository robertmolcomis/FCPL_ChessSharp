using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chess.Core
{
    /// <summary>Thrown when pieces.json / abilities.json / conditions.json are malformed or inconsistent. The message says what and where.</summary>
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
        /// <summary>The C# behaviour name: "type" in the JSON, or the id when there is no type.</summary>
        public string Type { get; internal set; }
        /// <summary>When the ability is on (null = always). From "condition": a conditions.json id or an inline condition.</summary>
        public Condition Condition { get; internal set; }
        public string DisplayName { get; internal set; }
        public string Description { get; internal set; }
        /// <summary>Raw parameters; read by the ability's C# behaviour.</summary>
        internal JObject Params { get; set; }
        /// <summary>The C# behaviour (one shared instance; parameters are global).</summary>
        public Ability Behaviour { get; internal set; }

        public bool IsPowerUp => Category == AbilityCategory.PowerUp;
    }

    public static class AbilityCategory
    {
        public const string Rule = "rule";
        public const string PowerUp = "powerup";
    }

    /// <summary>Everything loaded from the three JSON files. Install it with <see cref="PieceLibrary.Install"/>.</summary>
    public sealed class RulesData
    {
        public IReadOnlyDictionary<string, PieceDefinition> PiecesByName { get; internal set; }
        public IReadOnlyDictionary<char, PieceDefinition> PiecesBySymbol { get; internal set; }
        public IReadOnlyDictionary<string, AbilityInfo> Abilities { get; internal set; }
        /// <summary>Named conditions from conditions.json.</summary>
        public IReadOnlyDictionary<string, Condition> Conditions { get; internal set; }
        /// <summary>True if some piece can transform or latch a permanent condition (the game then checks after each move).</summary>
        public bool HasPostMoveEffects { get; internal set; }
        /// <summary>Symbols a promoting piece may choose from, in picker order (empty if there is no Promotes ability).</summary>
        public IReadOnlyList<char> PromotionChoices { get; internal set; }
        /// <summary>False when loaded for Classic: "powerup" abilities are defined but given to no piece.</summary>
        public bool PowerUpsIncluded { get; internal set; }
        /// <summary>The source text, kept so save files can snapshot the exact rules a match was played with.</summary>
        public string PiecesJson { get; internal set; }
        public string AbilitiesJson { get; internal set; }
        public string ConditionsJson { get; internal set; }
    }

    /// <summary>
    /// Parses and validates pieces.json + abilities.json + conditions.json (format: FEATURES_PLAN.md §2).
    /// Pure C#: callers pass the file contents, so it runs in Unity, tests and headless tools alike.
    /// </summary>
    public static class RulesDataLoader
    {
        public const int SupportedVersion = 1;

        /// <param name="includePowerUps">false = Classic: abilities with category "powerup" are left off every piece.</param>
        public static RulesData Load(string piecesJson, string abilitiesJson, string conditionsJson, bool includePowerUps = true)
        {
            var conditionsRoot = ParseRoot(conditionsJson, "conditions.json");
            var abilitiesRoot = ParseRoot(abilitiesJson, "abilities.json");
            var piecesRoot = ParseRoot(piecesJson, "pieces.json");

            var rawPieces = ReadPieceEntries(piecesRoot);
            var conditions = ReadConditions(conditionsRoot);
            var abilities = ReadAbilities(abilitiesRoot, conditions);

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
                var def = BuildPiece(raw, abilities, conditions, includePowerUps);
                byName[def.Id] = def;
                bySymbol[def.Symbol] = def;
            }

            foreach (var a in abilities.Values) a.Behaviour.Resolve(byName);
            var promotionChoices = ReadPromotionChoices(abilities, byName);
            CheckPieceNamesInConditions(conditions, abilities, byName);

            bool postMove = false;
            foreach (var def in byName.Values)
                if (def.Transforms.Count > 0 || def.PermanentConditions.Count > 0) postMove = true;

            return new RulesData
            {
                PiecesByName = byName,
                PiecesBySymbol = bySymbol,
                Abilities = abilities,
                Conditions = conditions,
                HasPostMoveEffects = postMove,
                PromotionChoices = promotionChoices,
                PowerUpsIncluded = includePowerUps,
                PiecesJson = piecesJson,
                AbilitiesJson = abilitiesJson,
                ConditionsJson = conditionsJson,
            };
        }

        // ───────────────────────── abilities.json ─────────────────────────

        static Dictionary<string, AbilityInfo> ReadAbilities(JObject root, Dictionary<string, Condition> conditions)
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
                string type = OptionalString(o, "type", file, where) ?? id;
                if (!AbilityRegistry.IsKnown(type))
                    throw Error(file, where, (type == id ? "the engine has no behaviour (C# class) for this id — give it a \"type\" or register one in AbilityRegistry"
                                                         : $"unknown type '{type}'") +
                                             " (known: " + string.Join(", ", AbilityRegistry.KnownIds) + ")");

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

                var info = new AbilityInfo
                {
                    Id = id,
                    Type = type,
                    Category = category,
                    AppliesTo = appliesTo,
                    Condition = o["condition"] == null ? null : ReadConditionRef(o["condition"], conditions, file, where + ".condition"),
                    DisplayName = OptionalString(o, "displayName", file, where) ?? id,
                    Description = OptionalString(o, "description", file, where) ?? "",
                    Params = (JObject)prms ?? new JObject(),
                };
                var behaviour = AbilityRegistry.Create(type);
                behaviour.Info = info;
                behaviour.KnownConditions = conditions;
                behaviour.Configure(info.Params); // reads + validates params
                info.Behaviour = behaviour;
                result[id] = info;
            }
            return result;
        }

        /// <summary>Resolves the Promotes ability's piece names to symbols (needs every piece loaded first).</summary>
        static IReadOnlyList<char> ReadPromotionChoices(Dictionary<string, AbilityInfo> abilities, Dictionary<string, PieceDefinition> pieces)
        {
            var result = new List<char>();
            if (!abilities.TryGetValue(AbilityIds.Promotes, out var p)) return result;
            var promotes = (PromotesAbility)p.Behaviour;
            const string file = "abilities.json", where = "ability 'Promotes' params.choices";
            foreach (var name in promotes.ChoiceNames)
            {
                if (!pieces.TryGetValue(name, out var def)) throw Error(file, where, $"unknown piece '{name}'");
                if (def.IsRoyal || def.IsWall) throw Error(file, where, $"can't promote to '{name}'");
                if (!result.Contains(def.Symbol)) result.Add(def.Symbol);
            }
            promotes.Choices = result;
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

        static PieceDefinition BuildPiece(RawPiece raw, Dictionary<string, AbilityInfo> abilities, Dictionary<string, Condition> conditions, bool includePowerUps)
        {
            const string file = "pieces.json";
            string where = raw.Where;

            // Abilities: defined, and allowed on this piece. Classic skips power-ups (still validated).
            var pieceAbilities = new List<Ability>();
            foreach (var id in raw.Flags)
            {
                if (!abilities.TryGetValue(id, out var info))
                    throw Error(file, where, $"flag '{id}' is not defined in abilities.json");
                if (!info.AppliesTo.Contains(raw.Name))
                    throw Error(file, where, $"ability '{id}' can't be given to '{raw.Name}' (appliesTo: {string.Join(", ", info.AppliesTo)})");
                if (info.IsPowerUp && !includePowerUps) continue;
                pieceAbilities.Add(info.Behaviour);
            }

            // Movement: "moves" → move-only primitives, "captures" → capture-only, captures "same" → both.
            var prims = new List<MovePrimitive>();
            bool same = raw.Captures.Type == JTokenType.String;
            if (same && (string)raw.Captures != "same")
                throw Error(file, where, "captures must be a list of patterns or \"same\"");

            prims.AddRange(ReadPatterns(raw.Moves, same ? MoveFlags.None : MoveFlags.MoveOnly, file, where + ".moves", required: false, conditions));
            if (!same) prims.AddRange(ReadPatterns(raw.Captures, MoveFlags.CaptureOnly, file, where + ".captures", required: false, conditions));

            return new PieceDefinition(raw.Name, raw.DisplayName, raw.Symbol, prims, raw.Royal, pieceAbilities);
        }

        /// <summary>
        /// A pattern list: [{ "dirs": [[dx,dy],...], "slide": bool, "maxSteps": n, "condition": "neverMoved" }, ...].
        /// Directions are from White's view. slide = unlimited range; otherwise maxSteps (default 1).
        /// "condition" = a conditions.json id or an inline condition; the pattern is only usable while it holds.
        /// </summary>
        internal static List<MovePrimitive> ReadPatterns(JToken token, MoveFlags baseFlags, string file, string where, bool required,
                                                         IReadOnlyDictionary<string, Condition> conditions)
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
                Condition condition = condTok == null ? null : ReadConditionRef(condTok, conditions, file, w + ".condition");

                if (!(o["dirs"] is JArray dirs) || dirs.Count == 0) throw Error(file, w, "dirs must be a non-empty list of [dx, dy] pairs");
                foreach (var d in dirs)
                {
                    if (!(d is JArray pair) || pair.Count != 2 || pair[0].Type != JTokenType.Integer || pair[1].Type != JTokenType.Integer)
                        throw Error(file, w, $"bad direction {d.ToString(Formatting.None)} — expected [dx, dy] with whole numbers");
                    int dx = (int)pair[0], dy = (int)pair[1];
                    if (dx == 0 && dy == 0) throw Error(file, w, "direction [0, 0] is not a move");
                    result.Add(new MovePrimitive(dx, dy, range, flags, condition));
                }
            }
            return result;
        }

        // ───────────────────────── conditions.json ─────────────────────────

        static readonly HashSet<string> NamedConditionFields = new HashSet<string>
            { "id", "displayName", "description", "permanent", "stat", "of", "except", "op", "value", "all", "any", "not" };
        static readonly HashSet<string> InlineConditionFields = new HashSet<string>
            { "stat", "of", "except", "op", "value", "all", "any", "not" };

        static Dictionary<string, Condition> ReadConditions(JObject root)
        {
            const string file = "conditions.json";
            var list = RequireArray(root, "conditions", file, "root");
            var result = new Dictionary<string, Condition>();

            // Pass 1: create every named condition, so they can refer to each other in any order.
            var bodies = new List<(JObject o, Condition c, string where)>();
            for (int i = 0; i < list.Count; i++)
            {
                string where = $"conditions[{i}]";
                if (!(list[i] is JObject o)) throw Error(file, where, "must be an object");
                string id = RequireString(o, "id", file, where);
                where = $"condition '{id}'";
                if (result.ContainsKey(id)) throw Error(file, where, "duplicate id");

                var permTok = o["permanent"];
                if (permTok != null && permTok.Type != JTokenType.Boolean) throw Error(file, where, "permanent must be true or false");

                var c = new Condition
                {
                    Id = id,
                    DisplayName = OptionalString(o, "displayName", file, where) ?? id,
                    Description = OptionalString(o, "description", file, where) ?? "",
                    Permanent = permTok != null && (bool)permTok,
                };
                result[id] = c;
                bodies.Add((o, c, where));
            }

            // Pass 2: fill them in.
            foreach (var (o, c, where) in bodies) ReadConditionBody(o, c, result, file, where, NamedConditionFields);

            // No condition may (indirectly) contain itself.
            var state = new Dictionary<Condition, int>(); // 1 = visiting, 2 = done
            foreach (var c in result.Values) CheckCycle(c, state, new List<string>());
            return result;
        }

        static void CheckCycle(Condition c, Dictionary<Condition, int> state, List<string> path)
        {
            state.TryGetValue(c, out int s);
            if (s == 2) return;
            if (c.Id != null) path.Add(c.Id);
            if (s == 1) throw new RulesDataException($"conditions.json: condition '{c.Id}': refers to itself ({string.Join(" → ", path)})");
            state[c] = 1;
            foreach (var child in c.Children) CheckCycle(child, state, path);
            state[c] = 2;
            if (c.Id != null) path.RemoveAt(path.Count - 1);
        }

        /// <summary>A "condition" value: an id from conditions.json, or an inline condition object.</summary>
        internal static Condition ReadConditionRef(JToken token, IReadOnlyDictionary<string, Condition> conditions, string file, string where)
        {
            if (token.Type == JTokenType.String)
            {
                var id = (string)token;
                if (conditions == null || !conditions.TryGetValue(id, out var named))
                    throw Error(file, where, $"unknown condition '{id}' (not in conditions.json)");
                return named;
            }
            if (token is JObject o)
            {
                var c = new Condition();
                ReadConditionBody(o, c, conditions, file, where, InlineConditionFields);
                return c;
            }
            throw Error(file, where, "a condition must be a conditions.json id (string) or a condition object");
        }

        static void ReadConditionBody(JObject o, Condition c, IReadOnlyDictionary<string, Condition> conditions, string file, string where,
                                      HashSet<string> allowedFields)
        {
            c.Where = $"{file}: {where}";
            foreach (var prop in o.Properties())
                if (!allowedFields.Contains(prop.Name))
                    throw Error(file, where, allowedFields == InlineConditionFields && NamedConditionFields.Contains(prop.Name)
                        ? $"'{prop.Name}' is only allowed on named conditions in conditions.json"
                        : $"unknown field '{prop.Name}' (allowed: {string.Join(", ", allowedFields)})");

            int shapes = 0;
            foreach (var k in new[] { "stat", "all", "any", "not" }) if (o[k] != null) shapes++;
            if (shapes != 1) throw Error(file, where, "needs exactly one of \"stat\", \"all\", \"any\", \"not\"");

            if (o["stat"] != null)
            {
                c.Kind = Condition.Shape.Compare;
                c.Stat = RequireString(o, "stat", file, where);
                if (!ConditionStats.TryGet(c.Stat, out var statDef))
                    throw Error(file, where, $"unknown stat '{c.Stat}' (known: {string.Join(", ", ConditionStats.KnownStats)})");
                c.StatDef = statDef;

                c.Op = RequireString(o, "op", file, where);
                if (Array.IndexOf(Condition.Operators, c.Op) < 0)
                    throw Error(file, where, $"op must be one of {string.Join(" ", Condition.Operators)}, got \"{c.Op}\"");

                var v = o["value"];
                if (v == null || v.Type != JTokenType.Integer) throw Error(file, where, "'value' must be a whole number");
                c.Value = (int)v;

                c.Of = ReadNameList(o, "of", file, where);
                c.Except = ReadNameList(o, "except", file, where);
                if ((c.Of != null || c.Except != null) && !statDef.CountsPieces)
                    throw Error(file, where, $"stat '{c.Stat}' doesn't count pieces, so it can't use \"of\" / \"except\"");
                return;
            }

            foreach (var k in new[] { "op", "value", "of", "except" })
                if (o[k] != null) throw Error(file, where, $"'{k}' only goes with \"stat\"");

            if (o["not"] != null)
            {
                c.Kind = Condition.Shape.Not;
                c.Children = new[] { ReadConditionRef(o["not"], conditions, file, where + ".not") };
                return;
            }

            string key = o["all"] != null ? "all" : "any";
            c.Kind = key == "all" ? Condition.Shape.All : Condition.Shape.Any;
            if (!(o[key] is JArray arr) || arr.Count == 0) throw Error(file, where, $"'{key}' must be a non-empty list of conditions");
            var children = new List<Condition>();
            for (int i = 0; i < arr.Count; i++) children.Add(ReadConditionRef(arr[i], conditions, file, $"{where}.{key}[{i}]"));
            c.Children = children;
        }

        /// <summary>"of" / "except": a piece name or a list of them (checked against pieces.json once pieces are loaded).</summary>
        static IReadOnlyCollection<string> ReadNameList(JObject o, string key, string file, string where)
        {
            var t = o[key];
            if (t == null) return null;
            var names = new List<string>();
            if (t.Type == JTokenType.String) names.Add((string)t);
            else if (t is JArray arr && arr.Count > 0)
                foreach (var n in arr)
                {
                    if (n.Type != JTokenType.String) throw Error(file, where, $"'{key}' must contain piece names (strings)");
                    names.Add((string)n);
                }
            else throw Error(file, where, $"'{key}' must be a piece name or a non-empty list of piece names");
            return names;
        }

        /// <summary>Every "of" / "except" must name a piece from pieces.json — catches typos like "knigth".</summary>
        static void CheckPieceNamesInConditions(Dictionary<string, Condition> conditions, Dictionary<string, AbilityInfo> abilities,
                                                Dictionary<string, PieceDefinition> pieces)
        {
            var all = new HashSet<Condition>();
            foreach (var c in conditions.Values) c.CollectAll(all);
            foreach (var a in abilities.Values) a.Condition?.CollectAll(all);
            foreach (var def in pieces.Values)
            {
                foreach (var m in def.Moves) m.Condition?.CollectAll(all);
                foreach (var m in def.ModifiedMoves) m.Condition?.CollectAll(all);
            }
            foreach (var c in all)
                foreach (var list in new[] { c.Of, c.Except })
                    if (list != null)
                        foreach (var name in list)
                            if (!pieces.ContainsKey(name))
                                throw new RulesDataException($"{c.Where}: unknown piece '{name}' (not in pieces.json)");
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
    }
}

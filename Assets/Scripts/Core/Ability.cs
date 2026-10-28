using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chess.Core
{
    /// <summary>Ability ids as written in pieces.json / abilities.json.</summary>
    public static class AbilityIds
    {
        public const string Castles = "Castles";
        public const string CastlePartner = "CastlePartner";
        public const string CreatesEnPassant = "CreatesEnPassant";
        public const string CapturesEnPassant = "CapturesEnPassant";
        public const string ResetsFiftyMoveClock = "ResetsFiftyMoveClock";
        public const string Promotes = "Promotes";
        public const string Wall = "Wall";
        public const string BerserkPawn = "BerserkPawn";
    }

    /// <summary>
    /// Generic behaviours meant to be reused under many ids: an abilities.json entry picks one with "type".
    /// (Without "type", the id itself names the class, as for the rule abilities.)
    /// </summary>
    public static class AbilityTypes
    {
        public const string TransformsInto = "TransformsInto";
    }

    /// <summary>
    /// The C# behaviour behind one entry of abilities.json. One instance per ability id, shared by every piece type
    /// that lists it (parameters are global). Per-piece state (captures, streaks...) lives on <see cref="Piece"/>.
    ///
    /// WHEN an ability is switched on is data: its optional "condition" (an id from conditions.json, or an inline condition).
    /// WHAT it does is the C# class. To add a behaviour: subclass this, override the hooks it needs, and register it in
    /// <see cref="AbilityRegistry"/>.
    /// </summary>
    public abstract class Ability
    {
        public AbilityInfo Info { get; internal set; }
        public string Id => Info.Id;
        public bool IsPowerUp => Info.IsPowerUp;
        /// <summary>When the ability is on (null = always).</summary>
        public Condition Condition => Info.Condition;

        /// <summary>The conditions from conditions.json, for abilities whose params contain move patterns.</summary>
        protected internal IReadOnlyDictionary<string, Condition> KnownConditions { get; internal set; }

        /// <summary>Called once when abilities.json is loaded: read and validate <paramref name="parameters"/> here.</summary>
        protected internal virtual void Configure(JObject parameters) { }

        /// <summary>Called once every piece is loaded: resolve piece names from params here.</summary>
        protected internal virtual void Resolve(IReadOnlyDictionary<string, PieceDefinition> pieces) { }

        // ───────────────────────── Hooks ─────────────────────────

        /// <summary>
        /// Move set a piece type gets while this ability is active (see <see cref="IsActive"/>), built once from the
        /// type's base moves. Return null if the ability doesn't change movement.
        /// </summary>
        protected internal virtual IReadOnlyList<MovePrimitive> BuildActiveMoves(IReadOnlyList<MovePrimitive> baseMoves) => null;

        /// <summary>Is the ability's effect switched on for this piece right now? Default: its condition holds.</summary>
        public virtual bool IsActive(Piece piece) => Condition == null || Condition.Evaluate(piece);

        /// <summary>Adds extra pseudo-legal moves for the piece (e.g. castling). Legality is checked by the engine afterwards.</summary>
        protected internal virtual void GenerateExtraMoves(Game game, Square from, Piece piece, List<Move> output) { }

        /// <summary>
        /// Per-piece state that must change the position hash (0 = none). Default: a conditional ability that is
        /// currently on changes the hash (e.g. a berserk pawn is a different position from a normal one).
        /// </summary>
        public virtual ulong StateHash(Piece piece) => Condition != null && IsActive(piece) ? IdHash : 0UL;

        ulong _idHash;
        ulong IdHash
        {
            get
            {
                if (_idHash != 0) return _idHash;
                ulong h = 14695981039346656037UL; // FNV-1a, stable across runs
                foreach (char c in Id) { h ^= c; h *= 1099511628211UL; }
                return _idHash = h | 1;
            }
        }

        /// <summary>Error for a bad parameter, in the loader's message format.</summary>
        protected RulesDataException Invalid(string what) => new RulesDataException($"abilities.json: ability '{Id}' params: {what}");

        public override string ToString() => Id;
    }

    /// <summary>
    /// Behaviour name → C# class. An abilities.json entry uses its "type", or its id when it has no type.
    /// A name without an entry here is a load error.
    /// </summary>
    public static class AbilityRegistry
    {
        static readonly Dictionary<string, Func<Ability>> Factories = new Dictionary<string, Func<Ability>>
        {
            [AbilityIds.Castles] = () => new CastlesAbility(),
            [AbilityIds.CastlePartner] = () => new CastlePartnerAbility(),
            [AbilityIds.CreatesEnPassant] = () => new CreatesEnPassantAbility(),
            [AbilityIds.CapturesEnPassant] = () => new CapturesEnPassantAbility(),
            [AbilityIds.ResetsFiftyMoveClock] = () => new ResetsFiftyMoveClockAbility(),
            [AbilityIds.Promotes] = () => new PromotesAbility(),
            [AbilityIds.Wall] = () => new WallAbility(),
            [AbilityIds.BerserkPawn] = () => new BerserkPawnAbility(),
            [AbilityTypes.TransformsInto] = () => new TransformsIntoAbility(),
        };

        public static IEnumerable<string> KnownIds => Factories.Keys;

        public static bool IsKnown(string id) => Factories.ContainsKey(id);

        /// <summary>Registers (or replaces) the class behind a behaviour name. Call before loading the data.</summary>
        public static void Register(string id, Func<Ability> factory) => Factories[id] = factory ?? throw new ArgumentNullException(nameof(factory));

        internal static Ability Create(string id) => Factories[id]();
    }
}

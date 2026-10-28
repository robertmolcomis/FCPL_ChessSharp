using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chess.Core
{
    // ───────────────────────── Standard chess rules (category "rule") ─────────────────────────

    /// <summary>
    /// Generic castling: the unmoved piece moves 2 squares toward an unmoved <see cref="CastlePartnerAbility"/> piece on
    /// the same rank, with only empty squares between them; the partner lands on the square it passed.
    /// Can't castle out of, through, or into check.
    /// </summary>
    public sealed class CastlesAbility : Ability
    {
        protected internal override void GenerateExtraMoves(Game game, Square kingSq, Piece king, List<Move> output)
        {
            if (king.HasMoved) return;
            var board = game.Board;
            var enemy = king.Color.Opponent();
            bool inCheckChecked = false;

            foreach (int dir in new[] { 1, -1 })
            {
                // find the first piece in this direction
                var sq = kingSq.Offset(dir, 0);
                while (board.IsValid(sq) && board[sq] == null) sq = sq.Offset(dir, 0);
                if (!board.IsValid(sq)) continue;

                var partner = board[sq];
                if (partner.Color != king.Color || partner.HasMoved || !partner.Definition.Has(AbilityIds.CastlePartner)) continue;

                var kingTo = kingSq.Offset(2 * dir, 0);
                var pass = kingSq.Offset(dir, 0);
                if (!board.IsValid(kingTo)) continue;
                // king's destination must be empty or the partner itself (partner moves away)
                var atDest = board[kingTo];
                if (atDest != null && atDest != partner) continue;

                if (!inCheckChecked)
                {
                    if (MoveGenerator.IsAttacked(board, kingSq, enemy)) return; // can't castle out of check
                    inCheckChecked = true;
                }
                if (MoveGenerator.IsAttacked(board, pass, enemy)) continue; // through check
                // "into check" is filtered by the normal legality test

                output.Add(new Move(kingSq, kingTo, '\0', MoveKind.Castle, sq));
            }
        }
    }

    /// <summary>The piece a <see cref="CastlesAbility"/> piece castles with (rook). Marker: read by castling.</summary>
    public sealed class CastlePartnerAbility : Ability { }

    /// <summary>A 2-square straight move leaves an en-passant square behind. Marker: read by move generation.</summary>
    public sealed class CreatesEnPassantAbility : Ability { }

    /// <summary>Capture-only diagonal steps may land on the en-passant square. Marker: read by move generation.</summary>
    public sealed class CapturesEnPassantAbility : Ability { }

    /// <summary>Moving this piece is irreversible: it resets the 50-move counter. Marker: read by the game.</summary>
    public sealed class ResetsFiftyMoveClockAbility : Ability { }

    /// <summary>
    /// Wall: never moves, nothing passes over it (knights included). A piece may stand on it; capturing that piece
    /// destroys the wall instead and the attacker stays where it was. Marker: the board and move generation read it.
    /// </summary>
    public sealed class WallAbility : Ability { }

    /// <summary>Reaching the last rank, the piece becomes one of <see cref="Choices"/>. Params: { "choices": [piece names] }.</summary>
    public sealed class PromotesAbility : Ability
    {
        /// <summary>Piece names from params, in picker order.</summary>
        public IReadOnlyList<string> ChoiceNames { get; private set; }
        /// <summary>Symbols of the pieces it can become, in picker order (resolved once all pieces are loaded).</summary>
        public IReadOnlyList<char> Choices { get; internal set; } = new char[0];

        protected internal override void Configure(JObject parameters)
        {
            if (!(parameters["choices"] is JArray arr) || arr.Count == 0)
                throw Invalid("'choices' must list at least one piece");
            var names = new List<string>();
            foreach (var t in arr)
            {
                if (t.Type != JTokenType.String) throw Invalid("'choices' must contain piece names (strings)");
                names.Add((string)t);
            }
            ChoiceNames = names;
        }
    }

    // ───────────────────────── Power-ups (category "powerup") ─────────────────────────

    /// <summary>
    /// Berserk pawn: while its condition holds (conditions.json, e.g. "captureTwoPieces"), the pawn's capture pattern is
    /// replaced by (or extended with) the pattern in params. Lost on promotion with the pawn type.
    /// Params: { "mode": "replace" | "add", "captures": [patterns] }.
    /// </summary>
    public sealed class BerserkPawnAbility : Ability
    {
        public bool ReplacesCaptures { get; private set; }
        IReadOnlyList<MovePrimitive> _captures;

        protected internal override void Configure(JObject parameters)
        {
            if (parameters["threshold"] != null)
                throw Invalid("'threshold' moved to conditions.json — give the ability a \"condition\" instead");

            var mode = parameters["mode"];
            if (mode == null || mode.Type != JTokenType.String || ((string)mode != "replace" && (string)mode != "add"))
                throw Invalid("'mode' must be \"replace\" or \"add\"");
            ReplacesCaptures = (string)mode == "replace";

            _captures = RulesDataLoader.ReadPatterns(parameters["captures"], MoveFlags.CaptureOnly,
                "abilities.json", $"ability '{Id}' params.captures", required: true, KnownConditions);
        }

        protected internal override IReadOnlyList<MovePrimitive> BuildActiveMoves(IReadOnlyList<MovePrimitive> baseMoves)
        {
            var result = new List<MovePrimitive>();
            foreach (var m in baseMoves)
            {
                if (!ReplacesCaptures) { result.Add(m); continue; }
                if ((m.Flags & MoveFlags.CaptureOnly) != 0) continue;                                                // old captures dropped
                result.Add(new MovePrimitive(m.Dx, m.Dy, m.MaxRange, m.Flags | MoveFlags.MoveOnly, m.Condition));  // moves kept
            }
            result.AddRange(_captures);
            return result;
        }
    }

    /// <summary>
    /// Generic upgrade: when its condition becomes true (checked after each move of the piece's side), the piece turns
    /// into another piece type — same piece, same id and history, new moves and abilities. Undo reverts it.
    /// Used through "type": "TransformsInto", so any number of upgrade paths can be added in JSON alone.
    /// Params: { "into": piece name }.
    /// </summary>
    public sealed class TransformsIntoAbility : Ability
    {
        public string IntoName { get; private set; }
        public PieceDefinition Into { get; private set; }

        protected internal override void Configure(JObject parameters)
        {
            var into = parameters["into"];
            if (into == null || into.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)into))
                throw Invalid("'into' must be a piece name");
            IntoName = (string)into;
            if (Condition == null)
                throw new RulesDataException($"abilities.json: ability '{Id}': a TransformsInto ability needs a \"condition\"");
        }

        protected internal override void Resolve(IReadOnlyDictionary<string, PieceDefinition> pieces)
        {
            if (!pieces.TryGetValue(IntoName, out var def)) throw Invalid($"'into' names unknown piece '{IntoName}'");
            if (def.IsWall) throw Invalid($"can't transform into a wall ('{IntoName}')");
            if (Info.AppliesTo.Contains(IntoName)) throw Invalid($"'{IntoName}' can't transform into itself");
            foreach (var from in Info.AppliesTo)
                if (pieces.TryGetValue(from, out var f) && f.IsWall)
                    throw new RulesDataException($"abilities.json: ability '{Id}': walls can't transform ('{from}')");
            Into = def;
        }

        /// <summary>It never stays "on": the piece changes type the moment the condition holds.</summary>
        public override ulong StateHash(Piece piece) => 0;
    }
}

using System;
using System.Collections.Generic;

namespace Chess.Core
{
    public enum PieceColor { White, Black }

    public static class PieceColorExt
    {
        public static PieceColor Opponent(this PieceColor c) => c == PieceColor.White ? PieceColor.Black : PieceColor.White;
        /// <summary>+1 for White (forward = up), -1 for Black.</summary>
        public static int Forward(this PieceColor c) => c == PieceColor.White ? 1 : -1;
    }

    [Flags]
    public enum MoveFlags
    {
        None = 0,
        MoveOnly = 1 << 0,      // can't capture with this primitive (pawn push)
        CaptureOnly = 1 << 1,   // only captures (pawn diagonal)
    }

    /// <summary>
    /// One movement primitive. Direction is written from White's point of view (dy > 0 = forward);
    /// it is mirrored automatically for Black.
    /// MaxRange = 1 is a leap/step (knight, king), 0 = unlimited slide (rook, bishop, nightrider...).
    /// Steps of range 1 are never blocked by intermediate squares (the knight "jumps").
    /// <see cref="Condition"/> (from the pattern's "condition") limits when it can be used, e.g. the pawn double step.
    /// </summary>
    public readonly struct MovePrimitive
    {
        public readonly int Dx;
        public readonly int Dy;
        public readonly int MaxRange;
        public readonly MoveFlags Flags;
        /// <summary>Only usable while this holds (null = always).</summary>
        public readonly Condition Condition;

        public MovePrimitive(int dx, int dy, int maxRange, MoveFlags flags = MoveFlags.None, Condition condition = null)
        {
            Dx = dx; Dy = dy; MaxRange = maxRange; Flags = flags; Condition = condition;
        }

        /// <summary>Can <paramref name="piece"/> use this primitive right now?</summary>
        public bool UsableBy(Piece piece) => Condition == null || Condition.Evaluate(piece);
    }

    /// <summary>
    /// Data-only description of a piece type, built from pieces.json by <see cref="RulesDataLoader"/>.
    /// No hardcoded "bishop moves diagonally" anywhere in the engine. Behaviour beyond movement comes from its
    /// <see cref="Abilities"/> (the "flags" of pieces.json).
    /// </summary>
    public sealed class PieceDefinition
    {
        /// <summary>The piece's "name" in pieces.json (e.g. "pawn").</summary>
        public string Id { get; }
        public string DisplayName { get; }
        public char Symbol { get; }           // FEN-style letter, uppercase
        public bool IsRoyal { get; }
        /// <summary>The piece's abilities, in the order of its pieces.json flags.</summary>
        public IReadOnlyList<Ability> Abilities { get; }
        /// <summary>
        /// Move primitives: entries from "moves" are MoveOnly, from "captures" CaptureOnly; with captures "same" they do both.
        /// </summary>
        public IReadOnlyList<MovePrimitive> Moves { get; }

        // Cached because the engine asks these constantly.
        public bool IsWall { get; }
        public bool PromotesOnLastRank => Promotes != null;
        public PromotesAbility Promotes { get; }

        /// <summary>Upgrades this type can go through (TransformsInto abilities), in flag order.</summary>
        public IReadOnlyList<TransformsIntoAbility> Transforms { get; }
        /// <summary>Every "permanent" condition this type depends on (latched after each move of its side).</summary>
        public IReadOnlyList<Condition> PermanentConditions { get; }

        readonly HashSet<string> _abilityIds = new HashSet<string>();
        // The ability that can change this type's move set (e.g. berserk), and the move set it gives.
        readonly Ability _moveModifier;
        readonly IReadOnlyList<MovePrimitive> _modifiedMoves;

        public PieceDefinition(string id, string displayName, char symbol, IEnumerable<MovePrimitive> moves,
                               bool isRoyal = false, IEnumerable<Ability> abilities = null)
        {
            Id = id; DisplayName = displayName; Symbol = char.ToUpperInvariant(symbol);
            IsRoyal = isRoyal;
            Moves = new List<MovePrimitive>(moves);
            var list = abilities != null ? new List<Ability>(abilities) : new List<Ability>();
            Abilities = list;

            foreach (var a in list)
            {
                _abilityIds.Add(a.Id);
                if (a is PromotesAbility p) Promotes = p;
                var modified = a.BuildActiveMoves(Moves);
                if (modified == null) continue;
                if (_moveModifier != null)
                    throw new RulesDataException($"pieces.json: piece '{id}': '{_moveModifier.Id}' and '{a.Id}' both change its moves — only one such ability per piece is supported");
                _moveModifier = a;
                _modifiedMoves = modified;
            }
            IsWall = Has(AbilityIds.Wall);

            var transforms = new List<TransformsIntoAbility>();
            foreach (var a in list) if (a is TransformsIntoAbility t) transforms.Add(t);
            Transforms = transforms;

            // permanent conditions reachable from the abilities and from the move patterns (both move sets)
            var all = new HashSet<Condition>();
            foreach (var a in list) a.Condition?.CollectAll(all);
            foreach (var m in Moves) m.Condition?.CollectAll(all);
            if (_modifiedMoves != null) foreach (var m in _modifiedMoves) m.Condition?.CollectAll(all);
            var permanent = new List<Condition>();
            foreach (var c in all) if (c.Permanent) permanent.Add(c);
            PermanentConditions = permanent;
        }

        public bool Has(string abilityId) => _abilityIds.Contains(abilityId);

        /// <summary>The ability of type <typeparamref name="T"/>, or null.</summary>
        public T Get<T>() where T : Ability
        {
            foreach (var a in Abilities) if (a is T t) return t;
            return null;
        }

        /// <summary>Movement primitives currently in effect for <paramref name="piece"/> (base moves, or the modified set).</summary>
        public IReadOnlyList<MovePrimitive> MovesFor(Piece piece) =>
            _moveModifier != null && _moveModifier.IsActive(piece) ? _modifiedMoves : Moves;

        /// <summary>The move set while the move-changing ability is active (same as Moves if there is none).</summary>
        public IReadOnlyList<MovePrimitive> ModifiedMoves => _modifiedMoves ?? Moves;

        public override string ToString() => DisplayName;
    }
}

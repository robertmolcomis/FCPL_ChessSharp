using System;
using System.Collections.Generic;

namespace Chess.Core
{
    public enum MoveKind : byte
    {
        Normal,
        DoubleStep, // creates an en-passant square
        EnPassant,  // Aux = square of the captured pawn
        Castle,     // Aux = square of the castling partner (rook)
    }

    /// <summary>Small, serializable command object. UI creates it → engine validates and applies it.</summary>
    public readonly struct Move : IEquatable<Move>
    {
        public readonly Square From;
        public readonly Square To;
        /// <summary>Symbol of the piece to promote to ('\0' = none).</summary>
        public readonly char Promotion;
        public readonly MoveKind Kind;
        /// <summary>Extra square: EP victim or castling partner. Unused otherwise.</summary>
        public readonly Square Aux;

        public Move(Square from, Square to, char promotion = '\0', MoveKind kind = MoveKind.Normal, Square aux = default)
        {
            From = from; To = to; Promotion = promotion; Kind = kind; Aux = aux;
        }

        public bool IsPromotion => Promotion != '\0';

        public bool Equals(Move o) => From == o.From && To == o.To && Promotion == o.Promotion;
        public override bool Equals(object obj) => obj is Move m && Equals(m);
        public override int GetHashCode() => From.GetHashCode() * 31 + To.GetHashCode() * 7 + Promotion;

        /// <summary>Long algebraic / UCI style, e.g. e2e4, e7e8q.</summary>
        public override string ToString() => $"{From}{To}{(IsPromotion ? char.ToLowerInvariant(Promotion).ToString() : "")}";
    }

    /// <summary>What happened when a move was applied — everything needed to undo it exactly.</summary>
    public sealed class MoveRecord
    {
        public Move Move { get; }
        public Piece Mover { get; }
        public Piece Captured { get; }
        public Square CapturedSquare { get; }
        public PieceDefinition DefinitionBefore { get; }
        /// <summary>The mover changed type (promotion, or transformation right after the move).</summary>
        public bool Promoted => Mover.Definition != DefinitionBefore;
        public bool GaveCheck { get; internal set; }
        /// <summary>
        /// The target stood on a wall: the wall was destroyed (Captured = the wall) and the mover stayed on Move.From.
        /// </summary>
        public bool Bounced { get; }
        /// <summary>Square the mover ends on (Move.From after a bounce).</summary>
        public Square MoverSquare => Bounced ? Move.From : Move.To;

        // Undo info
        internal Square? PrevEnPassant { get; }
        internal Square PrevEnPassantVictim { get; }
        internal int PrevHalfmoveClock { get; }
        // move streaks: the side's previously streaking piece, its streak, and the mover's streak before this move
        internal Piece PrevStreakPiece { get; set; }
        internal int PrevStreakPieceStreak { get; set; }
        internal int PrevMoverStreak { get; set; }
        /// <summary>Permanent conditions first reached by this move (undone in reverse).</summary>
        internal List<(Piece piece, Condition condition)> Latched { get; set; }

        /// <summary>
        /// Pieces that changed type through a TransformsInto ability after this move (e.g. knight → paladin),
        /// with the type each had before. Empty when nothing transformed. Promotion is not listed here.
        /// </summary>
        public IReadOnlyList<(Piece piece, PieceDefinition before)> Transformations => (IReadOnlyList<(Piece, PieceDefinition)>)_transformations ?? Array.Empty<(Piece, PieceDefinition)>();
        internal List<(Piece piece, PieceDefinition before)> _transformations;
        /// <summary>The rook (or other partner) that moved with a castling move.</summary>
        public Piece CastlePartner { get; }

        internal MoveRecord(Move move, Piece mover, Piece captured, Square capturedSquare, PieceDefinition before,
                            Square? prevEp, Square prevEpVictim, int prevHalfmove, Piece castlePartner, bool bounced = false)
        {
            Bounced = bounced;
            Move = move; Mover = mover; Captured = captured; CapturedSquare = capturedSquare; DefinitionBefore = before;
            PrevEnPassant = prevEp; PrevEnPassantVictim = prevEpVictim; PrevHalfmoveClock = prevHalfmove;
            CastlePartner = castlePartner;
        }
    }
}

using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>The standard set (+ a couple of fairy pieces to prove the data-driven approach).</summary>
    public static class PieceLibrary
    {
        public static readonly PieceDefinition Pawn = new PieceDefinition("pawn", "Pawn", 'P', new[]
        {
            new MovePrimitive(0, 1, 1, MoveFlags.MoveOnly),
            new MovePrimitive(0, 1, 2, MoveFlags.MoveOnly | MoveFlags.FirstMoveOnly),
            new MovePrimitive(1, 1, 1, MoveFlags.CaptureOnly),
            new MovePrimitive(-1, 1, 1, MoveFlags.CaptureOnly),
        }, promotesOnLastRank: true,
           abilities: PieceAbilities.CreatesEnPassant | PieceAbilities.CapturesEnPassant | PieceAbilities.ResetsFiftyMoveClock);

        public static readonly PieceDefinition Knight = new PieceDefinition("knight", "Knight", 'N', Patterns.Leaper(1, 2));
        public static readonly PieceDefinition Bishop = new PieceDefinition("bishop", "Bishop", 'B', Patterns.Bishop());
        public static readonly PieceDefinition Rook = new PieceDefinition("rook", "Rook", 'R', Patterns.Rook(), abilities: PieceAbilities.CastlePartner);
        public static readonly PieceDefinition Queen = new PieceDefinition("queen", "Queen", 'Q', Patterns.Concat(Patterns.Rook(), Patterns.Bishop()));
        public static readonly PieceDefinition King = new PieceDefinition("king", "King", 'K', Patterns.Concat(Patterns.Rook(1), Patterns.Bishop(1)), isRoyal: true, abilities: PieceAbilities.Castles);

        // Fairy pieces (M3) — already free thanks to the data-driven design.
        public static readonly PieceDefinition Archbishop = new PieceDefinition("archbishop", "Archbishop", 'A', Patterns.Concat(Patterns.Bishop(), Patterns.Leaper(1, 2)));
        public static readonly PieceDefinition Chancellor = new PieceDefinition("chancellor", "Chancellor", 'C', Patterns.Concat(Patterns.Rook(), Patterns.Leaper(1, 2)));

        /// <summary>What a pawn may promote to, in picker order.</summary>
        public static readonly char[] PromotionChoices = { 'Q', 'R', 'B', 'N' };

        public static readonly IReadOnlyDictionary<char, PieceDefinition> BySymbol = new Dictionary<char, PieceDefinition>
        {
            ['P'] = Pawn, ['N'] = Knight, ['B'] = Bishop, ['R'] = Rook, ['Q'] = Queen, ['K'] = King,
            ['A'] = Archbishop, ['C'] = Chancellor,
        };
    }
}

namespace Chess.Core
{
    /// <summary>
    /// A piece instance. Carries its own state so per-piece mechanics (berserk counter, modifiers, on-wall)
    /// can be added later without touching the board.
    /// </summary>
    public sealed class Piece
    {
        static int _nextId;

        /// <summary>Stable id so the view can track the same piece across moves.</summary>
        public int Id { get; }
        public PieceDefinition Definition { get; internal set; }
        public PieceColor Color { get; }
        public int MoveCount { get; internal set; }
        public int CaptureCount { get; internal set; }

        public bool HasMoved => MoveCount > 0;
        public bool IsRoyal => Definition.IsRoyal;

        public Piece(PieceDefinition definition, PieceColor color)
        {
            Id = System.Threading.Interlocked.Increment(ref _nextId);
            Definition = definition;
            Color = color;
        }

        /// <summary>
        /// Extra state that must be part of the position hash (modifiers, on-wall, berserk…). 0 = none.
        /// M3 power-ups extend this.
        /// </summary>
        public ulong StateHash() => 0UL;

        public override string ToString() => $"{Color} {Definition}";
    }
}

namespace Chess.Core
{
    public enum GameStatus
    {
        Ongoing,
        Checkmate,
        Resignation,
        Stalemate,
        DrawFiftyMove,
        DrawRepetition,
        DrawInsufficientMaterial,
        DrawAgreed,
    }

    public static class GameStatusExt
    {
        public static bool IsOver(this GameStatus s) => s != GameStatus.Ongoing;
        public static bool IsDraw(this GameStatus s) => s >= GameStatus.Stalemate;

        public static string Describe(this GameStatus s, PieceColor? winner) => s switch
        {
            GameStatus.Ongoing => "",
            GameStatus.Checkmate => $"Checkmate — {winner} wins",
            GameStatus.Resignation => $"{winner?.Opponent()} resigned — {winner} wins",
            GameStatus.Stalemate => "Draw by stalemate",
            GameStatus.DrawFiftyMove => "Draw by the 50-move rule",
            GameStatus.DrawRepetition => "Draw by threefold repetition",
            GameStatus.DrawInsufficientMaterial => "Draw — insufficient material",
            GameStatus.DrawAgreed => "Draw agreed",
            _ => s.ToString(),
        };
    }
}

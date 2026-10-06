using System;
using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>
    /// Width × height grid with a mask of valid squares (so plus-shaped / holed boards work later).
    /// Standard chess = 8×8 with every square valid.
    /// </summary>
    public sealed class Board
    {
        public int Width { get; }
        public int Height { get; }

        readonly Piece[,] _cells;
        readonly bool[,] _valid;

        public Board(int width = 8, int height = 8, Func<int, int, bool> validMask = null)
        {
            Width = width; Height = height;
            _cells = new Piece[width, height];
            _valid = new bool[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    _valid[x, y] = validMask?.Invoke(x, y) ?? true;
        }

        public bool InBounds(Square s) => s.File >= 0 && s.File < Width && s.Rank >= 0 && s.Rank < Height;
        public bool IsValid(Square s) => InBounds(s) && _valid[s.File, s.Rank];

        public Piece this[Square s]
        {
            get => IsValid(s) ? _cells[s.File, s.Rank] : null;
            internal set
            {
                if (!IsValid(s)) throw new ArgumentOutOfRangeException(nameof(s), $"{s} is not a valid square");
                _cells[s.File, s.Rank] = value;
            }
        }

        public IEnumerable<(Square square, Piece piece)> Pieces()
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (_cells[x, y] != null) yield return (new Square(x, y), _cells[x, y]);
        }

        public IEnumerable<Square> ValidSquares()
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (_valid[x, y]) yield return new Square(x, y);
        }

        /// <summary>Places pieces from the piece-placement part of a FEN (ranks top→bottom, '/' separated).</summary>
        public static Board FromFen(string placement)
        {
            var rows = placement.Split(' ')[0].Split('/');
            int height = rows.Length;
            int width = 0;
            foreach (var c in rows[0]) width += char.IsDigit(c) ? c - '0' : 1;

            var board = new Board(width, height);
            for (int r = 0; r < height; r++)
            {
                int rank = height - 1 - r, file = 0;
                foreach (var c in rows[r])
                {
                    if (char.IsDigit(c)) { file += c - '0'; continue; }
                    if (!PieceLibrary.BySymbol.TryGetValue(char.ToUpperInvariant(c), out var def))
                        throw new FormatException($"Unknown piece symbol '{c}'");
                    board[new Square(file, rank)] = new Piece(def, char.IsUpper(c) ? PieceColor.White : PieceColor.Black);
                    file++;
                }
            }
            return board;
        }

        public const string StandardStart = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";
    }
}

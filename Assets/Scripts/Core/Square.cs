using System;

namespace Chess.Core
{
    /// <summary>A board coordinate. File = x (0 = a), Rank = y (0 = rank 1, White's side).</summary>
    public readonly struct Square : IEquatable<Square>
    {
        public readonly int File;
        public readonly int Rank;

        public Square(int file, int rank) { File = file; Rank = rank; }

        public Square Offset(int df, int dr) => new Square(File + df, Rank + dr);

        public bool Equals(Square other) => File == other.File && Rank == other.Rank;
        public override bool Equals(object obj) => obj is Square s && Equals(s);
        public override int GetHashCode() => (File * 397) ^ Rank;
        public static bool operator ==(Square a, Square b) => a.Equals(b);
        public static bool operator !=(Square a, Square b) => !a.Equals(b);

        public override string ToString() =>
            File >= 0 && File < 26 ? $"{(char)('a' + File)}{Rank + 1}" : $"({File},{Rank})";
    }
}

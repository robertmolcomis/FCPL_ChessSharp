using System;
using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>The save data is damaged, from another version, or made with different rules.</summary>
    public sealed class SaveFormatException : Exception
    {
        public SaveFormatException(string message) : base(message) { }
    }

    /// <summary>Writes values with an exact number of bits, most significant bit first. The last byte is zero-padded.</summary>
    public sealed class BitWriter
    {
        readonly List<byte> _bytes = new List<byte>();

        /// <summary>Bits written so far (without the padding of the last byte).</summary>
        public int BitCount { get; private set; }

        public void WriteBit(bool bit)
        {
            if ((BitCount & 7) == 0) _bytes.Add(0);
            if (bit) _bytes[_bytes.Count - 1] |= (byte)(0x80 >> (BitCount & 7));
            BitCount++;
        }

        /// <summary>Writes the low <paramref name="bits"/> bits of <paramref name="value"/> (0..64 bits).</summary>
        public void Write(ulong value, int bits)
        {
            if (bits < 0 || bits > 64) throw new ArgumentOutOfRangeException(nameof(bits));
            if (bits < 64 && value >> bits != 0) throw new ArgumentOutOfRangeException(nameof(value), $"{value} doesn't fit in {bits} bits");
            for (int i = bits - 1; i >= 0; i--) WriteBit(((value >> i) & 1) != 0);
        }

        public void Write(bool value) => WriteBit(value);

        /// <summary>
        /// Elias-gamma code for n ≥ 1: small numbers are short (1 → 1 bit, 2-3 → 3 bits, 4-7 → 5 bits, 100 → 13 bits),
        /// and no maximum has to be agreed on in advance.
        /// </summary>
        public void WriteGamma(uint n)
        {
            if (n == 0) throw new ArgumentOutOfRangeException(nameof(n), "Gamma codes start at 1");
            int length = BitsFor(n + 1UL) - 1; // floor(log2 n)
            for (int i = 0; i < length; i++) WriteBit(false);
            Write(n, length + 1);
        }

        /// <summary>Copies every bit of another writer onto the end of this one.</summary>
        public void Append(BitWriter other)
        {
            for (int i = 0; i < other.BitCount; i++)
                WriteBit((other._bytes[i >> 3] & (0x80 >> (i & 7))) != 0);
        }

        public byte[] ToArray() => _bytes.ToArray();

        /// <summary>
        /// Bits needed to tell <paramref name="choices"/> options apart: 1 → 0 bits (nothing to store), 2 → 1, 3-4 → 2, 5-8 → 3...
        /// </summary>
        public static int BitsFor(ulong choices)
        {
            int bits = 0;
            while (choices > 1UL << bits) bits++;
            return bits;
        }
    }

    /// <summary>Reads what <see cref="BitWriter"/> wrote. Throws <see cref="SaveFormatException"/> when the data runs out.</summary>
    public sealed class BitReader
    {
        readonly byte[] _bytes;

        public int Position { get; private set; }
        public int BitLength => _bytes.Length * 8;
        public int Remaining => BitLength - Position;

        public BitReader(byte[] bytes) => _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));

        public bool ReadBit()
        {
            if (Position >= BitLength) throw new SaveFormatException("Save data ends too early");
            bool bit = (_bytes[Position >> 3] & (0x80 >> (Position & 7))) != 0;
            Position++;
            return bit;
        }

        public ulong Read(int bits)
        {
            if (bits < 0 || bits > 64) throw new ArgumentOutOfRangeException(nameof(bits));
            ulong v = 0;
            for (int i = 0; i < bits; i++) v = (v << 1) | (ReadBit() ? 1UL : 0UL);
            return v;
        }

        public int ReadInt(int bits) => (int)Read(bits);

        public uint ReadGamma()
        {
            int length = 0;
            while (!ReadBit())
                if (++length > 31) throw new SaveFormatException("Bad number in save data");
            return (uint)((1UL << length) | Read(length));
        }

        /// <summary>True when only the zero padding of the last byte is left.</summary>
        public bool AtPaddingOnly()
        {
            if (Remaining >= 8) return false;
            int p = Position;
            for (; p < BitLength; p++)
                if ((_bytes[p >> 3] & (0x80 >> (p & 7))) != 0) return false;
            return true;
        }
    }
}

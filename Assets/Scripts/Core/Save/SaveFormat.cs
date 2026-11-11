using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chess.Core
{
    /// <summary>
    /// Things both save files share: the 22-bit header and the lookup tables that turn piece types and
    /// conditions into small numbers.
    ///
    /// Header (22 bits):
    /// <code>
    ///   4  format version
    ///   1  file kind        0 = board state, 1 = move history (so swapped files are caught)
    ///   1  classic          1 = played without power-ups
    ///  16  rules fingerprint  hash of pieces/abilities/conditions .json (whitespace ignored)
    /// </code>
    /// The fingerprint matters because both files store piece types and moves as indexes into the current rules:
    /// if the JSON changes, an old save would decode into different pieces / moves, so it is refused instead.
    /// </summary>
    public static class SaveFormat
    {
        public const int Version = 1;
        const int VersionBits = 4;
        const int FingerprintBits = 16;

        /// <summary>The board is fixed at 8×8 for now (no size is stored).</summary>
        public const int BoardSize = 8;
        public const int SquareCount = BoardSize * BoardSize;

        public enum FileKind { Board = 0, Moves = 1 }

        // ───────────────────────── header ─────────────────────────

        internal static void WriteHeader(BitWriter w, FileKind kind)
        {
            var t = SaveTables.Current;
            w.Write(Version, VersionBits);
            w.Write((ulong)kind, 1);
            w.Write(!t.PowerUps);
            w.Write(t.Fingerprint, FingerprintBits);
        }

        /// <summary>Checks the header against the rules that are loaded now. Throws <see cref="SaveFormatException"/>.</summary>
        internal static void ReadHeader(BitReader r, FileKind kind)
        {
            bool classic = ReadHeaderUnchecked(r, kind, out ulong fingerprint);
            var t = SaveTables.Current;
            if (classic == t.PowerUps)
                throw new SaveFormatException(classic ? "The save is a Classic match but power-up rules are loaded"
                                                      : "The save uses power-ups but Classic rules are loaded");
            if (fingerprint != t.Fingerprint)
                throw new SaveFormatException("The save was made with different piece rules (pieces / abilities / conditions .json changed)");
        }

        static bool ReadHeaderUnchecked(BitReader r, FileKind kind, out ulong fingerprint)
        {
            int version = r.ReadInt(VersionBits);
            if (version != Version) throw new SaveFormatException($"Unknown save format version {version}");
            if ((FileKind)r.ReadInt(1) != kind)
                throw new SaveFormatException(kind == FileKind.Board ? "This is a move-history file, not a board file"
                                                                     : "This is a board file, not a move-history file");
            bool classic = r.ReadBit();
            fingerprint = r.Read(FingerprintBits);
            return classic;
        }

        /// <summary>
        /// Reads only the header of a save file: was it a Classic match? Call before loading the rules,
        /// so the right rule set can be loaded first. Doesn't need any rules loaded.
        /// </summary>
        public static bool IsClassic(byte[] file, FileKind kind) => ReadHeaderUnchecked(new BitReader(file), kind, out _);

        /// <summary>Throws unless only the zero padding of the last byte is left.</summary>
        internal static void ExpectEnd(BitReader r)
        {
            if (!r.AtPaddingOnly()) throw new SaveFormatException("Unexpected data at the end of the save");
        }

        // ───────────────────────── tables ─────────────────────────

        /// <summary>
        /// Numbering of the loaded rules, rebuilt whenever another <see cref="RulesData"/> is installed.
        /// Piece types are numbered by name (ordinal sort), so the order of pieces.json doesn't matter.
        /// </summary>
        internal sealed class SaveTables
        {
            static SaveTables _cached;

            public static SaveTables Current
            {
                get
                {
                    var data = PieceLibrary.Data;
                    if (_cached == null || _cached.Data != data) _cached = new SaveTables(data);
                    return _cached;
                }
            }

            public readonly RulesData Data;
            public readonly bool PowerUps;
            public readonly ulong Fingerprint;

            public readonly PieceDefinition[] Types;
            public readonly Dictionary<PieceDefinition, int> TypeIndex = new Dictionary<PieceDefinition, int>();

            // Piece types are written with a Huffman code: frequent types get short codes. Weights = how often each type
            // stands on the board in the start positions (+1, so every type has a code). Today: pawn 1 bit, rook 3,
            // knight/bishop/queen/king/wall 4, archbishop 5, chancellor/paladin 6 (a fixed width would be 4 for all).
            readonly int[] _codeLength;
            readonly ulong[] _code;
            readonly Dictionary<(int length, ulong code), int> _decode = new Dictionary<(int, ulong), int>();

            /// <summary>Wall types only (what can lie under a piece). Usually 1 → 0 bits.</summary>
            public readonly PieceDefinition[] WallTypes;
            public readonly Dictionary<PieceDefinition, int> WallIndex = new Dictionary<PieceDefinition, int>();
            public readonly int WallBits;

            /// <summary>Every "permanent" named condition, by id. One bit each per piece that has state (none today → 0 bits).</summary>
            public readonly Condition[] Permanent;

            SaveTables(RulesData data)
            {
                Data = data;
                PowerUps = data.PowerUpsIncluded;

                var names = new List<string>(data.PiecesByName.Keys);
                names.Sort(StringComparer.Ordinal);
                Types = new PieceDefinition[names.Count];
                var walls = new List<PieceDefinition>();
                for (int i = 0; i < names.Count; i++)
                {
                    Types[i] = data.PiecesByName[names[i]];
                    TypeIndex[Types[i]] = i;
                    if (Types[i].IsWall) { WallIndex[Types[i]] = walls.Count; walls.Add(Types[i]); }
                }
                WallTypes = walls.ToArray();
                _codeLength = HuffmanLengths(TypeWeights(data));
                _code = CanonicalCodes(_codeLength);
                for (int i = 0; i < Types.Length; i++) _decode[(_codeLength[i], _code[i])] = i;
                WallBits = BitWriter.BitsFor((ulong)Math.Max(1, WallTypes.Length));

                var ids = new List<string>();
                foreach (var kv in data.Conditions) if (kv.Value.Permanent) ids.Add(kv.Key);
                ids.Sort(StringComparer.Ordinal);
                Permanent = new Condition[ids.Count];
                for (int i = 0; i < ids.Count; i++) Permanent[i] = data.Conditions[ids[i]];

                Fingerprint = ComputeFingerprint(data);
            }

            public void WriteType(BitWriter w, PieceDefinition def)
            {
                int i = TypeIndex[def];
                w.Write(_code[i], _codeLength[i]);
            }

            public PieceDefinition ReadType(BitReader r)
            {
                if (Types.Length == 1) return Types[0];
                ulong code = 0;
                for (int length = 1; length <= 32; length++)
                {
                    code = (code << 1) | (r.ReadBit() ? 1UL : 0UL);
                    if (_decode.TryGetValue((length, code), out int i)) return Types[i];
                }
                throw new SaveFormatException("Unknown piece type in save");
            }

            int[] TypeWeights(RulesData data)
            {
                var weights = new int[Types.Length];
                for (int i = 0; i < weights.Length; i++) weights[i] = 1;
                foreach (var fen in new[] { Game.StartFen, Game.ClassicFen })
                    foreach (char c in fen.Split(' ')[0])
                        if (data.PiecesBySymbol.TryGetValue(char.ToUpperInvariant(c), out var def)) weights[TypeIndex[def]]++;
                return weights;
            }

            /// <summary>Huffman code lengths; ties are broken by index so every run gives the same codes.</summary>
            static int[] HuffmanLengths(int[] weights)
            {
                int n = weights.Length;
                var lengths = new int[n];
                if (n <= 1) return lengths;

                // Each node: weight, order (for ties), the symbols below it.
                var nodes = new List<(long weight, int order, List<int> symbols)>();
                for (int i = 0; i < n; i++) nodes.Add((weights[i], i, new List<int> { i }));
                int nextOrder = n;
                while (nodes.Count > 1)
                {
                    nodes.Sort((a, b) => a.weight != b.weight ? a.weight.CompareTo(b.weight) : a.order.CompareTo(b.order));
                    var a0 = nodes[0];
                    var b0 = nodes[1];
                    nodes.RemoveRange(0, 2);
                    foreach (var s in a0.symbols) lengths[s]++;
                    foreach (var s in b0.symbols) lengths[s]++;
                    a0.symbols.AddRange(b0.symbols);
                    nodes.Add((a0.weight + b0.weight, nextOrder++, a0.symbols));
                }
                return lengths;
            }

            /// <summary>Canonical Huffman: codes are handed out in order of (length, index).</summary>
            static ulong[] CanonicalCodes(int[] lengths)
            {
                var order = new List<int>();
                for (int i = 0; i < lengths.Length; i++) order.Add(i);
                order.Sort((a, b) => lengths[a] != lengths[b] ? lengths[a].CompareTo(lengths[b]) : a.CompareTo(b));
                var codes = new ulong[lengths.Length];
                ulong code = 0;
                int prevLength = 0;
                foreach (int i in order)
                {
                    code <<= lengths[i] - prevLength;
                    codes[i] = code++;
                    prevLength = lengths[i];
                }
                return codes;
            }

            static ulong ComputeFingerprint(RulesData data)
            {
                ulong h = 14695981039346656037UL; // FNV-1a 64
                void Add(string s)
                {
                    foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
                    h ^= 0xFF; h *= 1099511628211UL; // separator
                }
                Add(Canonical(data.PiecesJson));
                Add(Canonical(data.AbilitiesJson));
                Add(Canonical(data.ConditionsJson));
                Add(Game.StartFen);   // the start positions decide the Huffman code of the piece types,
                Add(Game.ClassicFen); // and they're what a move history starting with "standard start" means
                // fold 64 → 16 bits
                return (h ^ (h >> 16) ^ (h >> 32) ^ (h >> 48)) & 0xFFFF;
            }

            /// <summary>Same JSON without formatting, so re-indenting a file doesn't invalidate saves.</summary>
            static string Canonical(string json)
            {
                if (string.IsNullOrEmpty(json)) return "";
                try { return JToken.Parse(json).ToString(Formatting.None); }
                catch (JsonException) { return json; }
            }
        }
    }
}

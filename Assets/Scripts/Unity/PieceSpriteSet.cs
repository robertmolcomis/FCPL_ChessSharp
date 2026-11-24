using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chess.Unity
{
    /// <summary>
    /// Maps piece symbol + colour to a PNG sprite. Create via Assets ▸ Create ▸ ChessSharp ▸ Piece Sprite Set.
    /// Any missing sprite falls back to a generated placeholder (coloured disc + letter).
    /// </summary>
    [CreateAssetMenu(menuName = "ChessSharp/Piece Sprite Set", fileName = "PieceSpriteSet")]
    public class PieceSpriteSet : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Piece letter: P N B R Q K (A, C for fairy pieces)")]
            public char symbol;
            public Sprite white;
            public Sprite black;
            [Tooltip("Optional. Shown instead while the piece is berserk (pawns).")]
            public Sprite berserkWhite;
            [Tooltip("Optional. Shown instead while the piece is berserk (pawns).")]
            public Sprite berserkBlack;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>The sprite for a piece; the berserk variant if asked for and present, else the normal one.</summary>
        public Sprite Get(char symbol, bool white, bool berserk = false)
        {
            symbol = char.ToUpperInvariant(symbol);
            foreach (var e in entries)
                if (char.ToUpperInvariant(e.symbol) == symbol)
                {
                    var rage = berserk ? (white ? e.berserkWhite : e.berserkBlack) : null;
                    return rage != null ? rage : white ? e.white : e.black;
                }
            return null;
        }
    }
}

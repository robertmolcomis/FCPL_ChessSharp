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
        }

        public List<Entry> entries = new List<Entry>();

        public Sprite Get(char symbol, bool white)
        {
            symbol = char.ToUpperInvariant(symbol);
            foreach (var e in entries)
                if (char.ToUpperInvariant(e.symbol) == symbol)
                    return white ? e.white : e.black;
            return null;
        }
    }
}

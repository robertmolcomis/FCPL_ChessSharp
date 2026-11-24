using System.Collections.Generic;
using UnityEngine;

namespace Chess.Unity
{
    /// <summary>The piece art styles that ship with the game.</summary>
    public enum PieceSkin
    {
        Traditional,
        Dino,
    }

    /// <summary>
    /// Loads the <see cref="PieceSpriteSet"/> of a <see cref="PieceSkin"/>.
    /// Each skin is an asset at Assets/Resources/PieceSkins/&lt;SkinName&gt;.asset that points at the PNGs in
    /// Assets/Sprites/Pieces/&lt;SkinName&gt;/. To add a skin: add its PNGs, create a Piece Sprite Set asset with the
    /// same name as a new enum value, and put it in Resources/PieceSkins.
    /// </summary>
    public static class PieceSkins
    {
        const string Folder = "PieceSkins/";
        static readonly Dictionary<PieceSkin, PieceSpriteSet> Cache = new Dictionary<PieceSkin, PieceSpriteSet>();

        /// <summary>Domain reload may be off ("Enter Play Mode Options"), so drop cached assets at the start of every run.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => Cache.Clear();

        /// <summary>The sprite set of <paramref name="skin"/>, or null (with a warning) if its asset is missing.</summary>
        public static PieceSpriteSet Load(PieceSkin skin)
        {
            if (Cache.TryGetValue(skin, out var set) && set != null) return set;
            set = Resources.Load<PieceSpriteSet>(Folder + skin);
            if (set == null)
                Debug.LogWarning($"Piece skin '{skin}' not found at Resources/{Folder}{skin}. Using placeholder pieces.");
            Cache[skin] = set;
            return set;
        }
    }
}

using System;
using System.Linq;
using UnityEngine;

namespace Chess.Unity
{
    /// <summary>
    /// The piece art styles: every <see cref="PieceSpriteSet"/> asset in Assets/Resources/PieceSkins/, found at runtime,
    /// so a new skin needs no code. To add one, drop its PNGs in Assets/Sprites/Pieces/&lt;SkinName&gt;/White and /Black
    /// named &lt;SkinName&gt;_White_King.png etc.: the editor (PieceSkinImporter) builds the asset for you.
    /// The chosen skin is saved in PlayerPrefs.
    /// </summary>
    public static class PieceSkins
    {
        const string Folder = "PieceSkins";
        const string PrefKey = "PieceSkin";
        const string Default = "Traditional";

        static PieceSpriteSet[] _all;

        /// <summary>Domain reload may be off ("Enter Play Mode Options"), so drop cached assets at the start of every run.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => _all = null;

        /// <summary>Every skin, sorted by name (the asset's file name).</summary>
        public static PieceSpriteSet[] All =>
            _all ??= Resources.LoadAll<PieceSpriteSet>(Folder).OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase).ToArray();

        /// <summary>The saved skin; falls back to Traditional, then the first one, then null (placeholder pieces).</summary>
        public static PieceSpriteSet Current
        {
            get
            {
                var all = All;
                string name = PlayerPrefs.GetString(PrefKey, Default);
                return all.FirstOrDefault(s => s.name == name) ?? all.FirstOrDefault(s => s.name == Default) ?? all.FirstOrDefault();
            }
            set
            {
                if (value == null) return;
                PlayerPrefs.SetString(PrefKey, value.name);
                PlayerPrefs.Save();
            }
        }
    }
}

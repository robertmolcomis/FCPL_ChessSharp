#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chess.Unity;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns every folder in Assets/Sprites/Pieces/ into a skin, with no code changes:
/// Assets/Sprites/Pieces/&lt;Skin&gt;/White/&lt;Skin&gt;_White_King.png (and Black/…) →
/// Assets/Resources/PieceSkins/&lt;Skin&gt;.asset, which the game finds at runtime (PieceSkins.All).
/// Runs by itself whenever a PNG there is added, changed, moved or deleted; also Chess ▸ Rebuild Piece Skins.
/// The piece is the last "_" part of the file name (King, Queen, Rook, Bishop, Knight, Pawn, BerserkPawn,
/// Archbishop, Chancellor, Wall); the colour is the parent folder. Missing PNGs fall back to placeholder pieces.
/// </summary>
public class PieceSkinImporter : AssetPostprocessor
{
    const string SpritesRoot = "Assets/Sprites/Pieces";
    const string SkinsFolder = "Assets/Resources/PieceSkins";

    // Same order as the hand-made assets. A new piece type needs a line here.
    static readonly (string name, char symbol)[] Pieces =
    {
        ("Pawn", 'P'), ("Knight", 'N'), ("Bishop", 'B'), ("Rook", 'R'), ("Queen", 'Q'), ("King", 'K'),
        ("Archbishop", 'A'), ("Chancellor", 'C'), ("Wall", 'W'),
    };

    static bool InSprites(string path) => path.Replace('\\', '/').StartsWith(SpritesRoot + "/");

    // New PNGs only (existing import settings are left alone): pixel-art sprite, like the shipped ones.
    void OnPreprocessTexture()
    {
        if (!InSprites(assetPath) || !assetImporter.importSettingsMissing) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (!imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(InSprites)) return;
        EditorApplication.delayCall += RebuildAll; // after the import finishes, so the new sprites can be loaded
    }

    [MenuItem("Chess/Rebuild Piece Skins")]
    static void RebuildAll()
    {
        EditorApplication.delayCall -= RebuildAll; // several imports in a row → one rebuild
        if (!AssetDatabase.IsValidFolder(SpritesRoot)) return;
        foreach (var dir in AssetDatabase.GetSubFolders(SpritesRoot)) Rebuild(dir);
        AssetDatabase.SaveAssets();
    }

    static void Rebuild(string skinDir)
    {
        string skin = Path.GetFileName(skinDir);
        var sprites = new Dictionary<(string piece, bool white), Sprite>();
        foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { skinDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string colour = Path.GetFileName(Path.GetDirectoryName(path));
            if (colour != "White" && colour != "Black") continue;
            string piece = Path.GetFileNameWithoutExtension(path).Split('_').Last();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) sprites[(piece, colour == "White")] = sprite;
        }
        if (sprites.Count == 0) return; // empty or not-yet-imported folder: don't make a blank skin

        Sprite Get(string piece, bool white) => sprites.TryGetValue((piece, white), out var s) ? s : null;
        var entries = new List<PieceSpriteSet.Entry>();
        foreach (var (name, symbol) in Pieces)
        {
            var e = new PieceSpriteSet.Entry { symbol = symbol, white = Get(name, true), black = Get(name, false) };
            if (symbol == 'P') { e.berserkWhite = Get("BerserkPawn", true); e.berserkBlack = Get("BerserkPawn", false); }
            if (e.white != null || e.black != null) entries.Add(e);
        }

        // Update in place when the asset exists, so its GUID (and any Inspector reference to it) survives.
        string assetPath = $"{SkinsFolder}/{skin}.asset";
        var set = AssetDatabase.LoadAssetAtPath<PieceSpriteSet>(assetPath);
        if (set == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(SkinsFolder)) AssetDatabase.CreateFolder("Assets/Resources", "PieceSkins");
            set = ScriptableObject.CreateInstance<PieceSpriteSet>();
            set.entries = entries;
            AssetDatabase.CreateAsset(set, assetPath);
            Debug.Log($"[Chess] New piece skin '{skin}' → {assetPath}");
        }
        else
        {
            set.entries = entries;
            EditorUtility.SetDirty(set);
        }
    }
}
#endif

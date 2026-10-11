using System;
using UnityEngine;

namespace Chess.Unity
{
    /// <summary>Generates placeholder sprites at runtime so the game runs before any art exists.</summary>
    public static class SpriteFactory
    {
        const int Res = 64;
        static Sprite _square, _circle, _ring, _corners, _glow, _drop, _bricks;

        /// <summary>
        /// With "Enter Play Mode Options" (domain reload disabled), statics survive between play sessions but the
        /// sprites they point to are destroyed. Clear the cache at the start of every run.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => _square = _circle = _ring = _corners = _glow = _drop = _bricks = null;

        public static Sprite Square => Get(ref _square, (x, y) => 1f);

        public static Sprite Circle => Get(ref _circle, (x, y) =>
        {
            float d = Dist(x, y);
            return Mathf.Clamp01((0.5f - d) * Res);
        });

        public static Sprite Ring => Get(ref _ring, (x, y) =>
        {
            float d = Dist(x, y);
            return Mathf.Clamp01((0.5f - d) * Res) * Mathf.Clamp01((d - 0.40f) * Res);
        });

        /// <summary>Soft radial gradient — "king in check" marker.</summary>
        public static Sprite Glow => Get(ref _glow, (x, y) =>
        {
            float d = Dist(x, y) * 2f; // 0 centre → 1 edge
            return Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
        });

        /// <summary>Four corner triangles — classic "capture target" marker.</summary>
        public static Sprite Corners => Get(ref _corners, (x, y) =>
        {
            float u = (x + 0.5f) / Res, v = (y + 0.5f) / Res;
            float cu = Mathf.Min(u, 1 - u), cv = Mathf.Min(v, 1 - v);
            return cu + cv < 0.22f ? 1f : 0f;
        });

        /// <summary>Brick pattern (bricks opaque, mortar + a thin border transparent) — the wall piece.</summary>
        public static Sprite Bricks => Get(ref _bricks, (x, y) =>
        {
            const int border = 3, rowH = 15, brickW = 29, mortar = 2;
            if (x < border || y < border || x >= Res - border || y >= Res - border) return 0f;
            int yy = y - border, row = yy / rowH;
            if (yy % rowH < mortar) return 0f;                                  // horizontal mortar
            int xx = x - border + (row % 2 == 1 ? brickW / 2 : 0);              // stagger every other row
            return xx % brickW < mortar ? 0f : 1f;                              // vertical joints
        });

        /// <summary>Blood drop (round bottom, pointed top) — "pawn has a kill" marker.</summary>
        public static Sprite Drop => Get(ref _drop, (x, y) =>
        {
            float u = (x + 0.5f) / Res - 0.5f, v = (y + 0.5f) / Res;
            const float cy = 0.36f, r = 0.30f, tip = 0.97f;
            float circle = (r - Mathf.Sqrt(u * u + (v - cy) * (v - cy))) * Res;
            // above the centre, the half-width shrinks linearly to 0 at the tip
            float cone = v >= cy && v <= tip ? (r * (tip - v) / (tip - cy) - Mathf.Abs(u)) * Res : -1f;
            return Mathf.Clamp01(Mathf.Max(circle, cone));
        });

        /// <summary>
        /// Uses Unity's overloaded == so a destroyed sprite counts as null and is regenerated.
        /// (Don't use ??= with UnityEngine.Object — it ignores destruction.)
        /// </summary>
        static Sprite Get(ref Sprite cache, Func<int, int, float> alpha)
        {
            // also regenerate if only the texture was unloaded (e.g. by UnloadUnusedAssets)
            if (cache == null || cache.texture == null) cache = Make(alpha);
            return cache;
        }

        static float Dist(int x, int y)
        {
            float u = (x + 0.5f) / Res - 0.5f, v = (y + 0.5f) / Res - 0.5f;
            return Mathf.Sqrt(u * u + v * v);
        }

        static Sprite Make(Func<int, int, float> alpha)
        {
            var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[Res * Res];
            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                    px[y * Res + x] = new Color(1, 1, 1, alpha(x, y));
            tex.SetPixels32(px);
            tex.Apply();
            // pixelsPerUnit = Res → sprite is exactly 1 world unit wide.
            return Sprite.Create(tex, new Rect(0, 0, Res, Res), new Vector2(0.5f, 0.5f), Res);
        }
    }
}

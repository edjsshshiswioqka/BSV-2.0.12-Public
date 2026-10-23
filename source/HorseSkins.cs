using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;

namespace BetterStardewValley;

/// <summary>
/// Horse coats. Every skin is a recolour rule applied at runtime to the player's own installed
/// "Animals/horse" texture (no game art is bundled). The saddle keeps its native colours. If another
/// mod replaced the horse texture with a different palette, the rules find nothing to recolour and the
/// other mod's texture is shown unchanged.
/// </summary>
internal static class HorseSkins
{
    internal const string SkinKey = "Eddie.BetterStardewValley/HorseSkin";
    internal const string AssetPrefix = "Mods/Eddie.BetterStardewValley/HorseSkin/";
    internal const string Original = "original";

    private static Color C(int r, int g, int b) => new(r, g, b, 255);
    private static Color[] R(params Color[] c) => c;
    // Native palette (light -> dark coat ramp, mane, outline, saddle straps).
    private static readonly Color Out = C(81, 32, 0), Mane = C(99, 40, 22), ManeLight = C(142, 75, 39);
    private static readonly Color[] Coat = { C(249, 201, 144), C(236, 173, 97), C(198, 133, 53), C(163, 99, 19), C(127, 71, 3) };
    private static readonly Color[] Strap = { C(112, 83, 31), C(142, 120, 39), C(153, 72, 5) };

    internal readonly struct Ctx
    {
        internal readonly int X, Y, Row, Column, Level, Top;
        internal bool Side => Row is 1 or 3;
        internal Ctx(int x, int y, int row, int column, int level, int top) { X = x; Y = y; Row = row; Column = column; Level = level; Top = top; }
    }
    internal sealed class Skin
    {
        internal string Id = "";
        internal Color[] Coat = HorseSkins.Coat;
        internal Color Mane = HorseSkins.Mane, ManeLight = HorseSkins.ManeLight;
        internal Color? Outline;
        internal Func<Ctx, int>? Pattern;            // index into Alternates, or -1
        internal Color[][] Alternates = Array.Empty<Color[]>();
        internal bool Rainbow;
    }

    // ---- deterministic noise (identical to the approved sample generator) ----
    private static double Hash(int x, int y, int s)
    {
        uint n = unchecked((uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 982451653u);
        n = unchecked((n ^ (n >> 13)) * 1274126177u);
        return (n ^ (n >> 16)) / (double)uint.MaxValue;
    }
    private static double Noise(double x, double y, int s, double scale)
    {
        double gx = x / scale, gy = y / scale; int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
        double fx = gx - x0, fy = gy - y0; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        double a = Hash(x0, y0, s) * (1 - fx) + Hash(x0 + 1, y0, s) * fx, b = Hash(x0, y0 + 1, s) * (1 - fx) + Hash(x0 + 1, y0 + 1, s) * fx;
        return a * (1 - fy) + b * fy;
    }

    private static readonly Color[] White = R(C(255, 255, 250), C(244, 242, 236), C(222, 220, 214), C(190, 188, 184), C(150, 148, 146));
    private static readonly Color[] Snow = R(C(255, 255, 255), C(250, 250, 252), C(236, 238, 244), C(214, 218, 228), C(186, 190, 204));
    private static readonly Color[] BlackCoat = R(C(118, 112, 124), C(80, 76, 88), C(56, 54, 64), C(40, 38, 46), C(28, 26, 32));
    private static readonly Color[] Chestnut = R(C(247, 178, 128), C(226, 132, 80), C(196, 96, 50), C(158, 66, 30), C(118, 44, 20));
    private static readonly Color[] Bay = R(C(214, 150, 104), C(176, 104, 62), C(140, 74, 40), C(108, 52, 28), C(80, 36, 20));
    private static readonly Color[] DarkLeg = R(C(90, 70, 60), C(60, 46, 40), C(44, 34, 30), C(34, 26, 22), C(26, 20, 18));
    private static bool Socks(Ctx c) => c.Y >= 25;
    private static bool Legs(Ctx c) => c.Side ? c.Y >= 22 : c.Y >= 23;
    private static bool Blaze(Ctx c) => c.Side ? c.X >= 27 && c.Y >= 7 && c.Y <= 15 && c.Column < 7 : c.Row == 0 && c.X >= 14 && c.X <= 17 && c.Y <= 13;
    private static bool Dorsal(Ctx c) => c.Side && c.X >= 5 && c.X <= 22 && c.Y <= c.Top + 1 || c.Row == 2 && c.X >= 15 && c.X <= 16;
    private static int Roan(Ctx c) => Hash(c.X, c.Y, c.Row * 3 + 1) > .62 && c.Level >= 1 && !(c.Side && c.X >= 24) ? 0 : -1;
    private static int Spots(int x, int y, int seed, double chance)
    {
        int cx = x / 3, cy = y / 3;
        return Hash(cx, cy, seed) > chance && (cx * 3 + 1 - x) * (cx * 3 + 1 - x) + (cy * 3 + 1 - y) * (cy * 3 + 1 - y) <= 1 ? 0 : -1;
    }
    private static int Appaloosa(Ctx c)
    {
        bool blanket = c.Side && c.X < 15 && c.Y < 22 || c.Row == 2;
        if (!blanket) return -1;
        int cx = (c.X + 1) / 3, cy = c.Y / 3;
        return Hash(cx, cy, 5) > .55 && (cx * 3 + 1 - c.X) * (cx * 3 + 1 - c.X) + (cy * 3 + 1 - c.Y) * (cy * 3 + 1 - c.Y) <= 1 ? 1 : 0;
    }
    private static Func<Ctx, int> Stripes(int period, int width, int slant) => c =>
    {
        if (c.Side && c.X >= 24 && c.Y <= 16) return -1; // keep the face plain
        int v = c.Side ? (c.X + slant * c.Y) % period : c.Y % period;
        return v < width ? 0 : -1;
    };

    internal static readonly List<Skin> All = Build();
    private static List<Skin> Build()
    {
        var list = new List<Skin>
        {
            new() { Id = Original },
            new() { Id = "chestnut", Coat = Chestnut, Mane = C(150, 58, 26), ManeLight = C(196, 96, 50) },
            new() { Id = "bay", Coat = Bay, Mane = C(34, 24, 22), ManeLight = C(70, 52, 44), Outline = C(40, 22, 14), Pattern = c => Legs(c) ? 0 : -1, Alternates = new[] { DarkLeg } },
            new() { Id = "black", Coat = BlackCoat, Mane = C(18, 16, 20), ManeLight = C(52, 48, 58), Outline = C(14, 12, 16) },
            new() { Id = "palomino", Coat = R(C(255, 236, 170), C(246, 206, 120), C(222, 172, 78), C(186, 134, 48), C(148, 100, 30)), Mane = C(248, 244, 226), ManeLight = C(255, 255, 246), Outline = C(110, 62, 16) },
            new() { Id = "grey", Coat = R(C(252, 252, 252), C(228, 230, 234), C(200, 204, 210), C(162, 166, 176), C(124, 128, 140)), Mane = C(206, 208, 214), ManeLight = C(246, 246, 248), Outline = C(70, 68, 76) },
            new() { Id = "snow", Coat = Snow, Mane = C(236, 240, 248), ManeLight = C(255, 255, 255), Outline = C(120, 126, 146) },
            new() { Id = "socks", Coat = Chestnut, Mane = C(120, 46, 20), ManeLight = C(170, 80, 40), Pattern = c => Socks(c) || Blaze(c) ? 0 : -1, Alternates = new[] { White } },
            new() { Id = "black_socks", Coat = BlackCoat, Mane = C(18, 16, 20), ManeLight = C(52, 48, 58), Outline = C(14, 12, 16), Pattern = c => Socks(c) || Blaze(c) ? 0 : -1, Alternates = new[] { White } },
            new() { Id = "buckskin", Coat = R(C(255, 226, 160), C(236, 196, 120), C(206, 160, 86), C(168, 124, 60), C(130, 92, 42)), Mane = C(30, 22, 18), ManeLight = C(64, 48, 38), Outline = C(60, 38, 18), Pattern = c => Legs(c) ? 0 : -1, Alternates = new[] { DarkLeg } },
            new() { Id = "dun", Coat = R(C(240, 214, 164), C(214, 182, 128), C(184, 150, 98), C(150, 118, 74), C(116, 88, 54)), Mane = C(70, 48, 32), ManeLight = C(110, 80, 56), Pattern = c => Dorsal(c) || Legs(c) ? 0 : -1, Alternates = new[] { R(C(120, 88, 60), C(100, 72, 48), C(86, 60, 40), C(72, 50, 32), C(58, 40, 26)) } },
            new() { Id = "strawberry_roan", Coat = Chestnut, Mane = C(140, 54, 26), ManeLight = C(186, 90, 48), Pattern = Roan, Alternates = new[] { R(C(255, 240, 232), C(246, 214, 200), C(230, 188, 172), C(206, 160, 146), C(176, 130, 118)) } },
            new() { Id = "blue_roan", Coat = BlackCoat, Mane = C(18, 16, 20), ManeLight = C(52, 48, 58), Outline = C(14, 12, 16), Pattern = Roan, Alternates = new[] { R(C(236, 240, 248), C(206, 212, 226), C(176, 184, 202), C(146, 154, 174), C(118, 126, 148)) } },
            new() { Id = "appaloosa", Coat = Bay, Mane = C(52, 34, 26), ManeLight = C(96, 66, 48), Outline = C(60, 34, 18), Pattern = Appaloosa, Alternates = new[] { White, R(C(112, 72, 48), C(98, 60, 40), C(84, 50, 32), C(70, 40, 26), C(56, 32, 20)) } },
            new() { Id = "leopard", Coat = White, Mane = C(60, 48, 44), ManeLight = C(110, 96, 90), Outline = C(66, 54, 48), Pattern = c => Spots(c.X + c.Row, c.Y, 9, .45), Alternates = new[] { R(C(70, 56, 52), C(60, 48, 44), C(52, 40, 36), C(44, 34, 30), C(36, 28, 24)) } },
            new() { Id = "pinto", Coat = R(C(236, 173, 97), C(214, 140, 70), C(184, 108, 46), C(150, 82, 30), C(116, 60, 22)), Mane = C(60, 36, 24), ManeLight = C(110, 72, 46), Pattern = c => Noise(c.X + 3 * c.Row, c.Y, 11, 6) > .55 ? 0 : -1, Alternates = new[] { White } },
            new() { Id = "piebald", Coat = BlackCoat, Mane = C(22, 20, 24), ManeLight = C(240, 240, 236), Outline = C(18, 16, 20), Pattern = c => Noise(c.X + 5 * c.Row, c.Y + 2, 23, 5) > .5 ? 0 : -1, Alternates = new[] { White } },
            new() { Id = "dapple", Coat = R(C(214, 218, 226), C(170, 176, 188), C(130, 136, 150), C(100, 106, 120), C(74, 78, 92)), Mane = C(44, 44, 52), ManeLight = C(90, 92, 104), Outline = C(36, 36, 44), Pattern = c => Noise(c.X + c.Row * 9, c.Y, 31, 2.2) > .62 && c.Level >= 2 ? 0 : -1, Alternates = new[] { R(C(236, 238, 242), C(214, 218, 226), C(196, 200, 210), C(170, 176, 188), C(140, 146, 160)) } },
            new() { Id = "silver_dapple", Coat = R(C(176, 120, 96), C(140, 90, 70), C(110, 66, 50), C(86, 50, 38), C(64, 36, 28)), Mane = C(222, 214, 200), ManeLight = C(250, 246, 236), Outline = C(44, 26, 20) },
            new() { Id = "tiger", Coat = R(C(255, 196, 120), C(244, 160, 70), C(220, 124, 40), C(184, 92, 24), C(146, 66, 16)), Mane = C(40, 26, 18), ManeLight = C(80, 52, 32), Outline = C(60, 30, 10), Pattern = Stripes(5, 2, 1), Alternates = new[] { R(C(80, 44, 22), C(64, 34, 16), C(52, 28, 14), C(44, 24, 12), C(36, 20, 10)) } },
            new() { Id = "zebra", Coat = White, Mane = C(24, 22, 24), ManeLight = C(60, 58, 62), Outline = C(20, 18, 22), Pattern = Stripes(4, 2, 0), Alternates = new[] { R(C(60, 58, 62), C(44, 42, 46), C(34, 32, 36), C(28, 26, 30), C(22, 20, 24)) } },
            new() { Id = "starry", Coat = R(C(92, 104, 170), C(62, 72, 140), C(44, 52, 110), C(32, 38, 86), C(24, 28, 66)), Mane = C(150, 200, 255), ManeLight = C(220, 240, 255), Outline = C(16, 18, 44), Pattern = c => Hash(c.X, c.Y, c.Row + 40) > .93 ? 0 : -1, Alternates = new[] { R(C(255, 250, 210), C(255, 240, 170), C(250, 226, 140), C(230, 206, 120), C(200, 180, 100)) } },
            new() { Id = "sakura", Coat = R(C(255, 240, 246), C(252, 214, 228), C(240, 184, 206), C(214, 150, 180), C(180, 118, 150)), Mane = C(236, 120, 160), ManeLight = C(255, 178, 206), Outline = C(120, 60, 90) },
            new() { Id = "dark_brown", Coat = R(C(150, 100, 74), C(112, 70, 48), C(84, 50, 34), C(64, 38, 26), C(46, 28, 20)), Mane = C(24, 16, 14), ManeLight = C(56, 40, 32), Outline = C(26, 14, 10) },
            new() { Id = "red_hare", Coat = R(C(255, 150, 110), C(236, 80, 50), C(200, 40, 28), C(160, 22, 18), C(118, 14, 12)), Mane = C(70, 10, 12), ManeLight = C(130, 24, 24), Outline = C(60, 8, 8) },
            new() { Id = "flame", Coat = R(C(255, 170, 90), C(244, 96, 48), C(214, 52, 30), C(170, 28, 22), C(124, 16, 16)), Mane = C(255, 190, 40), ManeLight = C(255, 240, 140), Outline = C(90, 14, 10), Pattern = c => c.Y >= 24 ? 0 : -1, Alternates = new[] { R(C(255, 236, 140), C(255, 200, 70), C(250, 160, 40), C(230, 120, 30), C(200, 84, 24)) } },
            new() { Id = "mint", Coat = R(C(236, 255, 246), C(196, 240, 220), C(150, 218, 190), C(110, 186, 160), C(80, 150, 130)), Mane = C(40, 150, 140), ManeLight = C(110, 210, 196), Outline = C(36, 90, 80) },
            new() { Id = "lavender", Coat = R(C(250, 240, 255), C(226, 206, 246), C(196, 170, 230), C(160, 132, 204), C(124, 98, 170)), Mane = C(110, 70, 170), ManeLight = C(186, 150, 236), Outline = C(70, 44, 110) },
            new() { Id = "ice", Coat = R(C(246, 252, 255), C(206, 232, 252), C(164, 206, 242), C(124, 170, 222), C(90, 132, 196)), Mane = C(236, 250, 255), ManeLight = C(255, 255, 255), Outline = C(50, 90, 140), Pattern = c => Hash(c.X, c.Y, c.Row + 77) > .9 ? 0 : -1, Alternates = new[] { R(C(255, 255, 255), C(255, 255, 255), C(236, 248, 255), C(214, 236, 255), C(190, 220, 250)) } },
            new() { Id = "rainbow", Coat = R(C(255, 255, 255), C(250, 246, 255), C(238, 230, 250), C(214, 204, 240), C(186, 176, 224)), Outline = C(110, 96, 150), Rainbow = true },
        };
        return list;
    }
    private static readonly Dictionary<string, Skin> ById = All.ToDictionary(s => s.Id, StringComparer.Ordinal);
    internal static Skin? Find(string? id) => id == null ? null : ById.GetValueOrDefault(id == "unicorn" ? "rainbow" : id);
    internal static string AssetName(string id) => AssetPrefix + id;

    private static readonly Color[] RainbowBands = { C(232, 64, 72), C(250, 150, 50), C(250, 220, 70), C(90, 200, 110), C(80, 150, 240), C(160, 100, 220) };
    private static Color Lighten(Color c, float f) => new((int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f), 255);

    /// <summary>Recolours a copy of the native horse sheet (224 x 128, 32 x 32 frames). Returns null when the
    /// sheet is not the native layout/palette (another mod's texture): the caller then keeps that texture.</summary>
    internal static Color[]? Apply(Color[] source, int width, int height, Skin skin)
    {
        if (skin.Id == Original) return null;
        if (width % 32 != 0 || height % 32 != 0 || width < 224 || height < 128) return null;
        Color Px(int x, int y) => source[y * width + x];
        int recognised = source.Count(c => c.A == 255 && Array.IndexOf(Coat, c) >= 0);
        if (recognised < 1000) return null;
        var output = (Color[])source.Clone();
        for (int fy = 0; fy < 128; fy += 32)
            for (int fx = 0; fx < 224; fx += 32)
            {
                int row = fy / 32, column = fx / 32;
                var saddle = SaddleMask(Px, fx, fy);
                var top = new int[32];
                for (int x = 0; x < 32; x++) { top[x] = 99; for (int y = 0; y < 32; y++) if (Px(fx + x, fy + y).A == 255) { top[x] = y; break; } }
                for (int x = 0; x < 32; x++)
                    for (int y = 0; y < 32; y++)
                    {
                        var p = Px(fx + x, fy + y);
                        if (p.A != 255) continue;
                        int i = (fy + y) * width + fx + x;
                        if (saddle.Contains((x, y)))
                        {
                            // The saddle keeps its colours; only a unicorn's mane showing through stays rainbow.
                            if (skin.Rainbow && (p == Mane || p == ManeLight)) { var band = RainbowBands[(x + y) / 2 % RainbowBands.Length]; output[i] = p == ManeLight ? Lighten(band, .35f) : band; }
                            continue;
                        }
                        int level = Array.IndexOf(Coat, p);
                        if (level >= 0)
                        {
                            var ramp = skin.Coat;
                            if (skin.Pattern != null)
                            {
                                int alt = skin.Pattern(new Ctx(x, y, row, column, level, top[x]));
                                if (alt >= 0 && alt < skin.Alternates.Length) ramp = skin.Alternates[alt];
                            }
                            output[i] = ramp[level];
                        }
                        else if (p == Mane || p == ManeLight)
                        {
                            if (skin.Rainbow)
                            {
                                var band = RainbowBands[(x + y) / 2 % RainbowBands.Length];
                                output[i] = p == ManeLight ? Lighten(band, .35f) : band;
                            }
                            else output[i] = p == Mane ? skin.Mane : skin.ManeLight;
                        }
                        else if (p == Out && skin.Outline is { } outline) output[i] = outline;
                    }
            }
        return output;
    }

    private static HashSet<(int, int)> SaddleMask(Func<int, int, Color> px, int fx, int fy)
    {
        var straps = new List<(int X, int Y)>();
        for (int x = 0; x < 32; x++) for (int y = 0; y < 32; y++) { var c = px(fx + x, fy + y); if (c.A == 255 && Array.IndexOf(Strap, c) >= 0) straps.Add((x, y)); }
        var mask = new HashSet<(int, int)>();
        if (straps.Count == 0) return mask;
        int x0 = straps.Min(p => p.X), x1 = straps.Max(p => p.X), y0 = straps.Min(p => p.Y);
        for (int x = x0; x <= x1; x++)
        {
            var ys = straps.Where(p => p.X == x).Select(p => p.Y).ToList();
            if (ys.Count == 0) continue;
            for (int y = y0 - 2; y <= ys.Max(); y++) mask.Add((x, y));
        }
        return mask;
    }

    // ---- assets ----
    private static IModHelper? Helper;
    private static IMonitor? Monitor;
    internal static void Register(IModHelper helper, IMonitor monitor)
    {
        Helper = helper; Monitor = monitor;
        helper.Events.Content.AssetRequested += OnAssetRequested;
        // Another mod changing the base horse texture: rebuild the coats from the new texture.
        helper.Events.Content.AssetsInvalidated += (_, e) =>
        {
            if (e.NamesWithoutLocale.Any(n => n.IsEquivalentTo("Animals/horse")))
                helper.GameContent.InvalidateCache(a => a.NameWithoutLocale.StartsWith(AssetPrefix, allowPartialWord: true, allowSubfolder: true));
        };
    }
    private static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.StartsWith(AssetPrefix, allowPartialWord: true, allowSubfolder: true)) return;
        string id = e.NameWithoutLocale.BaseName.Substring(AssetPrefix.Length);
        var skin = Find(id);
        if (skin == null) return;
        e.LoadFrom(() => Texture(skin), AssetLoadPriority.Exclusive);
    }
    private static readonly HashSet<string> Warned = new();
    internal static Texture2D Texture(Skin skin)
    {
        var baseTexture = Helper!.GameContent.Load<Texture2D>("Animals/horse");
        var data = new Color[baseTexture.Width * baseTexture.Height];
        baseTexture.GetData(data);
        var recoloured = Apply(data, baseTexture.Width, baseTexture.Height, skin);
        if (recoloured == null && skin.Id != Original && Warned.Add("palette"))
            Monitor?.Log("BSV horse coats: the horse texture is not the native palette (another mod may replace it); coats keep that texture.", LogLevel.Debug);
        var texture = new Texture2D(Game1.graphics.GraphicsDevice, baseTexture.Width, baseTexture.Height);
        texture.SetData(recoloured ?? data);
        return texture;
    }

    /// <summary>Local, unsynced texture override from the synced modData choice (every client builds its own).</summary>
    internal static void ApplyTo(Horse horse)
    {
        if (!horse.modData.TryGetValue(SkinKey, out var id) || Find(id) is not { } skin || skin.Id == Original) return;
        string asset = AssetName(skin.Id);
        var sprite = horse.Sprite;
        if (sprite == null || Applied.TryGetValue(sprite, out var current) && current.Value == asset) return;
        try { sprite.LoadTexture(asset, syncTextureName: false); Applied.AddOrUpdate(sprite, new Box(asset)); }
        catch (Exception ex) { if (Warned.Add("apply")) Monitor?.Log("BSV horse coat could not be applied: " + ex.Message, LogLevel.Warn); }
    }
    private sealed class Box { internal readonly string Value; internal Box(string v) { Value = v; } }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AnimatedSprite, Box> Applied = new();
}

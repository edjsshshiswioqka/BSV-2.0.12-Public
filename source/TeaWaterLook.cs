using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace BetterStardewValley;

/// <summary>
/// A tea bush watered today draws with slightly darker leaves (x0.80), like watered soil. The darker sheet is
/// made at runtime from the game's own bush texture (nothing is shipped); trunk and shadow pixels are unchanged.
/// </summary>
internal sealed partial class TeaFeature
{
    private const float WateredShade = .80f;
    private static Texture2D? DarkSource, DarkSheet;

    private static void InstallWaterLook(Harmony h, IModHelper helper, IMonitor monitor)
    {
        try
        {
            h.Patch(AccessTools.Method(typeof(Bush), nameof(Bush.draw), new[] { typeof(SpriteBatch) }),
                transpiler: new HarmonyMethod(typeof(TeaFeature), nameof(DrawIL)));
            helper.Events.Content.AssetsInvalidated += (_, e) =>
            { if (e.NamesWithoutLocale.Any(n => n.IsEquivalentTo("TileSheets/bushes"))) DarkSource = null; };
        }
        catch (Exception ex) { monitor.Log("BSV tea: the watered look is unavailable (another mod changed bush drawing); tea itself works normally. " + ex.Message, LogLevel.Warn); }
    }
    private static IEnumerable<CodeInstruction> DrawIL(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        var code = original.Select(c => new CodeInstruction(c)).ToList();
        var field = AccessTools.Field(typeof(Bush), "texture");
        int found = 0;
        for (int i = 0; i < code.Count - 1; i++)
        {
            if (!code[i].LoadsField(field) || code[i + 1].operand is not MethodInfo { Name: "get_Value" }) continue;
            code[i].opcode = OpCodes.Ldarg_0; code[i].operand = null;
            code[i + 1].opcode = OpCodes.Call; code[i + 1].operand = AccessTools.Method(typeof(TeaFeature), nameof(BushTexture));
            found++;
        }
        if (found != 1) { TranspilerGuard.Failed("Bush.draw", $"Bush texture draw not found ({found})."); return original; }
        TranspilerGuard.Succeeded("Bush.draw");
        return code;
    }
    private static Texture2D BushTexture(Bush bush)
    {
        var normal = Bush.texture.Value;
        try
        {
            if (!Managed(bush) || Dead(bush) || Get(bush, TeaAppleRules.WaterKey) != Today) return normal;
            if (!ReferenceEquals(DarkSource, normal) || DarkSheet == null || DarkSheet.IsDisposed)
            {
                var data = new Color[normal.Width * normal.Height];
                normal.GetData(data);
                for (int i = 0; i < data.Length; i++)
                {
                    var c = data[i];
                    if (c.A > 0 && c.G > c.R && c.G > c.B + 10) // leaf greens only; trunk, soil and shadow keep their colour
                        data[i] = new Color((int)(c.R * WateredShade), (int)(c.G * WateredShade), (int)(c.B * WateredShade), c.A);
                }
                DarkSheet?.Dispose();
                DarkSheet = new Texture2D(Game1.graphics.GraphicsDevice, normal.Width, normal.Height);
                DarkSheet.SetData(data);
                DarkSource = normal;
            }
            return DarkSheet;
        }
        catch { return normal; }
    }
}

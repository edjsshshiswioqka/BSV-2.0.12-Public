using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Objects;

namespace BetterStardewValley;

/// <summary>
/// Draws BSV horses 1.15x larger, scaled around the hooves so they stand on the same ground line.
/// Everything Horse.draw paints (body, the head drawn over a rider facing down, hats, carrot-munching
/// heads) goes through the same transform. Collision, mounting and pathing are unchanged.
/// The rider is NOT scaled: it is lifted onto the bigger saddle by the saddle's measured displacement.
/// Tractors and other mods' horse-like characters are never touched.
/// </summary>
internal static class HorseScale
{
    internal const float Factor = 1.15f;
    [ThreadStatic] private static Horse? current;
    [ThreadStatic] private static Vector2 pivot;
    private static IMonitor? Monitor;
    private static bool failed;

    // Saddle top-centre in the native 32x32 frame for each facing (measured from Animals/horse):
    // facing down (frame 0) x16 y10, side (frame 7) x15.5 y12, facing up (frame 14) x16 y9. The pivot is the
    // hoof line centre (16, 32). Lift = (saddle - pivot) * 4 px * (Factor - 1).
    private static Vector2 SaddleLift(int facing) => facing switch
    {
        0 => new Vector2(0, (9 - 32) * 4 * (Factor - 1)),
        2 => new Vector2(0, (10 - 32) * 4 * (Factor - 1)),
        1 => new Vector2((15.5f - 16) * 4 * (Factor - 1), (12 - 32) * 4 * (Factor - 1)),
        _ => new Vector2(-(15.5f - 16) * 4 * (Factor - 1), (12 - 32) * 4 * (Factor - 1)),
    };

    internal static bool Applies(Horse? horse) => horse != null && HorseFeature.ScaleEligible(horse);

    internal static void Install(Harmony harmony, IMonitor monitor)
    {
        Monitor = monitor;
        var draw = AccessTools.Method(typeof(Horse), nameof(Horse.draw), new[] { typeof(SpriteBatch) });
        harmony.Patch(draw, prefix: new HarmonyMethod(typeof(HorseScale), nameof(BeforeHorseDraw)),
            postfix: new HarmonyMethod(typeof(HorseScale), nameof(AfterHorseDraw)),
            transpiler: new HarmonyMethod(typeof(HorseScale), nameof(Transpile)));
        var farmerDraw = AccessTools.Method(typeof(Farmer), nameof(Farmer.draw), new[] { typeof(SpriteBatch) });
        harmony.Patch(farmerDraw, prefix: new HarmonyMethod(typeof(HorseScale), nameof(BeforeFarmerDraw)),
            finalizer: new HarmonyMethod(typeof(HorseScale), nameof(AfterFarmerDraw)));
    }

    private static void BeforeHorseDraw(Horse __instance)
    {
        if (HorseFeature.ScaleEligible(__instance) || __instance.modData.ContainsKey(HorseSkins.SkinKey) && !HorseFeature.IsTractor(__instance)) HorseSkins.ApplyTo(__instance);
        current = !failed && Applies(__instance) ? __instance : null;
        if (current != null)
        {
            var h = __instance;
            // Native body anchor (NPC.draw) plus the distance to the hoof line (sprite y 24 -> 32).
            pivot = h.getLocalPosition(Game1.viewport) + new Vector2(h.GetSpriteWidthForPositioning() * 4 / 2, h.GetBoundingBox().Height / 2)
                + new Vector2(0, (h.Sprite.SpriteHeight - h.Sprite.SpriteHeight * 3f / 4f) * 4);
        }
    }
    private static void AfterHorseDraw() => current = null;

    private static Vector2 Map(Vector2 p) => pivot + (p - pivot) * Factor;

    /// <summary>Replaces the base NPC body draw and every SpriteBatch.Draw / Hat.draw call in Horse.draw.</summary>
    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var drawMethod = AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), new[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float) });
        var hatDraw = AccessTools.Method(typeof(Hat), nameof(Hat.draw), new[] { typeof(SpriteBatch), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(int), typeof(bool) });
        int body = 0, sprites = 0, hats = 0;
        foreach (var code in instructions)
        {
            // 'base.draw(b)' compiles to a non-virtual call of Character.draw(SpriteBatch).
            if (code.opcode == OpCodes.Call && code.operand is MethodInfo m && m.Name == "draw" && m.DeclaringType != typeof(Horse)
                && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(SpriteBatch) && m.DeclaringType!.IsAssignableFrom(typeof(Horse)))
            { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(HorseScale), nameof(Body)); body++; }
            else if ((code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Call) && Equals(code.operand, drawMethod))
            { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(HorseScale), nameof(Draw)); sprites++; }
            else if ((code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Call) && Equals(code.operand, hatDraw))
            { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(HorseScale), nameof(HatDraw)); hats++; }
            yield return code;
        }
        if (body != 1 || sprites < 2)
        {
            failed = true; // unknown Horse.draw: the helpers below pass everything through unchanged
            Monitor?.Log($"BSV horse size: Horse.draw differs from 1.6.15 (body {body}, draws {sprites}, hats {hats}); horses keep native size.", LogLevel.Warn);
        }
        else Monitor?.Log($"BSV horse size: 1.15x active (body {body}, draws {sprites}, hats {hats}).", LogLevel.Trace);
    }

    /// <summary>NPC.draw (non-swimming branch) with the 1.3x scale around the hooves.</summary>
    private static void Body(Character npc, SpriteBatch b)
    {
        if (current == null || !ReferenceEquals(npc, current) || npc.Sprite?.Texture == null || current.swimming.Value) { BaseDraw(npc, b); return; }
        var h = current;
        if (h.IsInvisible || !Utility.isOnScreen(h.Position, 192)) return;
        float depth = Math.Max(0f, h.drawOnTop ? 0.991f : h.StandingPixel.Y / 10000f);
        var anchor = h.getLocalPosition(Game1.viewport) + new Vector2(h.GetSpriteWidthForPositioning() * 4 / 2, h.GetBoundingBox().Height / 2)
            + (h.shakeTimer > 0 ? new Vector2(Game1.random.Next(-1, 2), Game1.random.Next(-1, 2)) : Vector2.Zero);
        bool flip = h.flip || h.Sprite.CurrentAnimation != null && h.Sprite.CurrentAnimation[h.Sprite.currentAnimationIndex].flip;
        b.Draw(h.Sprite.Texture, Map(anchor), h.Sprite.SourceRect, Color.White, h.rotation, new Vector2(h.Sprite.SpriteWidth / 2, h.Sprite.SpriteHeight * 3f / 4f),
            Math.Max(0.2f, h.Scale) * 4f * Factor, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, depth);
        h.DrawGlow(b);
        if (!Game1.eventUp) h.DrawEmote(b);
    }
    // Non-virtual call of Character.draw(SpriteBatch), exactly what the original 'base.draw(b)' did.
    private static readonly Action<Character, SpriteBatch> BaseDraw = AccessTools.MethodDelegate<Action<Character, SpriteBatch>>(
        AccessTools.Method(typeof(Character), nameof(Character.draw), new[] { typeof(SpriteBatch) }), virtualCall: false);
    private static void Draw(SpriteBatch b, Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float depth)
    {
        if (current != null) { position = Map(position); scale *= Factor; }
        b.Draw(texture, position, source, color, rotation, origin, scale, effects, depth);
    }
    private static void HatDraw(Hat hat, SpriteBatch b, Vector2 location, float scaleSize, float transparency, float depth, int direction, bool useAnimalTexture)
    {
        if (current != null) { location = Map(location); scaleSize *= Factor; }
        hat.draw(b, location, scaleSize, transparency, depth, direction, useAnimalTexture);
    }

    // ---- rider ----
    private static void BeforeFarmerDraw(Farmer __instance, ref Vector2 __state)
    {
        __state = __instance.jitter;
        if (failed || !__instance.isRidingHorse() || !Applies(__instance.mount)) return;
        // The horse's own Horse.draw call inside Farmer.draw does not use the rider's jitter.
        __instance.jitter += SaddleLift(__instance.mount.FacingDirection);
    }
    private static Exception? AfterFarmerDraw(Farmer __instance, Vector2 __state, Exception? __exception)
    {
        __instance.jitter = __state;
        return __exception;
    }
}

using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Monsters;

namespace BetterStardewValley.MultiplayerRescue;

internal sealed partial class RescueService
{
    private static MethodInfo Exact(Type type, string name, Type result, params Type[] arguments)
    {
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == name && m.ReturnType == result && !m.IsGenericMethod
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(arguments)).ToArray();
        if (methods.Length != 1) throw new MissingMethodException($"BSV rescue: {type.FullName}.{name}: expected one exact signature; found {methods.Length}.");
        return methods[0];
    }
    private static void Install(Harmony h)
    {
        HarmonyMethod Hook(string name) => new(typeof(RescueService), name);
        var after = Hook(nameof(AfterDamage));
        after.priority = Priority.Last;
        after.after = new[] { TeaAppleRules.Prefix + ".GoldenApple" };
        var trace = Hook(nameof(DamageTrace)); trace.after = after.after;
        h.Patch(Exact(typeof(Farmer), nameof(Farmer.takeDamage), typeof(void), typeof(int), typeof(bool), typeof(Monster)),
            prefix: Hook(nameof(BeforeDamage)), postfix: after, transpiler: trace, finalizer: Hook(nameof(FinishDamage)));
        h.Patch(Exact(typeof(Farmer), nameof(Farmer.Update), typeof(void), typeof(GameTime), typeof(GameLocation)),
            prefix: Hook(nameof(BeforeNativeUpdate)),postfix: Hook(nameof(AfterNativeUpdate)),transpiler: Hook(nameof(DeathGuard)));
        h.Patch(Exact(typeof(Farmer), nameof(Farmer.CanBeDamaged), typeof(bool)), postfix: Hook(nameof(DamageImmunity)));
        h.Patch(Exact(typeof(Farmer), "MovePosition", typeof(void), typeof(GameTime), typeof(xTile.Dimensions.Rectangle), typeof(GameLocation)),
            prefix: Hook(nameof(Move)));
        h.Patch(Exact(typeof(FarmerRenderer), "draw", typeof(void), typeof(SpriteBatch), typeof(FarmerSprite.AnimationFrame), typeof(int),
            typeof(Rectangle), typeof(Vector2), typeof(Vector2), typeof(float), typeof(int), typeof(Color), typeof(float), typeof(float), typeof(Farmer)),
            prefix: Hook(nameof(Pose)));
    }
    internal sealed class DamageSample { internal Farmer Farmer = null!; internal int Before; internal bool Hit; }
    [ThreadStatic] private static Stack<DamageSample>? DamageScopes;
    private static void BeforeDamage(Farmer __instance, out DamageSample __state)
    {
        __state = new DamageSample { Farmer = __instance, Before = __instance.health };
        (DamageScopes ??= new()).Push(__state);
    }
    private static void DamageAssigned(Farmer who)
    {
        if (DamageScopes?.Count > 0 && DamageScopes.Peek() is var s && s.Farmer == who)
            s.Hit |= s.Before > who.health;
    }
    private static void AfterDamage(Farmer __instance, DamageSample? __state)
    { if (__state?.Hit == true) Instance?.Safe(() => Instance.Damaged(__instance, __state.Before)); }
    private static Exception? FinishDamage(DamageSample? __state, Exception? __exception)
    {
        if (DamageScopes?.Count > 0 && ReferenceEquals(DamageScopes.Peek(), __state)) DamageScopes.Pop();
        return __exception;
    }
    internal static IEnumerable<CodeInstruction> DamageTrace(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList(); int count = 0;
        var health = AccessTools.Field(typeof(Farmer), nameof(Farmer.health));
        for (int i = 0; i < code.Count; i++)
        {
            bool native = i >= 2 && code[i].StoresField(health) && code[i-1].operand is MethodInfo max
                && max.DeclaringType == typeof(Math) && max.Name == nameof(Math.Max) && code[i-2].opcode == OpCodes.Sub;
            bool apple = code[i].operand is MethodInfo m && m.DeclaringType == typeof(GoldenAppleFeature) && m.Name == "SetDamageHealth";
            if (!native && !apple) continue;
            code.InsertRange(i+1,new[] { new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Call,
                Exact(typeof(RescueService),nameof(DamageAssigned),typeof(void),typeof(Farmer))) });
            count++; i += 2;
        }
        if (count != 1) throw new InvalidOperationException($"BSV rescue damage trace expected once; found {count}.");
        return code;
    }
    private static void DamageImmunity(Farmer __instance, ref bool __result)
    { if (Instance?.ProtectDeath(__instance) == true) __result = false; }
    private static bool Move(Farmer __instance) => !Controls(__instance);
    // Only alter the value read by the native injury-death predicate. Never rewrite
    // health globally, CanMove, shouldTimePass, freezeControls or native sleep flags.
    internal static IEnumerable<CodeInstruction> DeathGuard(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList(); int matches = 0;
        var health = AccessTools.Field(typeof(Farmer), nameof(Farmer.health));
        var kill = AccessTools.Field(typeof(Game1), nameof(Game1.killScreen));
        for (int i = 0; i + 3 < code.Count; i++)
        {
            if (!code[i].LoadsField(health) || !code[i+1].LoadsConstant(0)
                || (code[i+2].opcode != OpCodes.Bgt && code[i+2].opcode != OpCodes.Bgt_S)
                || !code[i+3].LoadsField(kill)) continue;
            code[i].opcode = OpCodes.Call;
            code[i].operand = Exact(typeof(RescueService), nameof(DeathHealth), typeof(int), typeof(Farmer));
            matches++;
        }
        if (matches != 1) throw new InvalidOperationException($"BSV rescue: native injury guard expected once, found {matches}. Rescue patches rolled back.");
        return code;
    }
    private static int DeathHealth(Farmer who) => Instance?.ProtectDeath(who) == true ? 1 : who.health;
    private static void BeforeNativeUpdate(Farmer __instance)
    { if(World && __instance==Game1.player && __instance.IsLocalPlayer)Instance?.Safe(()=>Instance.RecoveryTick(false)); }
    private static void AfterNativeUpdate(Farmer __instance)
    { if(World && __instance==Game1.player && __instance.IsLocalPlayer)Instance?.Safe(()=>Instance.ObserveNativeRecovery()); }
    private static bool PoseFailed;
    // Render-only: a failure here must never take down the game's draw loop.
    private static void Pose(Farmer who, ref FarmerSprite.AnimationFrame animationFrame, ref int currentFrame,
        ref Rectangle sourceRect, ref int facingDirection)
    {
        if (PoseFailed) return;
        try { PoseCore(who, ref animationFrame, ref currentFrame, ref sourceRect, ref facingDirection); }
        catch (Exception ex) { PoseFailed = true; Instance?.Monitor.Log("BSV rescue: downed pose disabled after a drawing error: " + ex, LogLevel.Error); }
    }
    private static void PoseCore(Farmer who, ref FarmerSprite.AnimationFrame animationFrame, ref int currentFrame,
        ref Rectangle sourceRect, ref int facingDirection)
    {
        var s = Instance;
        if (s == null || !s.Healthy || !World || Context.IsSplitScreen || who.currentLocation != Game1.currentLocation
            || who==Game1.player && (s.Released || s.TerminatedEvent==s.ActiveEvent && s.ActiveEvent.Length>0)) return;
        int frame;
        if (s.View.TryGetValue(who.UniqueMultiplayerID, out var r) && r.Outcome == RescueOutcome.Downed)
            frame = s.HostNow-r.DownAt < .18 ? 4 : 5;
        else if (who == Game1.player && s.Now-s.StandAt < .30)
            frame = s.Now-s.StandAt < .15 ? 5 : 4;
        else if (r?.Outcome == RescueOutcome.Revived && s.HostNow-r.ResolvedAt < .30)
            frame = s.HostNow-r.ResolvedAt < .15 ? 5 : 4;
        else return;
        // Native FarmerSprite pass-out frames 4/5, inspected in 1.6.15. Render only:
        // never install animation 293's passOutFromTired callback or change collision.
        // Farmer frames are laid out like FarmerSprite.UpdateSourceRect: six 16 px frames per 96 px row.
        // (FarmerSprite.Texture is null for farmers: FarmerRenderer draws from its own base texture.)
        const int columns = 96 / 16;
        currentFrame = frame; sourceRect = new Rectangle(frame % columns * 16, frame / columns * 32, 16, 32);
        facingDirection = 2; animationFrame = new FarmerSprite.AnimationFrame(frame, 100);
    }
}

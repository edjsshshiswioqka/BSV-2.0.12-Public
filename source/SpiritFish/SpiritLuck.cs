using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Minigames;

namespace BetterStardewValley.SpiritFish;

// One dated, per-farmer blessing, shared with SpiritService's existing consumption path.
// Neither the base skill field, equipment nor the team's shared daily-luck field is changed.
internal static class SpiritLuck
{
    [ThreadStatic] private static Farmer? Bypass;
    internal static bool Active(Farmer who) => who != null && !ReferenceEquals(Bypass, who)
        && Context.IsWorldReady && who.health > 0 && !who.passedOut
        && who.modData.TryGetValue(SpiritRules.LuckKey, out var day) && day == Game1.Date.TotalDays.ToString();

    internal static void Install(Harmony h, IModHelper helper, IMonitor monitor)
    {
        h.Patch(DrawPatchTargets.Require(typeof(Farmer), "get_DailyLuck", typeof(double)),
            postfix: new HarmonyMethod(typeof(SpiritLuck), nameof(Daily)) { priority = Priority.Last });
        h.Patch(DrawPatchTargets.Require(typeof(Farmer), "get_LuckLevel", typeof(int)),
            postfix: new HarmonyMethod(typeof(SpiritLuck), nameof(Level)) { priority = Priority.Last });
        h.Patch(DrawPatchTargets.Require(typeof(Farmer), "takeDamage", typeof(void), typeof(int), typeof(bool), typeof(StardewValley.Monsters.Monster)),
            postfix: new HarmonyMethod(typeof(SpiritLuck), nameof(Damage)));
        // A mismatch throws before installing altered IL. SpiritService rolls back its patch owner.
        h.Patch(DrawPatchTargets.Require(typeof(Slots), "setSlotResults", typeof(void), typeof(List<float>)),
            transpiler: new HarmonyMethod(typeof(SpiritLuck), nameof(SlotsIL)));
        h.Patch(DrawPatchTargets.Require(typeof(CalicoJack), "tick", typeof(bool), typeof(Microsoft.Xna.Framework.GameTime)),
            transpiler: new HarmonyMethod(typeof(SpiritLuck), nameof(CalicoIL)));
        var module=typeof(Farmer).Module;
        if(module.ModuleVersionId.ToString()!=SpiritLuckTargets.Mvid)
            throw new NotSupportedException("Spirit luck call-site audit does not match this Stardew Valley assembly; no guessed target patches will be installed.");
        foreach(var site in SpiritLuckTargets.Sites)
            h.Patch(module.ResolveMethod(site.Token)!,transpiler:new HarmonyMethod(typeof(SpiritLuck),nameof(InputsIL)));
        helper.Events.GameLoop.DayEnding += (_, _) =>
        {
            if (!Context.IsWorldReady) return;
            // Host clears the whole day's online contributions before shared overnight work.
            if (Context.IsMainPlayer) foreach(var who in Game1.getOnlineFarmers()) End(who);
            else End(Game1.player);
        };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => Bypass = null;
    }

    internal static void End(Farmer who) => who.modData.Remove(SpiritRules.LuckKey);
    private static void Damage(Farmer __instance) { if (__instance.health <= 0) End(__instance); }
    private static void Daily(Farmer __instance, ref double __result) { if (Active(__instance)) __result = SpiritRules.DailyLuck; }
    private static void Level(Farmer __instance, ref int __result) { if (Active(__instance)) __result = SpiritRules.LuckLevel; }
    internal static int SlotLevel(Farmer who) => Active(who) ? SpiritRules.SlotLuckLevel : who.LuckLevel;
    internal static double EffectiveDaily(Farmer who) => Active(who) ? SpiritRules.DailyLuck : who.DailyLuck;
    internal static int EffectiveLevel(Farmer who) => Active(who) ? SpiritRules.LuckLevel : who.LuckLevel;

    // CalicoJack must retain its normal inputs, including current equipment/other mods.
    // Only the individual getter call bypasses this blessing; try/finally prevents leaked scope.
    internal static double NormalDaily(Farmer who)
    { var previous = Bypass; try { Bypass = who; return who.DailyLuck; } finally { Bypass = previous; } }
    internal static int NormalLevel(Farmer who)
    { var previous = Bypass; try { Bypass = who; return who.LuckLevel; } finally { Bypass = previous; } }

    internal static IEnumerable<CodeInstruction> SlotsIL(IEnumerable<CodeInstruction> input) =>
        ReplaceReads(input, false);
    internal static IEnumerable<CodeInstruction> CalicoIL(IEnumerable<CodeInstruction> input) =>
        ReplaceReads(input, true);
    internal static IEnumerable<CodeInstruction> InputsIL(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
    {
        var site=SpiritLuckTargets.Sites.Single(s=>s.Token==__originalMethod.MetadataToken);
        var result=Replace(input,site.Daily,site.Level,nameof(EffectiveDaily),nameof(EffectiveLevel),__originalMethod.ToString()!).ToList();
        if(__originalMethod.DeclaringType==typeof(StardewValley.Tools.FishingRod)&&__originalMethod.Name=="startMinigameEndFunction")
            return TreasureIL(result);
        if(__originalMethod.DeclaringType==typeof(StardewValley.Locations.MineShaft)&&__originalMethod.Name=="checkStoneForItems")
        {
            // Native ladder code uses who for L but Game1.player for D. Fix attribution only
            // when this blessing is involved, leaving unblessed vanilla behavior unchanged.
            var player=DrawPatchTargets.Require(typeof(Game1),"get_player",typeof(Farmer));
            var daily=typeof(SpiritLuck).GetMethod(nameof(EffectiveDaily),BindingFlags.Static|BindingFlags.NonPublic)!;
            var indices=Enumerable.Range(0,result.Count-1).Where(i=>result[i].Calls(player)&&result[i+1].Calls(daily)).ToArray();
            if(indices.Length!=1)throw new InvalidOperationException("Spirit ladder owner pattern mismatch.");
            int i=indices[0];result.Insert(i+1,new CodeInstruction(OpCodes.Ldarg_S,(byte)4));
            result[i+2].operand=typeof(SpiritLuck).GetMethod(nameof(LadderDaily),BindingFlags.Static|BindingFlags.NonPublic)!;
        }
        return result;
    }
    internal static double LadderDaily(Farmer nativePlayer,Farmer? actor) => actor!=null&&(Active(actor)||Active(nativePlayer))
        ? EffectiveDaily(actor) : EffectiveDaily(nativePlayer);
    // Only the chance operand at the existing random comparison is changed.
    // Magnet + Pirate + 1/2 Treasure Hunters can coexist. Mastery gates the second slot.
    // Bait, tackle, difficulty, fish eligibility and gold-chest mastery checks are left intact.
    internal static double TreasureThreshold(double native, Farmer who)
    {
        if(!Active(who))return native;
        int slots=who.stats.Get(StardewValley.Constants.StatKeys.Mastery(1))>0?2:1;
        double basis=StardewValley.Tools.FishingRod.baseChanceForTreasure;
        return 3*basis+slots*basis/3+.005*SpiritRules.LuckLevel+SpiritRules.DailyLuck/2;
    }
    internal static IEnumerable<CodeInstruction> TreasureIL(List<CodeInstruction> code)
    {
        var points=code.Select((ins,index)=>(ins,index)).Where(p=>p.ins.opcode==OpCodes.Clt).ToArray();
        if(points.Length!=1||points[0].index==0||code[points[0].index-1].opcode!=OpCodes.Add)
            throw new InvalidOperationException("Spirit fishing treasure comparison pattern mismatch.");
        int i=points[0].index;var load=new CodeInstruction(OpCodes.Ldarg_0);
        load.labels.AddRange(code[i].labels);code[i].labels.Clear();
        load.blocks.AddRange(code[i].blocks);code[i].blocks.Clear();
        code.InsertRange(i,new[]{load,new CodeInstruction(OpCodes.Ldfld,typeof(Tool).GetField("lastUser",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!),
            new CodeInstruction(OpCodes.Call,typeof(SpiritLuck).GetMethod(nameof(TreasureThreshold),BindingFlags.Static|BindingFlags.NonPublic)!)});
        return code;
    }
    private static IEnumerable<CodeInstruction> ReplaceReads(IEnumerable<CodeInstruction> input, bool calico)
        => Replace(input,1,1,calico?nameof(NormalDaily):nameof(EffectiveDaily),calico?nameof(NormalLevel):nameof(SlotLevel),calico?"CalicoJack":"Slots");
    private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> input,int expectedDaily,int expectedLevel,string dailyName,string levelName,string target)
    {
        var code = input.Select(i => new CodeInstruction(i)).ToList();
        var daily = DrawPatchTargets.Require(typeof(Farmer), "get_DailyLuck", typeof(double));
        var level = DrawPatchTargets.Require(typeof(Farmer), "get_LuckLevel", typeof(int));
        int d = code.Count(i => i.Calls(daily)), l = code.Count(i => i.Calls(level));
        if (d != expectedDaily || l != expectedLevel) throw new InvalidOperationException($"Spirit luck target mismatch ({target}): Daily={d}/{expectedDaily}, Level={l}/{expectedLevel}.");
        foreach (var i in code)
        {
            string? name = i.Calls(level) ? levelName : i.Calls(daily) ? dailyName : null;
            if (name == null) continue;
            i.opcode = OpCodes.Call;
            i.operand = typeof(SpiritLuck).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Farmer) }, null)!;
        }
        return code;
    }
}

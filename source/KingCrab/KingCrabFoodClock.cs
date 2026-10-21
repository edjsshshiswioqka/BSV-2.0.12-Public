using System.Runtime.CompilerServices;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buffs;
namespace BetterStardewValley.KingCrab;

internal static class KingCrabFoodClock
{
    private sealed record Active(Farmer Owner,KingCrabClock Clock);
    private const string Marker="Eddie.BetterStardewValley/KingCrabFood";
    private static readonly ConditionalWeakTable<Buff,Active> Clocks=new();
    internal static void Install(Harmony h)
    {
        h.Patch(DrawPatchTargets.Require(typeof(StardewValley.Object),"healthRecoveredOnConsumption",typeof(int)),postfix:new HarmonyMethod(typeof(KingCrabFoodClock),nameof(Health)));
        h.Patch(DrawPatchTargets.Require(typeof(StardewValley.Object),"GetFoodOrDrinkBuffs",typeof(IEnumerable<Buff>)),postfix:new HarmonyMethod(typeof(KingCrabFoodClock),nameof(Tag)));
        h.Patch(DrawPatchTargets.Require(typeof(BuffManager),"Apply",typeof(void),typeof(Buff)),postfix:new HarmonyMethod(typeof(KingCrabFoodClock),nameof(Applied)));
        h.Patch(DrawPatchTargets.Require(typeof(Buff),"update",typeof(bool),typeof(GameTime)),prefix:new HarmonyMethod(typeof(KingCrabFoodClock),nameof(Update)));
        h.Patch(DrawPatchTargets.Require(typeof(Buff),"getTimeLeft",typeof(string)),prefix:new HarmonyMethod(typeof(KingCrabFoodClock),nameof(TimeLeft)));
        h.Patch(DrawPatchTargets.Require(typeof(Buff),"OnRemoved",typeof(void)),postfix:new HarmonyMethod(typeof(KingCrabFoodClock),nameof(Removed)));
    }
    private static void Health(StardewValley.Object __instance,ref int __result)
    {
        // Only this dish has equal health/energy recovery. Reuse native energy calculation,
        // including quality/seasoning exactly once, for both tooltip and actual consumption.
        if(__instance.QualifiedItemId==KingCrabRules.Bowl)__result=__instance.staminaRecoveredOnConsumption();
    }
    private static void Tag(StardewValley.Object __instance,ref IEnumerable<Buff> __result)
    {if(__instance.QualifiedItemId==KingCrabRules.Bowl)__result=Marked(__result);}
    private static IEnumerable<Buff> Marked(IEnumerable<Buff> source)
    {foreach(var buff in source){if(buff.id=="food")buff.customFields[Marker]="1";yield return buff;}}
    private static void Applied(BuffManager __instance,Farmer ___Player,[HarmonyArgument(0)]Buff buff)
    {
        if(!buff.customFields.ContainsKey(Marker)||buff.id!="food"||!___Player.IsLocalPlayer
            ||!__instance.AppliedBuffs.TryGetValue("food",out var current)||!ReferenceEquals(current,buff))return;
        Start(___Player,buff,Game1.Date.TotalDays,Game1.timeOfDay,Game1.gameTimeInterval>0);
    }
    internal static void Start(Farmer who,Buff buff,int day,int time,bool partialTick=false)
    {
        if(buff.millisecondsDuration<=0||Game1.realMilliSecondsPerGameMinute<=0)throw new InvalidOperationException("Invalid native food duration.");
        Clocks.Remove(buff);Clocks.Add(buff,new(who,new(day,time,(double)buff.millisecondsDuration/Game1.realMilliSecondsPerGameMinute,partialTick)));
    }
    private static bool Current(Buff buff,Active active)=>active.Owner.buffs.AppliedBuffs.TryGetValue("food",out var value)&&ReferenceEquals(value,buff);
    internal static bool Advance(Buff buff,int day,int time)
    {
        if(!Clocks.TryGetValue(buff,out var a)||!Current(buff,a)){Clocks.Remove(buff);return false;}
        bool expired=a.Clock.Advance(day,time);
        buff.millisecondsDuration=(int)Math.Ceiling(a.Clock.Remaining*Game1.realMilliSecondsPerGameMinute);
        return expired;
    }
    private static bool Update(Buff __instance,ref bool __result)
    {
        if(!Clocks.TryGetValue(__instance,out var a)||!Current(__instance,a))return true;
        __result=Advance(__instance,Game1.Date.TotalDays,Game1.timeOfDay);return false; // never also debit real-time
    }
    internal static void WorldTick(Farmer who)
    {if(who.buffs.AppliedBuffs.TryGetValue("food",out var buff)&&Advance(buff,Game1.Date.TotalDays,Game1.timeOfDay))who.buffs.Remove("food");}
    internal static void End(Farmer who)
    {if(who.buffs.AppliedBuffs.TryGetValue("food",out var b)&&Clocks.TryGetValue(b,out _))who.buffs.Remove("food");}
    private static void Removed(Buff __instance)=>Clocks.Remove(__instance);
    private static bool TimeLeft(Buff __instance,ref string __result)
    {
        if(!Clocks.TryGetValue(__instance,out var a))return true;
        int minutes=(int)Math.Ceiling(a.Clock.Remaining);
        __result=KingCrabService.Text("kingcrab.time").Replace("{hours}",(minutes/60).ToString()).Replace("{minutes}",(minutes%60).ToString("00"));return false;
    }
}

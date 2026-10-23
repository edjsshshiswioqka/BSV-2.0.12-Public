using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.Tools;
namespace BetterStardewValley.LobsterKing;

// Narrow food-owned effects; no persistent player or weapon statistics are changed.
internal static class KingSashimi
{
    internal const string QualifiedId="(O)"+LobsterKingRules.SashimiId;
    internal const string Marker="Eddie.BetterStardewValley/LobsterKingFood";
    private sealed record Active(Farmer Owner,KingCrabClock Clock);
    private static readonly ConditionalWeakTable<Buff,Active> Clocks=new();
    internal static void Install(Harmony h)
    {
        h.Patch(DrawPatchTargets.Require(typeof(StardewValley.Object),"healthRecoveredOnConsumption",typeof(int)),postfix:new(typeof(KingSashimi),nameof(Health)));
        h.Patch(DrawPatchTargets.Require(typeof(StardewValley.Object),"GetFoodOrDrinkBuffs",typeof(IEnumerable<Buff>)),postfix:new(typeof(KingSashimi),nameof(Tag)));
        h.Patch(DrawPatchTargets.Require(typeof(BuffManager),"Apply",typeof(void),typeof(Buff)),postfix:new(typeof(KingSashimi),nameof(Applied)));
        h.Patch(DrawPatchTargets.Require(typeof(Buff),"update",typeof(bool),typeof(GameTime)),prefix:new(typeof(KingSashimi),nameof(Update)));
        h.Patch(DrawPatchTargets.Require(typeof(Buff),"getTimeLeft",typeof(string)),prefix:new(typeof(KingSashimi),nameof(TimeLeft)));
        h.Patch(DrawPatchTargets.Require(typeof(Buff),"OnRemoved",typeof(void)),postfix:new(typeof(KingSashimi),nameof(Removed)));
        h.Patch(DrawPatchTargets.Require(typeof(NPC),"getGiftTasteForThisItem",typeof(int),typeof(Item)),postfix:new(typeof(KingSashimi),nameof(Taste)));
        h.Patch(DrawPatchTargets.Require(typeof(MeleeWeapon),"setFarmerAnimating",typeof(void),typeof(Farmer)),transpiler:new(typeof(KingSashimi),nameof(SpeedRead)));
        h.Patch(DrawPatchTargets.Require(typeof(MeleeWeapon),"DoDamage",typeof(void),typeof(GameLocation),typeof(int),typeof(int),typeof(int),typeof(int),typeof(Farmer)),transpiler:new(typeof(KingSashimi),nameof(CriticalRead)));
    }
    private static void Health(StardewValley.Object __instance,ref int __result)
    {if(__instance.QualifiedItemId==QualifiedId)__result=__instance.staminaRecoveredOnConsumption();}
    internal static bool IsActive(Farmer who)=>who.buffs.AppliedBuffs.TryGetValue("food",out var b)&&b.customFields.ContainsKey(Marker);
    internal static int EffectiveSpeed(int speed,Farmer who)=>IsActive(who)?checked(speed+2):speed;
    // Native drawTooltip displays Round((weapon crit chance - .001) / .02).
    // Two additional native points therefore add .04, NOT a relative 2% multiplier.
    // Insert after dagger normalization and before the native buff/ring multiplier.
    internal static float EffectiveCritical(float chance,Farmer who)=>IsActive(who)?chance+.04f:chance;
    private static IEnumerable<CodeInstruction> CriticalRead(IEnumerable<CodeInstruction> instructions)
    {
        var code=instructions.ToList();var getter=AccessTools.PropertyGetter(typeof(BuffManager),nameof(BuffManager.CriticalChanceMultiplier));
        var matches=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(getter)).ToArray();
        int at=matches.Length==1?matches[0]-4:-1;
        if(at<0||code[at].opcode!=OpCodes.Ldloc_3||code[at+1].opcode!=OpCodes.Ldc_R4||!Equals(code[at+1].operand,1f)
            ||code[at+2].opcode!=OpCodes.Ldarg_S||!(Equals(code[at+2].operand,(byte)6)||Equals(code[at+2].operand,6)||code[at+2].operand is ParameterInfo p&&p.Position==5&&p.ParameterType==typeof(Farmer))
            ||!code[at+3].LoadsField(AccessTools.Field(typeof(Farmer),nameof(Farmer.buffs))))
            throw new InvalidOperationException("Lobster King critical chance target mismatch; refusing partial combat patch.");
        for(int i=0;i<code.Count;i++)
        {
            yield return code[i];
            if(i==at){yield return new(OpCodes.Ldarg_S,(byte)6);yield return new(OpCodes.Call,AccessTools.Method(typeof(KingSashimi),nameof(EffectiveCritical)));}
        }
    }
    private static IEnumerable<CodeInstruction> SpeedRead(IEnumerable<CodeInstruction> instructions)
    {
        var code=instructions.ToList();var field=AccessTools.Field(typeof(MeleeWeapon),nameof(MeleeWeapon.speed));
        var matches=Enumerable.Range(0,code.Count-1).Where(i=>code[i].LoadsField(field)
            &&code[i+1].operand is MethodInfo m&&m.Name=="get_Value"&&m.ReturnType==typeof(int)).ToArray();
        if(matches.Length!=1)throw new InvalidOperationException("Lobster King melee speed target mismatch; no partial speed patch.");
        for(int i=0;i<code.Count;i++)
        {
            yield return code[i];
            if(i==matches[0]+1){yield return new(OpCodes.Ldarg_1);yield return new(OpCodes.Call,AccessTools.Method(typeof(KingSashimi),nameof(EffectiveSpeed)));}
        }
    }
    private static void Taste([HarmonyArgument(0)]Item item,ref int __result)
    {if(item.QualifiedItemId==QualifiedId&&__result is not (NPC.gift_taste_love or NPC.gift_taste_like))__result=NPC.gift_taste_like;}
    private static void Tag(StardewValley.Object __instance,ref IEnumerable<Buff> __result)
    {if(__instance.QualifiedItemId==QualifiedId)__result=Marked(__result);}
    private static IEnumerable<Buff> Marked(IEnumerable<Buff> source)
    {foreach(var b in source){if(b.id=="food"){b.customFields[Marker]="1";b.description+=("\n"+KingService.Text("king.sashimi.speed")+"\n"+KingService.Text("king.sashimi.critical"));}yield return b;}}
    private static void Applied(BuffManager __instance,Farmer ___Player,[HarmonyArgument(0)]Buff buff)
    {
        if(!buff.customFields.ContainsKey(Marker)||buff.id!="food"||!___Player.IsLocalPlayer
            ||!__instance.AppliedBuffs.TryGetValue("food",out var current)||!ReferenceEquals(current,buff))return;
        Start(___Player,buff,Game1.Date.TotalDays,Game1.timeOfDay,Game1.gameTimeInterval>0);
    }
    internal static void Start(Farmer who,Buff buff,int day,int time,bool partialTick=false)
    {
        if(buff.millisecondsDuration<=0||Game1.realMilliSecondsPerGameMinute<=0)return; // endless or unusual buff (another mod): leave it on the native clock
        Clocks.Remove(buff);Clocks.Add(buff,new(who,new(day,time,(double)buff.millisecondsDuration/Game1.realMilliSecondsPerGameMinute,partialTick)));
    }
    private static bool Current(Buff b,Active a)=>a.Owner.buffs.AppliedBuffs.TryGetValue("food",out var value)&&ReferenceEquals(value,b);
    internal static bool Advance(Buff b,int day,int time)
    {
        if(!Clocks.TryGetValue(b,out var a)||!Current(b,a)){Clocks.Remove(b);return false;}
        bool expired=a.Clock.Advance(day,time);b.millisecondsDuration=(int)Math.Ceiling(a.Clock.Remaining*Game1.realMilliSecondsPerGameMinute);return expired;
    }
    private static bool Update(Buff __instance,ref bool __result)
    {if(!Clocks.TryGetValue(__instance,out var a)||!Current(__instance,a))return true;__result=Advance(__instance,Game1.Date.TotalDays,Game1.timeOfDay);return false;}
    internal static void WorldTick(Farmer who)
    {if(who.buffs.AppliedBuffs.TryGetValue("food",out var b)&&Advance(b,Game1.Date.TotalDays,Game1.timeOfDay))who.buffs.Remove("food");}
    internal static void End(Farmer who)
    {if(who.buffs.AppliedBuffs.TryGetValue("food",out var b)&&b.customFields.ContainsKey(Marker))who.buffs.Remove("food");}
    private static void Removed(Buff __instance)=>Clocks.Remove(__instance);
    private static bool TimeLeft(Buff __instance,ref string __result)
    {
        if(!Clocks.TryGetValue(__instance,out var a))return true;
        int minutes=(int)Math.Ceiling(a.Clock.Remaining);
        __result=KingService.Text("king.sashimi.time").Replace("{hours}",(minutes/60).ToString()).Replace("{minutes}",(minutes%60).ToString("00"));return false;
    }
}

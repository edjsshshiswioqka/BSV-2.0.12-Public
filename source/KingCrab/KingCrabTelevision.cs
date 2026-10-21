using System.Reflection.Emit;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Objects;
namespace BetterStardewValley.KingCrab;

internal static class KingCrabTelevision
{
    internal const string Channel="Eddie.BetterStardewValley_KingCrabPremiere";
    private sealed record Viewing(TV Television,Farmer Viewer,GameLocation Location,int Day);
    private static readonly PerScreen<Viewing?> Session=new(()=>null);
    internal static void Install(Harmony h)
    {
        h.Patch(DrawPatchTargets.Require(typeof(TV),"checkForAction",typeof(bool),typeof(Farmer),typeof(bool)),transpiler:new HarmonyMethod(typeof(KingCrabTelevision),nameof(MenuIL)));
        h.Patch(DrawPatchTargets.Require(typeof(TV),"selectChannel",typeof(void),typeof(Farmer),typeof(string)),prefix:new HarmonyMethod(typeof(KingCrabTelevision),nameof(Select)),finalizer:new HarmonyMethod(typeof(KingCrabTelevision),nameof(Failed)));
        h.Patch(DrawPatchTargets.Require(typeof(TV),"getWeeklyRecipe",typeof(string[])),prefix:new HarmonyMethod(typeof(KingCrabTelevision),nameof(Teach)));
        h.Patch(DrawPatchTargets.Require(typeof(TV),"turnOffTV",typeof(void)),postfix:new HarmonyMethod(typeof(KingCrabTelevision),nameof(Off)));
    }
    internal static bool Scheduled()=>KingCrabRules.Premiere(Game1.year,(int)Game1.season,Game1.dayOfMonth);
    internal static Response[] AddChannel(Response[] source)=>AppendChannel(source,KingCrabService.Ready&&KingCrabData.ApprovedBowl!=null);
    internal static Response[] AppendChannel(Response[] source,bool approved)
    {
        if(!approved||!Scheduled()||source.Any(r=>r.responseKey==Channel))return source;
        var result=source.ToList();int index=result.FindIndex(r=>r.responseKey=="(Leave)");
        result.Insert(index<0?result.Count:index,new Response(Channel,KingCrabService.Text("kingcrab.tv.channel")));return result.ToArray();
    }
    private static IEnumerable<CodeInstruction> MenuIL(IEnumerable<CodeInstruction> instructions)
    {
        var code=instructions.ToList();var target=AccessTools.Method(typeof(List<Response>),nameof(List<Response>.ToArray));
        if(code.Count(c=>c.Calls(target))!=1)throw new InvalidOperationException("TV channel-array insertion mismatch; no partial channel patch.");
        foreach(var c in code){yield return c;if(c.Calls(target))yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(KingCrabTelevision),nameof(AddChannel)));}
    }
    private static bool Select(TV __instance,[HarmonyArgument(0)]Farmer who,[HarmonyArgument(1)]ref string answer)
    {
        if(answer!=Channel)return true;
        if(!KingCrabService.Ready||KingCrabData.ApprovedBowl==null||!Scheduled()||!who.IsLocalPlayer)return false;
        BeginViewing(__instance,who);
        answer="The"; // native cooking opening/graphics/sound/scene transition; never change the date
        return true;
    }
    internal static void BeginViewing(TV television,Farmer who)=>Session.Value=new(television,who,who.currentLocation,Game1.Date.TotalDays);
    internal static bool Learn(Farmer who)
    {return who.cookingRecipes.TryAdd(KingCrabRules.BowlId,0);}
    private static bool Teach(TV __instance,ref string[] __result)
    {
        var s=Session.Value;if(s==null||!ReferenceEquals(s.Television,__instance))return true;
        if(!ReferenceEquals(Game1.player,s.Viewer)||!ReferenceEquals(s.Viewer.currentLocation,s.Location)||Game1.Date.TotalDays!=s.Day||!Scheduled())
        {__result=Array.Empty<string>();Cancel();return false;}
        bool learned=Learn(s.Viewer);
        __result=new[]{KingCrabService.Text("kingcrab.tv.lesson"),KingCrabService.Text(learned?"kingcrab.tv.learned":"kingcrab.tv.known")};
        return false; // leave native week selection/cache entirely untouched
    }
    private static void Off(TV __instance){if(ReferenceEquals(Session.Value?.Television,__instance))Session.Value=null;}
    private static Exception? Failed(Exception? __exception){if(__exception!=null)Cancel();return __exception;}
    internal static void Reset(){Cancel();Session.ResetAllScreens();}
    internal static void Cancel()
    {
        var s=Session.Value;Session.Value=null;if(s==null)return;
        if(ReferenceEquals(Game1.afterDialogues?.Target,s.Television))Game1.afterDialogues=null;
        s.Television.turnOffTV();
    }
}

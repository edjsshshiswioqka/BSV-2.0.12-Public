using System.Text.Json;
using HarmonyLib;
using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.FishPonds;
namespace BetterStardewValley.LobsterKing;

internal static class KingPonds
{
    internal const string Key="Eddie.BetterStardewValley/LobsterKing/Pond";
    internal static void Install(Harmony h)
    {
        void Hook(string method,Type returns,string? pre,string? post,params Type[] args)=>h.Patch(DrawPatchTargets.Require(typeof(FishPond),method,returns,args),
            prefix:pre==null?null:new HarmonyMethod(typeof(KingPonds),pre),postfix:post==null?null:new HarmonyMethod(typeof(KingPonds),post));
        // Resolve before GetFishPondData stores the result. Patching the instance
        // accessor instead can be bypassed by callers already JIT-inlined by another mod.
        Hook("GetRawData",typeof(FishPondData),null,nameof(Data),typeof(string));
        Hook("UpdateMaximumOccupancy",typeof(void),null,nameof(Capacity));
        Hook("SpawnFish",typeof(void),nameof(Spawn),null);
        Hook("GetFishProduce",typeof(Item),nameof(Produce),null,typeof(Random));
        Hook("isLegalFishForPonds",typeof(bool),nameof(Legal),null,typeof(string));
        Hook("performActiveObjectDropInAction",typeof(bool),nameof(Drop),null,typeof(Farmer),typeof(bool));
        Hook("dayUpdate",typeof(void),null,nameof(Observe),typeof(int));
        Hook("addFishToPond",typeof(bool),null,nameof(Observe),typeof(Farmer),typeof(StardewValley.Object));
        Hook("CatchFish",typeof(StardewValley.Object),null,nameof(Observe));
        Hook("OnFishTypeChanged",typeof(void),null,nameof(Observe),typeof(Netcode.NetString),typeof(string),typeof(string));
    }
    internal static IReadOnlyList<string>? NativeGate()
    {
        // GetRawData uses final loaded data, exact context tags and native precedence.
        // This also respects deliberate content-pack changes to ordinary Lobster ponds.
        try
        {
            var native=FishPond.GetRawData("(O)715");
            var gate=native?.PopulationGates?.Where(g=>g.Key>1&&g.Value?.Count>0).OrderBy(g=>g.Key).FirstOrDefault().Value;
            return gate!=null&&gate.All(s=>!string.IsNullOrWhiteSpace(s))?gate:null;
        }
        catch(Exception ex){KingService.Error("pond-native-gate",ex);return null;}
    }
    private static void Data([HarmonyArgument(0)]string id,ref FishPondData? __result)
    {
        if(!LobsterKingRules.IsFish(id)||__result?.Id!=LobsterKingRules.Id)return;
        var gate=NativeGate();
        if(gate==null){KingService.Error("pond-native-gate",new InvalidOperationException("Loaded ordinary Lobster has no usable population gate; King admission/upgrade paused."));return;}
        // Clone candidates rather than mutate the ordinary Lobster/shared template.
        __result=KingData.Pond(gate);
    }
    private static void Capacity(FishPond __instance)
    {
        // Audited 1.6.15: positive MaxPopulation returns before native gate iteration.
        // Keep the data cap at two and restore only this species' first upgrade stage.
        if(LobsterKingRules.IsFish(__instance.fishType.Value))
            __instance.maxOccupants.Value=__instance.lastUnlockedPopulationGate.Value>=2?2:1;
    }
    private static bool Spawn(FishPond __instance)=>!LobsterKingRules.IsFish(__instance.fishType.Value);
    private static bool Produce(FishPond __instance,ref Item __result)
    {
        if(!LobsterKingRules.IsFish(__instance.fishType.Value))return true;
        __result=__instance.output.Value;return false; // never run native roe/extra quantity or overwrite a waiting offspring
    }
    private static bool Legal([HarmonyArgument(0)]string id,ref bool __result)
    {if(!LobsterKingRules.IsFish(id)||NativeGate()!=null)return true;__result=false;return false;}
    private static bool Drop(FishPond __instance,Farmer who,bool probe,ref bool __result)
    {
        bool pending=LobsterKingRules.IsFish(who.ActiveObject?.QualifiedItemId)&&NativeGate()==null;
        bool cracker=LobsterKingRules.IsFish(__instance.fishType.Value)&&who.ActiveObject?.QualifiedItemId=="(O)GoldenAnimalCracker";
        if(!pending&&!cracker)return true;
        if(!probe&&who.IsLocalPlayer)KingService.Message(pending?"king.pond.pending":"king.pond.cracker");
        __result=false;return false;
    }
    internal static void Observe(FishPond __instance)
    {
        if(!Game1.IsMasterGame)return;
        if(!LobsterKingRules.IsFish(__instance.fishType.Value)){__instance.modData.Remove(Key);return;}
        try{Tick(__instance,Game1.Date.TotalDays);}catch(Exception ex){KingService.Error("pond",ex);}
    }
    internal static bool Tick(FishPond pond,int day)
    {
        if(!Game1.IsMasterGame||!LobsterKingRules.IsFish(pond.fishType.Value))return false;
        bool had=pond.modData.TryGetValue(Key,out var before);
        var state=had?JsonSerializer.Deserialize<KingPondState>(before)??throw new InvalidOperationException("Null King pond state."):new();
        // Stable per-pond/per-cycle random source. No shared/global RNG consumption.
        int seed=17;unchecked{foreach(char c in state.PondId)seed=seed*31+c;seed=seed*31+state.CycleIndex;seed=seed*31+day;}
        bool ready=state.Advance(day,pond.FishCount,pond.output.Value!=null,new Random(seed));
        string next=JsonSerializer.Serialize(state);if(next==before)return false;
        Item? output=ready?ItemRegistry.Create(LobsterKingRules.Fish,1,0):null;
        var previous=pond.output.Value;
        try{if(ready)pond.output.Value=output;pond.modData[Key]=next;}
        catch{pond.output.Value=previous;if(had)pond.modData[Key]=before;else pond.modData.Remove(Key);throw;}
        return ready;
    }
}

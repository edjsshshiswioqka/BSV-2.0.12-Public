using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Tools;
using StardewValley.GameData.Locations;
using StardewValley.Internal;
using Eddie.FishingMachine.Core;
using SObject=StardewValley.Object;
namespace BetterStardewValley.SpiritFish;

/// <summary>One eligible native fish opportunity per cast. Runs before FishingRod receives/displays the result.
/// Native selection (including garbage, scripts, prerequisites and retry loops) remains intact.
/// Only a successful ordinary Data/Fish candidate recorded by the native requirement check may enter E.</summary>
internal static class SpiritSelection
{
    internal sealed class Attempt
    {
        internal bool Rolled;internal Item? Result;internal Item? Fallback;
        internal SObject? UsedBait;internal Farmer? Player;internal GameLocation? Location;
        internal int Day,Time;internal ulong Save;
    }
    internal sealed class Scope
    {
        internal Scope? Parent;internal Attempt? Attempt;internal bool Eligible;
        internal bool Weight;internal string LocationName="";internal Vector2 Bobber;internal int Depth;
        internal Farmer Player=null!;internal GameLocation Location=null!;internal ItemQueryContext? Context;
        internal readonly HashSet<Item> Ordinary=new();
    }
    private delegate Item SelectNative(string name,Vector2 tile,int depth,Farmer player,bool tutorial,bool inherited,GameLocation location,ItemQueryContext? context);
    private static readonly SelectNative Select=(SelectNative)AccessTools.DeclaredMethod(typeof(GameLocation),"GetFishFromLocationData",new[]{typeof(string),typeof(Vector2),typeof(int),typeof(Farmer),typeof(bool),typeof(bool),typeof(GameLocation),typeof(ItemQueryContext)}).CreateDelegate(typeof(SelectNative));
    internal static long WeightFallbacks {get;private set;}
    [ThreadStatic] private static Scope? Current;
    private static readonly ConditionalWeakTable<FishingRod,Attempt> Attempts=new();
    internal static bool HasBait(Farmer who)=>who.CurrentTool is FishingRod rod&&rod.GetBait() is {Stack:>0} bait&&bait.QualifiedItemId==GiantLobsterRules.Bait;
    internal static void Install(Harmony h)
    {
        var target=DrawPatchTargets.Require(typeof(GameLocation),"GetFishFromLocationData",typeof(Item),typeof(string),typeof(Vector2),typeof(int),typeof(Farmer),typeof(bool),typeof(bool),typeof(GameLocation),typeof(ItemQueryContext));
        h.Patch(target,prefix:new HarmonyMethod(typeof(SpiritSelection),nameof(Begin)),postfix:new HarmonyMethod(typeof(SpiritSelection),nameof(End)){priority=Priority.Last},finalizer:new HarmonyMethod(typeof(SpiritSelection),nameof(Finish)));
        h.Patch(DrawPatchTargets.Require(typeof(FishingRod),"beginUsing",typeof(bool),typeof(GameLocation),typeof(int),typeof(int),typeof(Farmer)),prefix:new HarmonyMethod(typeof(SpiritSelection),nameof(Reset)));
        h.Patch(DrawPatchTargets.Require(typeof(FishingRod),"doDoneFishing",typeof(void),typeof(bool)),postfix:new HarmonyMethod(typeof(SpiritSelection),nameof(Reset)));
    }
    private static void Reset(FishingRod __instance)=>Attempts.Remove(__instance);
    internal static void Clear()=>Attempts.Clear();
    internal static void ResetPlayer(Farmer player)
    {foreach(var item in player.Items)if(item is FishingRod rod)Attempts.Remove(rod);}
    internal static bool ValidCapture(FishingRod rod,Farmer who,out Attempt? attempt)
    {
        if(!Attempts.TryGetValue(rod,out attempt))return false;
        return attempt.Rolled&&attempt.Player==who&&attempt.Save==Game1.uniqueIDForThisGame&&attempt.Day==Game1.Date.TotalDays
            &&ReferenceEquals(who.currentLocation,attempt.Location)&&ReferenceEquals(who.CurrentTool,rod)
            &&ReferenceEquals(rod.GetBait(),attempt.UsedBait)&&attempt.UsedBait?.Stack>0
            &&attempt.UsedBait.QualifiedItemId==GiantLobsterRules.Bait
            &&SpiritRules.Eligible(who.fishingLevel.Value,attempt.Time,attempt.Location!.NameOrUniqueName);
    }
    private static void Begin([HarmonyArgument(0)]string locationName,Vector2 bobberTile,int waterDepth,Farmer player,bool isTutorialCatch,bool isInherited,GameLocation location,ItemQueryContext itemQueryContext,out Scope __state)
    {
        __state=new(){Parent=Current,LocationName=locationName,Bobber=bobberTile,Depth=waterDepth,Player=player,Location=location,Context=itemQueryContext};Current=__state;
        if(__state.Parent!=null||isTutorialCatch||isInherited||player?.CurrentTool is not FishingRod rod||!rod.isFishing||!HasBait(player))return;
        __state.Attempt=Attempts.GetValue(rod,_=>new(){UsedBait=rod.GetBait(),Player=player,Location=location,Day=Game1.Date.TotalDays,Time=Game1.timeOfDay,Save=Game1.uniqueIDForThisGame});
        if(__state.Attempt.Rolled&&(!ReferenceEquals(__state.Attempt.UsedBait,rod.GetBait())
            ||__state.Attempt.Location!=location||__state.Attempt.Day!=Game1.Date.TotalDays||__state.Attempt.Player!=player))
        {__state.Attempt=null;return;}
        __state.Weight=true;
        __state.Eligible=SpiritService.Eligible(player,location)&&SpiritQuota.CanCatch(player);
    }
    internal static void Observe(Item? fish,SpawnFishData data,bool accepted)
    {
        if(Current==null||!accepted||fish is not SObject o||o.Category!=SObject.FishCategory||o.questItem.Value
            ||!string.IsNullOrEmpty(fish.SetFlagOnPickup)||data.IsBossFish||data.UseFishCaughtSeededRandom
            ||!string.IsNullOrEmpty(data.SetFlagOnCatch)||data.CatchLimit>=0||SpiritRules.IsFish(fish.QualifiedItemId)
            ||LobsterKingRules.IsFish(fish.QualifiedItemId)||MutantLobsterRules.IsItem(fish.QualifiedItemId))return;
        if(!DataLoader.Fish(Game1.content).TryGetValue(fish.ItemId,out var row))return;
        var fields=row.Split('/');if(fields.Length<13||fields[1]=="trap")return;
        Current.Ordinary.Add(fish);
    }
    private static void End(ref Item __result,Scope __state)
    {
        if(__result==null)return;
        if(__state.Parent!=null)
        {
            if(__state.Ordinary.Contains(__result))__state.Parent.Ordinary.Add(__result);
            return;
        }
        if(!__state.Weight&&!__state.Eligible||!__state.Ordinary.Contains(__result)||__state.Attempt==null)return;
        var attempt=__state.Attempt;
        if(!attempt.Rolled)
        {
            attempt.Rolled=true;attempt.Result=__result;
            // Rejection weighting of the native conditional ordinary-fish distribution:
            // high-value accept 1, other accept .8 -> normalized weights 1.25:1.
            // Native non-fish/legendary initial branches never reach this method.
            if(__state.Weight&&__result is SObject first&&first.Price<GiantLobsterRules.HighValuePrice&&Game1.random.NextDouble()>=1/GiantLobsterRules.ValueFactor)
            {
                const int maximumProposals=4096;
                bool accepted=false;
                for(int i=0;i<maximumProposals;i++)
                {
                    var candidate=Select(__state.LocationName,__state.Bobber,__state.Depth,__state.Player,false,false,__state.Location,__state.Context);
                    if(candidate is not SObject ordinary||!__state.Ordinary.Contains(candidate))continue;
                    if(ordinary.Price>=GiantLobsterRules.HighValuePrice||Game1.random.NextDouble()<1/GiantLobsterRules.ValueFactor){attempt.Result=candidate;accepted=true;break;}
                }
                if(!accepted)WeightFallbacks++;
                // A bounded compatibility fallback retains the original legal catch if a
                // scripted third-party pool never yields another ordinary candidate.
            }
            attempt.Fallback=attempt.Result;
            if(__state.Eligible&&GiantLobsterRules.SelectSpirit(true,true,BaitKind.GiantLobster,Game1.random.NextDouble()))
            {
                attempt.Result=ItemRegistry.Create(SpiritRules.Fish);
                attempt.Result.SetTempData("IsBossFish",true);
            }
        }
        __result=attempt.Result!;
    }
    private static Exception? Finish(Exception? __exception,Scope? __state)
    {if(__state!=null)Current=__state.Parent;return __exception;}
}

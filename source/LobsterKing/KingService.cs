using System.Text.Json;
using HarmonyLib;
using Eddie.FishingMachine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Buildings;
using StardewValley.GameData.Objects;
using StardewValley.GameData.FishPonds;
namespace BetterStardewValley.LobsterKing;

internal sealed class KingService
{
    private static KingService? Instance;
    internal static bool Ready {get;private set;}
    private readonly IModHelper Helper;private readonly IMonitor Monitor;
    private readonly Dictionary<string,Texture2D> Textures=new();private readonly HashSet<string> Reported=new();
    private readonly HashSet<(long Player,string Token)> Waiting=new();
    private readonly string ModId;
    private const string AcquireMessage="LobsterKing/Acquire";
    internal KingService(IModHelper helper,IMonitor monitor,string modId)
    {
        Ready=false;Helper=helper;Monitor=monitor;ModId=modId;Instance=this;
        var h=new Harmony(modId+".LobsterKing");try
        {
            KingPonds.Install(h);KingSashimi.Install(h);
            foreach(var args in new[]{new[]{typeof(Item)},new[]{typeof(Item),typeof(List<Item>)},new[]{typeof(Item),typeof(int)}})
                h.Patch(DrawPatchTargets.Require(typeof(Farmer),"addItemToInventory",typeof(Item),args),prefix:new HarmonyMethod(typeof(KingService),nameof(BeforeInsert)),postfix:new HarmonyMethod(typeof(KingService),nameof(AfterInsert)));
            h.Patch(DrawPatchTargets.Require(typeof(Farmer),"addItemToInventoryBool",typeof(bool),typeof(Item),typeof(bool)),prefix:new HarmonyMethod(typeof(KingService),nameof(BeforeInsert)),postfix:new HarmonyMethod(typeof(KingService),nameof(AfterInsert)));
        }catch{h.UnpatchAll(h.Id);throw;}
        string Tr(string key)=>BsvI18n.Get(helper,key);
        helper.Events.Content.AssetRequested+=(_,e)=>
        {
            if(e.NameWithoutLocale.StartsWith(KingData.TextureRoot))
            {string name=e.NameWithoutLocale.Name[KingData.TextureRoot.Length..];if(name is "fish" or "sashimi")e.LoadFrom(()=>HdSprites.Handoff(Texture(name,16)),StardewModdingAPI.Events.AssetLoadPriority.Exclusive);}
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))e.Edit(a=>KingData.Objects(a.AsDictionary<string,ObjectData>().Data,Tr,KingData.ApprovedSashimi), BetterStardewValley.BsvAssets.EditPriority);
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/CookingRecipes"))e.Edit(a=>KingData.Cooking(a.AsDictionary<string,string>().Data,Tr,KingData.ApprovedSashimi!=null), BetterStardewValley.BsvAssets.EditPriority);
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/mail"))e.Edit(a=>KingMail.Data(a.AsDictionary<string,string>().Data,Tr,KingData.ApprovedSashimi!=null), BetterStardewValley.BsvAssets.EditPriority);
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/Fish"))e.Edit(a=>KingData.Fish(a.AsDictionary<string,string>().Data), BetterStardewValley.BsvAssets.EditPriority);
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/FishPondData"))e.Edit(a=>KingData.Ponds(a.GetData<List<FishPondData>>()), BetterStardewValley.BsvAssets.EditPriority);
        };
        helper.Events.Content.AssetsInvalidated+=(_,e)=>{if(e.NamesWithoutLocale.Any(n=>n.StartsWith(KingData.TextureRoot))){foreach(var tex in Textures.Values)HdSprites.Unregister(tex);foreach(var tex in Textures.Values.Distinct())tex.Dispose();Textures.Clear();}};
        helper.Events.Player.InventoryChanged+=(_,e)=>
        {
            if(!Context.IsWorldReady||!e.IsLocalPlayer)return;
            foreach(var item in e.Player.Items.Where(i=>i!=null&&LobsterKingRules.IsFish(i.QualifiedItemId)))
                if(item.modData.TryGetValue(LobsterKingRules.ClaimKey,out var token))Request(e.Player,token);
        };
        helper.Events.Multiplayer.ModMessageReceived+=(_,e)=>
        {
            if(!Context.IsWorldReady||!Context.IsMainPlayer||e.FromModID!=ModId||e.Type!=AcquireMessage)return;
            var token=e.ReadAs<string>();if(Guid.TryParseExact(token,"N",out var parsed)&&Waiting.Count<128)Waiting.Add((e.FromPlayerID,token));
        };
        helper.Events.GameLoop.OneSecondUpdateTicked+=(_,_)=>
        {
            if(!Context.IsWorldReady)return;
            RefreshMail();
            if(!Context.IsMainPlayer)return;
            try
            {
                foreach(var request in Waiting.ToArray())
                {
                    var who=Game1.GetPlayer(request.Player,onlyOnline:true);if(who==null)continue;
                    KingWildQuota.Acquire(who,request.Token);
                    var claim=KingWildQuota.Read().Claims.SingleOrDefault(c=>c.Token==request.Token);
                    if(claim==null||claim.Recipient!=0)Waiting.Remove(request);
                }
                Utility.ForEachLocation(location=>{foreach(var pond in location.buildings.OfType<FishPond>())if(LobsterKingRules.IsFish(pond.fishType.Value))KingPonds.Observe(pond);return true;});
            }catch(Exception ex){Error("update",ex);}
        };
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>{Restore();RefreshMail();};
        helper.Events.GameLoop.DayStarted+=(_,_)=>RefreshMail();
        helper.Events.GameLoop.TimeChanged+=(_,_)=>{if(Context.IsWorldReady)KingSashimi.WorldTick(Game1.player);};
        helper.Events.GameLoop.DayEnding+=(_,_)=>{if(Context.IsWorldReady)KingSashimi.End(Game1.player);};
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{Waiting.Clear();Reported.Clear();};
        helper.ConsoleCommands.Add("bsv_king_status",BsvI18n.Get(helper,"cmd.king-status"),(_,_)=>
        {
            monitor.Log("Lobster King: host-authoritative ocean 0.3%, spring/summer/fall, one wild individual per farm/year/season; Giant Lobster 3%; King Crab 1.5%; one shared draw. Pond capacity 1 -> 2: native ordinary Lobster first-gate random material candidates. Sashimi: base health/energy 200, Attack +6, movement +2, effective melee speed +2, native critical points +2 (.04 before native multipliers), 600 game minutes. Native seasoning retained; mail after first acquired wild King.",LogLevel.Info);
            if(Context.IsWorldReady)try{var gate=KingPonds.NativeGate();monitor.Log("King pond material candidates: "+(gate==null?"BLOCKED: native Lobster gate missing":string.Join(" | ",gate)),LogLevel.Info);}catch(Exception ex){Error("pond-status",ex);}
            if(Context.IsWorldReady&&Context.IsMainPlayer)try{var ledger=KingWildQuota.Read();monitor.Log($"Wild available={KingWildQuota.Available()}, claims={ledger.Claims.Count}, prepared={ledger.Claims.Count(c=>!c.Committed)}, first recipients={ledger.FirstAcquired.Count}.",LogLevel.Info);}catch(Exception ex){Error("status",ex);}
        });
        Ready=true;
        monitor.Log("Lobster King capture, native pond upgrade and sashimi enabled.",LogLevel.Debug);
    }
    private static void RefreshMail()
    {
        if(!Context.IsWorldReady||IntegrationHost.Saving||KingData.ApprovedSashimi==null)return;
        try{KingMail.Deliver(Game1.player,KingWildQuota.Read(),Game1.Date.TotalDays,true);}catch(Exception ex){Error("mail",ex);}
    }
    internal static string Text(string key)=>Instance==null?key:BsvI18n.Get(Instance.Helper,key);
    private static void BeforeInsert([HarmonyArgument(0)]Item? item,out string? __state)
    {__state=item!=null&&LobsterKingRules.IsFish(item.QualifiedItemId)&&item.modData.TryGetValue(LobsterKingRules.ClaimKey,out var token)?token:null;}
    private static void AfterInsert(Farmer __instance,string? __state)
    {if(__state!=null&&__instance.IsLocalPlayer)Instance?.Request(__instance,__state);}
    private void Request(Farmer player,string token)
    {
        if(!Guid.TryParseExact(token,"N",out _))return;
        if(Context.IsMainPlayer){try{KingWildQuota.Acquire(player,token);}catch(Exception ex){Error("acquire",ex);}}
        else Helper.Multiplayer.SendMessage(token,AcquireMessage,new[]{ModId},new[]{Game1.MasterPlayer.UniqueMultiplayerID});
    }
    private void Restore()
    {
        if(!Context.IsWorldReady)return;
        try
        {
            if(Context.IsMainPlayer)
            {
                Utility.ForEachItem(item=>
                {
                    if(item is StardewValley.Object obj)
                    {
                        var tokens=new List<string>();
                        if(obj.heldObject.Value?.modData.TryGetValue(LobsterKingRules.ClaimKey,out var held)==true)tokens.Add(held);
                        if(obj is Chest c&&c.modData.TryGetValue(Eddie.FishingMachine.ProductionService.StateKey,out var raw))
                        {
                            var s=ProductionState.Load(raw);tokens.AddRange(s.Pending.Where(p=>p.WildClaim!=null).Select(p=>p.WildClaim!));
                            foreach(var i in c.Items)if(i!=null&&i.modData.TryGetValue(LobsterKingRules.ClaimKey,out var t))tokens.Add(t);
                        }
                        KingWildQuota.ReconcileSource(obj,obj is CrabPot?"pot":"machine",tokens);
                    }
                    return true;
                });
                // Only ledger-backed tokens can be recovered; unknown/spawned/pond fish never qualify.
                foreach(var who in Game1.getOnlineFarmers())foreach(var item in who.Items)
                    if(item!=null&&item.modData.TryGetValue(LobsterKingRules.ClaimKey,out var t))Request(who,t);
                var unresolved=KingWildQuota.Read().Claims.Where(c=>!c.Committed).ToArray();
                if(unresolved.Length>0)Error("prepared",new InvalidOperationException($"{unresolved.Length} prepared King claims lack source proof. Quota retained; inspect source instead of reopening or rerolling."));
            }
            else foreach(var item in Game1.player.Items)if(item!=null&&item.modData.TryGetValue(LobsterKingRules.ClaimKey,out var t))Request(Game1.player,t);
        }catch(Exception ex){Error("load",ex);}
    }
    internal static void Message(string key){if(Instance!=null)Game1.addHUDMessage(new HUDMessage(BsvI18n.Get(Instance.Helper,key),3));}
    internal static void Error(string area,Exception ex){if(Instance?.Reported.Add(area)==true)Instance.Monitor.Log("Lobster King "+area+" failed; existing data retained. "+ex,LogLevel.Error);}
    private Texture2D Texture(string name,int size)
    {
        string key=name+"/"+size;if(Textures.TryGetValue(key,out var tex))return tex;
        var source=Helper.ModContent.Load<Texture2D>("assets/lobster-king/"+name+".png");
        if(size!=16)throw new ArgumentOutOfRangeException(nameof(size),"Native logical handle must be 16px.");
        if(source.Width==16&&source.Height==16)
        {
            var data=new Microsoft.Xna.Framework.Color[256];source.GetData(data);
            var native=new Texture2D(Game1.graphics.GraphicsDevice,16,16);native.SetData(data);
            Textures[key]=native;return native;
        }
        tex=HdSprites.CreateSourceResolution(source,out var full);
        Textures[key]=tex;Textures[name+"/source"]=full;return tex;
    }
}

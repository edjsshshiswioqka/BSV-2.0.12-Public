using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.Objects;
namespace Eddie.FishingMachine;
public sealed class ModEntry
{
    internal IModHelper Helper { get; }
    internal IMonitor Monitor { get; }
    internal IManifest ModManifest { get; }
    private string PatchOwner => ModManifest.UniqueID + ".FishingMachine";
    internal ModEntry(IModHelper helper, IMonitor monitor, IManifest manifest)
    { Helper=helper; Monitor=monitor; ModManifest=manifest; }
    internal const string Id="Eddie.FishingMachine_Machine", QualifiedId="(BC)"+Id, Tag="Eddie.FishingMachine/";
    internal const string CrabId=MachineKinds.CrabId, CrabQualifiedId="(BC)"+CrabId;
    internal static ModEntry Instance=null!;
    internal CrabSettings CrabConfig=new();
    internal WorldService World=null!;
    internal ProductionService Production=null!;
    internal CaveMachineStorage Caves=null!;
    internal bool Saving;
    internal Artwork Art=null!;
    internal NetworkActions Actions=null!;
    internal readonly MachineIndex Machines=new();
    internal readonly PerScreen<Chest?> OpenChest=new(()=>null);
    private readonly PerScreen<long> ExplicitCloseAt=new(()=>long.MinValue);
    private readonly PerScreen<int> LastLevel=new(()=>-1);
    internal bool Ready;
    internal BalanceConfig Config=new();
    internal string T(string key, object? tokens=null)
    {
        if(tokens==null&&key is "machine.description" or "machine.lv2-description" or "machine.lv3-description" or "crab.description" or "crab.lv2-description")
        {
            bool crab=key.StartsWith("crab.");int level=key.Contains("lv3")?3:key.Contains("lv2")?2:1;
            var materials=crab?CrabRules.Upgrade(false):MachineUpgrade.Requirements(level);
            int cycles=MachineCycleRules.RequiredCycles(crab?MachineKinds.Crab:MachineKinds.Fishing,level);
            tokens=new{days=BetterStardewValley.BsvI18n.Number(Helper,(crab?CrabConfig.MinutesForLevel(level):Config.MinutesForLevel(level))/1440d,"0.####"),
                bait=crab?CrabConfig.Required(level,false):MachineUpgrade.RequiredBait(new(){Level=level},Config),
                cycles=cycles==0?T("machine.max-level"):cycles.ToString(),
                materials=cycles==0?"—":string.Join("; ",materials.Select(r=>ItemRegistry.GetDataOrErrorItem(r.Id).DisplayName+" ×"+r.Count)),
                lure=crab?CrabConfig.Required(level,true):0,capacity=crab?CrabRules.Capacity:MachineUpgrade.Capacity(level)};
        }
        if(tokens==null&&key=="recipe.materials")tokens=new{materials=string.Join("; ",PreparationRules.Requirements.Select(r=>ItemRegistry.GetDataOrErrorItem(r.Id).DisplayName+" ×"+r.Count))};
        if(tokens==null&&key is "crab.upgrade-materials" or "crab.upgrade-help" or "crab.upgrade-ready")tokens=new{materials=string.Join("; ",CrabRules.Upgrade(false).Select(r=>ItemRegistry.GetDataOrErrorItem(r.Id).DisplayName+" ×"+r.Count))};
        return BetterStardewValley.BsvI18n.Get(Helper,key,tokens);
    }
    internal void Message(string key)=>Game1.showRedMessage(T(key));
    internal void Entry(IModHelper helper)
    {
        Instance=this; World=new(this); Production=new(this); Actions=new(this); Art=new(this); Caves=new(this);
        // Register content before installing patches, then refresh any preloaded recipe table.
        helper.Events.Content.AssetRequested += OnAssetRequested;
        try
        {
            Config=BetterStardewValley.Configuration.MachineBalanceMigration.Load<BalanceConfig>(helper,Monitor,"fishing-machine.json",false,c=>{c.Migrate();c.Validate();});
            CrabConfig=BetterStardewValley.Configuration.MachineBalanceMigration.Load<CrabSettings>(helper,Monitor,"crab-catcher.json",true,c=>c.Validate());
            Art.Load();
            helper.GameContent.InvalidateCache("Data/BigCraftables");
            helper.GameContent.InvalidateCache("Data/CraftingRecipes");
            // Also refresh the game's static dictionary if another mod initialized it earlier.
            CraftingRecipe.craftingRecipes = DataLoader.CraftingRecipes(Game1.content);
            if (!CraftingRecipe.craftingRecipes.ContainsKey(Id)
                || !DataLoader.BigCraftables(Game1.content).ContainsKey(Id)
                || !CraftingRecipe.craftingRecipes.ContainsKey(CrabId) || !DataLoader.BigCraftables(Game1.content).ContainsKey(CrabId))
                throw new InvalidOperationException("Fishing machine item or recipe registration failed.");
            new Harmony(PatchOwner).PatchAll(typeof(ModEntry).Assembly);
            Ready=true;
        }
        catch
        {
            Ready=false;
            new Harmony(PatchOwner).UnpatchAll(PatchOwner);
            helper.Events.Content.AssetRequested -= OnAssetRequested;
            helper.GameContent.InvalidateCache("Data/BigCraftables");
            helper.GameContent.InvalidateCache("Data/CraftingRecipes");
            throw;
        }
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>{LastLevel.Value=-1; Unlock(); Caves.Load(); Machines.Rebuild();World.Audit();Actions.Tick();Production.Tick();};
        helper.Events.GameLoop.DayStarted+=(_,_)=>{Saving=false;Unlock();Caves.Restore();Machines.Rebuild();World.Audit();ObserveTimeline();Actions.Tick();Production.Tick();Production.LogAllFishPools();};
        helper.Events.GameLoop.TimeChanged+=(_,_)=>{ObserveTimeline();Production.Tick();};
        helper.Events.GameLoop.DayEnding+=(_,_)=>ObserveTimeline(true);
        helper.Events.Content.AssetsInvalidated+=(_,e)=>
        {if(e.NamesWithoutLocale.Any(n=>n.IsEquivalentTo("Data/Fish")||n.IsEquivalentTo("Data/Locations")||n.IsEquivalentTo("Data/Objects")||n.BaseName.StartsWith("Maps/",StringComparison.Ordinal)))ObserveTimeline();};
        helper.Events.World.ObjectListChanged+=Machines.Changed;
        helper.Events.World.LocationListChanged+=(_,_)=>{if(Context.IsWorldReady && Context.IsMainPlayer)Machines.Rebuild();};
        helper.Events.GameLoop.UpdateTicked+=(_,_)=>{if(Context.IsWorldReady&&Context.IsMainPlayer&&!Saving&&Production.HasBacklog)Production.Tick();};
        helper.Events.GameLoop.OneSecondUpdateTicked+=(_,_)=>{if(Context.IsWorldReady) { Unlock(); ObserveTimeline(weatherOnly:true); Actions.Tick(); Production.Tick(); }};
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{OpenChest.Value=null;LastLevel.Value=-1;Production.Reset();TimelineEnvironment.Reset();Actions.Reset();Caves.Reset();Machines.Reset();World.Reset();Art.Reset();Saving=false;};
        helper.Events.GameLoop.Saving+=(_,_)=>{Saving=true;Caves.Save();};
        helper.Events.GameLoop.Saved+=(_,_)=>Saving=false;
        helper.Events.Display.RenderedActiveMenu+=(_,e)=>Production.DrawMenu(e.SpriteBatch);
        helper.Events.Input.ButtonPressed+=OnButton;
        helper.Events.Display.RenderedWorld+=(_,e)=>World.DrawPreview(e.SpriteBatch);
        helper.Events.Display.MenuChanged+=(_,e)=>
        {
            // Native chest transfers rebuild the menu; its context, not the menu instance, identifies ownership.
            if(e.NewMenu is not StardewValley.Menus.ItemGrabMenu menu || !ReferenceEquals(menu.context,OpenChest.Value))
            {ReleaseChest(Environment.TickCount64-ExplicitCloseAt.Value is >=0 and <250);ExplicitCloseAt.Value=long.MinValue;}
        };
        helper.Events.Player.Warped+=(_,e)=>{if(e.IsLocalPlayer){ReleaseChest();World.Reset();}};
        helper.Events.Multiplayer.ModMessageReceived+=Actions.OnMessage;
        helper.Events.Multiplayer.PeerConnected+=(_,e)=>
        {
            var mod=e.Peer.GetMod(ModManifest.UniqueID);
            if(mod==null || mod.Version.ToString()!=ModManifest.Version.ToString())Monitor.Log(T("multiplayer.version"),LogLevel.Warn);
        };
        helper.ConsoleCommands.Add("bsv_cave_recover",T("cmd.cave-recover"),(_,_)=>Caves.Recover());
        helper.ConsoleCommands.Add("bsv_sewer_status",T("cmd.sewer-status"),(_,args)=>
        {
            if(!Context.IsWorldReady)return;
            try
            {
                var tile=helper.Input.GetCursorPosition().Tile;
                if(args.Length==2&&int.TryParse(args[0],out int x)&&int.TryParse(args[1],out int y))tile=new(x,y);
                else if(args.Length!=0){Monitor.Log(T("cmd.sewer-usage"),LogLevel.Info);return;}
                World.Diagnose(tile);
            }catch(Exception ex){Monitor.Log(T("diag.sewer-error")+"\n"+ex,LogLevel.Error);}
        });
        helper.ConsoleCommands.Add("eddie_fishing_info",T("cmd.fishing-info"),(_,_)=>
        {
            Monitor.Log($"BSV Fishing Machine: Lv2 after {MachineCycleRules.RequiredCycles(MachineKinds.Fishing,1)} cycles; Lv3 after {MachineCycleRules.RequiredCycles(MachineKinds.Fishing,2)} additional Lv2 cycles. Crab Lv2 after {MachineCycleRules.RequiredCycles(MachineKinds.Crab,1)} cycles (MAX). Default duration: {MachineCycleRules.DefaultMinutes} calendar minutes; actual configured durations below.",LogLevel.Info);
            Monitor.Log($"Lv1 {Config.BaitPerCycle} bait, Lv2 {Config.LevelTwoBaitPerCycle} bait, Lv3 {Config.LevelThreeBaitPerCycle} bait / {Config.CycleGameMinutes} calendar minutes; critical Lv1={Config.FishCriticalChance:P0}, Lv2={Config.LevelTwoCriticalChance:P0}; Lv3 double={Config.LevelThreeCriticalChance:P0}, triple={Config.LevelThreeTripleChance:P0}.",LogLevel.Info);
        });
        helper.ConsoleCommands.Add("eddie_fishing_simulate",T("cmd.fishing-simulate"),(_,args)=>
        {
            if(args.Length<1 || !Enum.TryParse<BaitKind>(args[0],true,out var k) || !Enum.IsDefined(typeof(BaitKind),k))return;
            int seed=args.Length>1 && int.TryParse(args[1],out var n)?n:20260928;
            int level=args.Length>2 && int.TryParse(args[2],out var supplied)?supplied:k==BaitKind.Challenge?2:1;
            if(level is <1 or >3 || level==1 && k==BaitKind.Challenge)return;
            var r=CatchRoller.Simulate(Config,k,seed,level);
            Monitor.Log($"SIMULATION: fish={r.ProducedFish}, target={r.ProducedTargetFish}, critical bonus={r.CriticalFish}, other={r.OtherItems}, empty={r.Nothing}.",LogLevel.Info);
        });
    }
    private string? TimelineObservationError;
    private void ObserveTimeline(bool closeDay=false,bool weatherOnly=false)
    {
        if(!Context.IsWorldReady||!Context.IsMainPlayer||Saving)return;
        try{if(weatherOnly&&!TimelineEnvironment.WeatherChanged(this))return;TimelineEnvironment.Observe(this,closeDay);TimelineObservationError=null;}
        catch(Exception ex)
        {if(TimelineObservationError!=ex.Message){TimelineObservationError=ex.Message;Monitor.Log("Machine environment observation failed; unknown windows use conservative ecology: "+ex,LogLevel.Error);}}
    }
    private void OnAssetRequested(object? s,AssetRequestedEventArgs e)
    {
        if(e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))e.Edit(a=>a.AsDictionary<string,BigCraftableData>().Data[Id]=new()
        {Name=Id,DisplayName=T("machine.name"),Description=T("machine.description"),Price=0,Fragility=2,
         CanBePlacedIndoors=true,CanBePlacedOutdoors=true,Texture="Mods/Eddie.BetterStardewValley/FishingIcon",SpriteIndex=0,ContextTags=new(){"eddie_fishing_machine"}});
        if(e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))e.Edit(a=>a.AsDictionary<string,BigCraftableData>().Data[CrabId]=new()
        {Name=CrabId,DisplayName=T("crab.name"),Description=T("crab.description"),Price=0,Fragility=2,
         CanBePlacedIndoors=true,CanBePlacedOutdoors=true,Texture="Mods/Eddie.BetterStardewValley/CrabIcon",SpriteIndex=0,ContextTags=new(){"bsv_crab_catcher"}});
        if(e.NameWithoutLocale.IsEquivalentTo("Mods/Eddie.BetterStardewValley/FishingIcon"))e.LoadFrom(()=>BetterStardewValley.HdSprites.WideIconHandle(Helper.ModContent.Load<Texture2D>("assets/icon.png")),AssetLoadPriority.Exclusive);
        if(e.NameWithoutLocale.IsEquivalentTo("Mods/Eddie.BetterStardewValley/CrabIcon"))e.LoadFromModFile<Texture2D>("assets/crab-catcher/native-icon.png",AssetLoadPriority.Exclusive);
        if(e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))e.Edit(a=>a.AsDictionary<string,string>().Data[CrabId]=
            $"{string.Join(" ",CrabRules.Craft(false).Select(r=>$"{r.Id} {r.Count}"))}/Home/{CrabId} 1/true/Fishing 6/{T("crab.name")}");
        if(e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))e.Edit(a=>a.AsDictionary<string,string>().Data[Id]=
            $"{string.Join(" ",PreparationRules.Requirements.Select(r=>$"{r.Id} {r.Count}"))}/Home/{Id} 1/true/Fishing 5/{T("machine.name")}");
    }
    private void Unlock()
    {
        if(!Context.IsWorldReady)return;
        int level=Game1.player.fishingLevel.Value;
        if(level>=6 && !Game1.player.craftingRecipes.ContainsKey(CrabId))
        {Game1.player.craftingRecipes.Add(CrabId,0);Monitor.Log(T("crab.unlocked"),LogLevel.Info);}
        if(level==LastLevel.Value && Game1.player.craftingRecipes.ContainsKey(Id))return;
        LastLevel.Value=level;
        if(level>=5 && !Game1.player.craftingRecipes.ContainsKey(Id))
        {Game1.player.craftingRecipes.Add(Id,0);Monitor.Log(T("recipe.unlocked"),LogLevel.Info);}
    }
    private void OnButton(object? s,ButtonPressedEventArgs e)
    {
        if(!Ready || !Context.IsWorldReady || Helper.Input.IsSuppressed(e.Button))return;
        if(OpenChest.Value!=null && Game1.activeClickableMenu is StardewValley.Menus.ItemGrabMenu opened
            && ReferenceEquals(opened.context,OpenChest.Value))
        {
            bool close=e.Button is SButton.Escape or SButton.ControllerB;
            if(e.Button==SButton.MouseLeft)
            {Game1.PushUIMode();try{int x=Game1.getMouseX(),y=Game1.getMouseY();close=opened.upperRightCloseButton?.containsPoint(x,y)==true||opened.okButton?.containsPoint(x,y)==true;}finally{Game1.PopUIMode();}}
            ExplicitCloseAt.Value=close?Environment.TickCount64:long.MinValue;
        }
        if(Production.MenuButton(e.Button)){Helper.Input.Suppress(e.Button);return;}
        if(!Context.IsPlayerFree)return;
        // SMAPI input runs before the toolbar: never consume clicks intended for HUD controls.
        if (e.Button is SButton.MouseLeft or SButton.MouseRight)
        {
            Game1.PushUIMode();
            try
            {
                int x=Game1.getMouseX(), y=Game1.getMouseY();
                if (Game1.onScreenMenus.Any(menu => menu.isWithinBounds(x,y))) return;
            }
            finally { Game1.PopUIMode(); }
        }
        bool action=e.Button.IsActionButton(),tool=e.Button.IsUseToolButton();
        if(!action && !tool)return;
        var tile=Game1.options.gamepadControls?e.Cursor.GrabTile:e.Cursor.Tile;
        if(IsMachine(Game1.player.ActiveObject)){Helper.Input.Suppress(e.Button);World.Place(e.Cursor);return;}
        if(!World.TryAnchor(Game1.currentLocation,tile,out var anchor))return;
        Helper.Input.Suppress(e.Button);
        if(action)World.Open(anchor!);
        else if(Game1.player.CurrentTool is StardewValley.Tools.Axe or StardewValley.Tools.Pickaxe)World.Remove(anchor!);
    }
    internal void ReleaseChest(bool startCrab=false)
    {
        var c=OpenChest.Value;
        // Publish intent while the native inventory mutex is still held. Automate cannot
        // slip a previous operator's start between closing the menu and host receipt.
        if(startCrab && c!=null && IsMachine(c) && Context.IsWorldReady && !Saving && !Actions.HasPending
            && Game1.currentLocation==c.Location && Production.Phase(c) is "idle" or "ready")Actions.Control(c,ActionKind.Start);
        if(c!=null && c.GetMutex().IsLockHeld())c.GetMutex().ReleaseLock();
        OpenChest.Value=null;
    }
    internal static bool IsMachine(Item? i)=>i?.QualifiedItemId is QualifiedId or CrabQualifiedId;
    internal static bool IsCrab(Item? i)=>i?.QualifiedItemId==CrabQualifiedId;
    internal static bool IsRecipe(string id)=>id is Id or CrabId;
    internal static bool IsPart(StardewValley.Object? o)=>o!=null && o.modData.ContainsKey(Tag+"MachineId");
    internal static bool IsAnchor(StardewValley.Object? o)=>o is Chest && IsPart(o) && o.modData.TryGetValue(Tag+"Role",out var r) && r=="anchor";
    internal void DrawIcon(SpriteBatch b,Vector2 p,float size,float alpha,float depth,bool crab=false)=>Art.DrawIcon(b,p,size,alpha,depth,crab:crab);
}

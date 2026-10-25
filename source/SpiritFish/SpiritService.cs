using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Tools;
using StardewValley.GameData.Locations;
using StardewValley.GameData.Objects;
using StardewValley.GameData.FishPonds;
using StardewValley.Menus;
using StardewValley.Locations;
using SObject=StardewValley.Object;

namespace BetterStardewValley.SpiritFish;

internal sealed class SpiritService
{
    internal static SpiritService Instance=null!;
    internal readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private static readonly Color[] PondColors={new(40,230,235),new(65,165,255),new(145,90,245),new(235,100,235),new(110,245,205)};
    internal string Tr(string key)=>BsvI18n.Get(Helper,key);
    internal SpiritService(IModHelper helper,IMonitor monitor,string modId)
    {
        Instance=this;Helper=helper;Monitor=monitor;
        var h=new Harmony(modId+".SpiritFish");
        try
        {
            void Patch(Type type,string name,Type returns,string? prefix,string? postfix,params Type[] args)=>h.Patch(
                DrawPatchTargets.Require(type,name,returns,args),prefix:prefix==null?null:new HarmonyMethod(typeof(SpiritService),prefix),
                postfix:postfix==null?null:new HarmonyMethod(typeof(SpiritService),postfix){priority=Priority.Last});
            Patch(typeof(GameLocation),"CheckGenericFishRequirements",typeof(bool),null,nameof(Requirements),typeof(Item),typeof(Dictionary<string,string>),typeof(GameLocation),typeof(Farmer),typeof(SpawnFishData),typeof(int),typeof(bool),typeof(bool),typeof(bool),typeof(bool));
            Patch(typeof(FishingRod),"pullFishFromWater",typeof(void),nameof(Landing),null,typeof(string),typeof(int),typeof(int),typeof(int),typeof(bool),typeof(bool),typeof(bool),typeof(string),typeof(bool),typeof(int));
            Patch(typeof(SObject),"get_DisplayName",typeof(string),null,nameof(DisplayName));
            Patch(typeof(SObject),"staminaRecoveredOnConsumption",typeof(int),null,nameof(Energy));
            Patch(typeof(SObject),"healthRecoveredOnConsumption",typeof(int),null,nameof(Health));
            Patch(typeof(Farmer),"caughtFish",typeof(bool),nameof(BeforeCaught),nameof(AfterCaught),typeof(string),typeof(int),typeof(bool),typeof(int));
            Patch(typeof(CraftingPage),"clickCraftingRecipe",typeof(void),nameof(Cook),null,typeof(ClickableTextureComponent),typeof(bool));
            Patch(typeof(Farmer),"doneEating",typeof(void),null,nameof(DoneEating));
            SpiritLuck.Install(h,helper,monitor);
            Patch(typeof(NPC),"getGiftTasteForThisItem",typeof(int),null,nameof(Taste),typeof(Item));
            Patch(typeof(NPC),"receiveGift",typeof(void),nameof(Gift),null,typeof(SObject),typeof(Farmer),typeof(bool),typeof(float),typeof(bool));
            Patch(typeof(FishPond),"SpawnFish",typeof(void),nameof(Spawn),null);
            Patch(typeof(FishPond),"GetWaterColor",typeof(Color?),null,nameof(Water),typeof(Vector2));
            Patch(typeof(FishPond),"draw",typeof(void),null,nameof(PondDraw),typeof(SpriteBatch));
            SpiritArt.Install(h,helper,monitor);
            SpiritSelection.Install(h);
        }
        catch {h.UnpatchAll(h.Id);throw;}
        _=new SpiritQuota(helper,monitor);
        helper.Events.Content.AssetRequested+=Assets;
        helper.Events.Content.AssetReady+=(_,e)=>
        { if(Context.IsWorldReady&&e.NameWithoutLocale.IsEquivalentTo("Data/FishPondData"))SpiritPondRewards.ValidateLoaded(monitor); };
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>SpiritSelection.Clear();
        helper.Events.Player.Warped+=(_,e)=>{if(e.IsLocalPlayer)SpiritSelection.ResetPlayer(e.Player);};
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>{Learn();SpiritPondRewards.ValidateLoaded(monitor);};
        helper.Events.GameLoop.DayStarted+=(_,_)=>Learn();
        helper.Events.GameLoop.TimeChanged+=(_,_)=>Learn();
        // The blessing shows as a buff icon while the soup's luck lasts (the luck itself lives in SpiritLuck).
        helper.Events.GameLoop.TimeChanged+=(_,_)=>ReconcileBuff();
        helper.Events.GameLoop.OneSecondUpdateTicked+=(_,_)=>ReconcileBuff();
        helper.Events.GameLoop.DayStarted+=(_,_)=>ReconcileBuff();
        helper.ConsoleCommands.Add("bsv_spirit_status",Tr("cmd.spirit-status"),(_,_)=>Status());
        monitor.Log("Spirit Fish registered; native processing, annual quota and soup hooks installed.",LogLevel.Debug);
    }
    private void Assets(object? sender,AssetRequestedEventArgs e)
    {
        if(e.NameWithoutLocale.StartsWith(SpiritRules.TextureRoot))
        {
            string name=e.NameWithoutLocale.Name[SpiritRules.TextureRoot.Length..];
            if(name is "fish" or "soup")e.LoadFrom(()=>SpiritArt.Texture(name,16),AssetLoadPriority.Exclusive);
        }
        else if(e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))e.Edit(a=>SpiritData.Objects(a.AsDictionary<string,ObjectData>().Data,Tr), BetterStardewValley.BsvAssets.EditPriority);
        else if(e.NameWithoutLocale.IsEquivalentTo("Data/Fish"))e.Edit(a=>SpiritData.Fish(a.AsDictionary<string,string>().Data), BetterStardewValley.BsvAssets.EditPriority);
        else if(e.NameWithoutLocale.IsEquivalentTo("Data/Locations"))e.Edit(a=>SpiritData.Locations(a.AsDictionary<string,LocationData>().Data), BetterStardewValley.BsvAssets.EditPriority);
        else if(e.NameWithoutLocale.IsEquivalentTo("Data/CookingRecipes"))e.Edit(a=>SpiritData.Cooking(a.AsDictionary<string,string>().Data,Tr), BetterStardewValley.BsvAssets.EditPriority);
        else if(e.NameWithoutLocale.IsEquivalentTo("Data/FishPondData"))e.Edit(a=>SpiritData.Ponds(a.GetData<List<FishPondData>>()), BetterStardewValley.BsvAssets.EditPriority);
    }
    private static void Learn()
    {
        if(!Context.IsWorldReady)return;
        var who=Game1.player;
        if(who.fishingLevel.Value>=10&&!who.cookingRecipes.ContainsKey(SpiritRules.SoupId))who.cookingRecipes.Add(SpiritRules.SoupId,0);
        if(who.modData.TryGetValue(SpiritRules.LuckKey,out var day)&&day!=Game1.Date.TotalDays.ToString())who.modData.Remove(SpiritRules.LuckKey);
    }
    internal static bool Eligible(Farmer owner,GameLocation location)=>SpiritRules.Eligible(owner.fishingLevel.Value,Game1.timeOfDay,location.NameOrUniqueName);
    private static void Requirements(Item fish,[HarmonyArgument(4)]SpawnFishData data,ref bool __result)
    {
        if(SpiritRules.IsFish(fish?.QualifiedItemId)||Eddie.FishingMachine.Core.LobsterKingRules.IsFish(fish?.QualifiedItemId)
            ||Eddie.FishingMachine.Core.MutantLobsterRules.IsItem(fish?.QualifiedItemId))__result=false; // No independent data-row entry.
        SpiritSelection.Observe(fish,data,__result);
    }
    private static void BeforeCaught(Farmer __instance,string itemId,bool from_fish_pond,out int __state)
    {__state=SpiritRules.IsFish(itemId)&&!from_fish_pond&&__instance.fishCaught.TryGetValue(SpiritRules.Fish,out var count)?count[0]:0;}
    private static void AfterCaught(Farmer __instance,string itemId,bool from_fish_pond,int numberCaught,int __state)
    {if(SpiritRules.IsFish(itemId)&&!from_fish_pond&&numberCaught>0&&__instance.fishCaught.TryGetValue(SpiritRules.Fish,out var count)&&count[0]>__state)SpiritQuota.Caught(__instance);}
    private static bool Cook(CraftingPage __instance,ClickableTextureComponent c)
    {
        var recipe=__instance.pagesOfCraftingRecipes[__instance.currentCraftingPage][c];
        if(recipe.name!=SpiritRules.SoupId)return true;
        if(__instance.cooking && (Game1.player.currentLocation is FarmHouse home&&home.upgradeLevel>=1 || Game1.player.currentLocation is IslandFarmHouse))return true;
        Game1.addHUDMessage(new HUDMessage(Instance.Tr("spirit.soup.kitchen"),3));return false;
    }
    private static bool Landing(FishingRod __instance,ref string fishId,bool fromFishPond,ref int numCaught)
    {
        if(fromFishPond)return true;
        var who=__instance.getLastFarmerToUse();
        if(Eddie.FishingMachine.Core.LobsterKingRules.IsFish(fishId)||Eddie.FishingMachine.Core.MutantLobsterRules.IsItem(fishId))
        {if(who!=null)__instance.doneFishing(who,false);return false;}
        if(!SpiritRules.IsFish(fishId))return true;
        if(who!=null&&SpiritQuota.CanCatch(who)&&SpiritSelection.ValidCapture(__instance,who,out var attempt)&&SpiritRules.IsFish(attempt!.Result?.QualifiedItemId))
        {numCaught=SpiritRules.Quantity(numCaught);return true;}
        if(who!=null)__instance.doneFishing(who,false);
        return false;
    }
    private static void DisplayName(SObject __instance,ref string __result)
    {
        string? kind=SpiritArt.Kind(__instance);
        if(kind is "smoked" or "roe" or "aged_roe")__result=Instance.Tr("spirit."+kind+".name");
    }
    private static void Energy(SObject __instance,ref int __result){if(__instance.QualifiedItemId==SpiritRules.Soup)__result=225;}
    private static void Health(SObject __instance,ref int __result){if(__instance.QualifiedItemId==SpiritRules.Soup)__result=225;}
    private static void DoneEating(Farmer __instance)
    {if(__instance.IsLocalPlayer&&__instance.itemToEat?.QualifiedItemId==SpiritRules.Soup){__instance.modData[SpiritRules.LuckKey]=Game1.Date.TotalDays.ToString();Instance.ReconcileBuff();}}
    private Texture2D? BuffIcon;
    private void ReconcileBuff()
    {
        if(!Context.IsWorldReady)return;
        var who=Game1.player;
        if(!SpiritLuck.Active(who)){if(who.hasBuff(SpiritRules.LuckBuff))who.buffs.Remove(SpiritRules.LuckBuff);return;}
        if(who.hasBuff(SpiritRules.LuckBuff))return;
        BuffIcon??=Helper.ModContent.Load<Texture2D>("assets/buffs/spirit_luck.png");
        // Display only: no stat effects here, so the luck values are never applied twice.
        who.applyBuff(new Buff(SpiritRules.LuckBuff,SpiritRules.SoupId,Tr("spirit.soup.name"),Buff.ENDLESS,BuffIcon,0,
            displayName:Tr("spirit.buff.name"),description:Tr("spirit.buff.description")));
    }
    // ---- Rainbow pond surface. Vanilla tints the whole water layer with ONE colour (overrideWaterColor, applied
    // the morning after the fish moves in). From that morning on, the Spirit pond's water layer is drawn again on
    // top with a colour per spot, at the same position/scale and just above the native water, below the waves.
    private static readonly Dictionary<Texture2D,Texture2D> RainbowWater=new();
    private static void PondDraw(FishPond __instance,SpriteBatch __0)
    {
        if(__instance.isMoving||__instance.daysOfConstructionLeft.Value>0||__instance.FishCount<=0||!SpiritRules.IsFish(__instance.fishType.Value))return;
        if(__instance.overrideWaterColor.Value==Color.White)return;
        var source=__instance.texture.Value;if(source==null||source.IsDisposed)return;
        if(!RainbowWater.TryGetValue(source,out var water)||water.IsDisposed){water=MakeRainbowWater(source);RainbowWater[source]=water;}
        __0.Draw(water,Game1.GlobalToLocal(Game1.viewport,new Vector2(__instance.tileX.Value*64,__instance.tileY.Value*64+__instance.tilesHigh.Value*64)),
            null,Color.White*__instance.alpha,0f,new Vector2(0f,80f),4f,SpriteEffects.None,((__instance.tileY.Value+.5f)*64f-2.9f)/10000f);
    }
    internal static Color RainbowAt(float tileX,float tileY)
    {
        var colors=PondColors;
        double phase=(tileX*.55+tileY*.35)%colors.Length;if(phase<0)phase+=colors.Length;int index=(int)phase;
        return Color.Lerp(colors[index],colors[(index+1)%colors.Length],(float)(phase-index));
    }
    private static Texture2D MakeRainbowWater(Texture2D source)
    {
        var area=new Rectangle(0,80,80,80);var pixels=new Color[area.Width*area.Height];
        source.GetData(0,area,pixels,0,pixels.Length);
        for(int y=0;y<area.Height;y++)for(int x=0;x<area.Width;x++)
        {
            int i=y*area.Width+x;var c=pixels[i];if(c.A==0)continue;
            var t=RainbowAt(x/16f,y/16f);   // 16 source pixels = one tile
            pixels[i]=new Color(c.R*t.R/255,c.G*t.G/255,c.B*t.B/255,(int)c.A);
        }
        var texture=new Texture2D(Game1.graphics.GraphicsDevice,area.Width,area.Height);texture.SetData(pixels);return texture;
    }
    private static void Taste(Item item,ref int __result){if(item?.QualifiedItemId==SpiritRules.Soup)__result=0;}
    private static void Gift(SObject o,ref float friendshipChangeMultiplier)
    {if(o?.QualifiedItemId==SpiritRules.Soup)friendshipChangeMultiplier=8f;} // native birthday branch assigns 8, it does not multiply again.
    private static bool Spawn(FishPond __instance)=>!SpiritRules.IsFish(__instance.fishType.Value);
    private static void Water(FishPond __instance,Vector2 tile,ref Color? __result)
    {
        if(__instance.FishCount<=0||!SpiritRules.IsFish(__instance.fishType.Value))return;
        // Spatial rainbow, not flashing color or shared net-field writes. Native water animation remains.
        var colors=PondColors;
        double phase=((tile.X-__instance.tileX.Value)*.55+(tile.Y-__instance.tileY.Value)*.35)%colors.Length;
        if(phase<0)phase+=colors.Length;int index=(int)phase;
        __result=Color.Lerp(colors[index],colors[(index+1)%colors.Length],(float)(phase-index));
    }
    private void Status()
    {
        if(!Context.IsWorldReady){Monitor.Log(Tr("diag.load-save"),LogLevel.Info);return;}
        var rows=DataLoader.Fish(Game1.content);
        SpiritPondRewards.ValidateLoaded(Monitor,LogLevel.Info);
        Monitor.Log($"Spirit Fish: Mountain, Fishing >=10, 1800–2600, Giant Lobster Bait required; chance 1% per eligible ordinary fish opportunity; loaded Legend difficulty/movement={string.Join('/',rows["163"].Split('/').Skip(1).Take(2))}; own={string.Join('/',rows[SpiritRules.Id].Split('/').Skip(1).Take(2))}; yearly available={AnnualLegendaryFeature.MachineAvailable(Game1.player,SpiritRules.Fish,Game1.year)}; machine reserved={Eddie.FishingMachine.MachineLegendaryBridge.Reserved(Game1.player.UniqueMultiplayerID,SpiritRules.Fish,Game1.year)}.",LogLevel.Info);
    }
}

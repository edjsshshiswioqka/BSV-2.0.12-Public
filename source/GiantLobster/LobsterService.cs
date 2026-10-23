using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Enums;
using StardewValley;
using StardewValley.GameData.Objects;
using Eddie.FishingMachine.Core;
namespace BetterStardewValley.GiantLobster;

internal sealed class LobsterService
{
    private readonly Dictionary<string,Texture2D> Textures=new();
    private readonly IModHelper Helper;
    internal LobsterService(IModHelper helper,IMonitor monitor,string modId)
    {
        Helper=helper;var harmony=new Harmony(modId+".GiantLobster");
        try {LobsterCrabPots.Install(harmony);LobsterBaitBar.Install(harmony);}catch{harmony.UnpatchAll(harmony.Id);throw;}
        string Tr(string key)=>BsvI18n.Get(helper,key,key=="lobster.bait.description"?new{
            barPercent=BsvI18n.Number(helper,(GiantLobsterRules.BarFactor-1)*100,"0"),price=GiantLobsterRules.HighValuePrice,
            weightPercent=BsvI18n.Number(helper,(GiantLobsterRules.ValueFactor-1)*100,"0")}:null);
        helper.Events.Content.AssetRequested+=(_,e)=>
        {
            if(e.NameWithoutLocale.StartsWith(LobsterData.TextureRoot))
            {string name=e.NameWithoutLocale.Name[LobsterData.TextureRoot.Length..];if(name is "fish" or "bait" or "dinner")e.LoadFrom(()=>Texture(name,16),AssetLoadPriority.Exclusive);}
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))e.Edit(a=>LobsterData.Objects(a.AsDictionary<string,ObjectData>().Data,Tr));
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/Fish"))e.Edit(a=>LobsterData.Fish(a.AsDictionary<string,string>().Data),AssetEditPriority.Late);
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))e.Edit(a=>LobsterData.Crafting(a.AsDictionary<string,string>().Data,Tr));
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/CookingRecipes"))e.Edit(a=>LobsterData.Cooking(a.AsDictionary<string,string>().Data,Tr));
        };
        helper.Events.Content.AssetsInvalidated+=(_,e)=>
        {if(e.NamesWithoutLocale.Any(n=>n.StartsWith(LobsterData.TextureRoot))) {foreach(var tex in Textures.Values)HdSprites.Unregister(tex);foreach(var tex in Textures.Values.Distinct())tex.Dispose();Textures.Clear();}};
        void Learn(){if(Context.IsWorldReady)LobsterData.Learn(Game1.player);}
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>Learn();
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>
        {
            if(!Context.IsWorldReady)return;
            if(Context.IsMainPlayer)Utility.ForEachItem(LobsterMigration.RepairPreview);
            else foreach(var item in Game1.player.Items)if(item!=null)LobsterMigration.RepairPreview(item);
        };
        helper.Events.Player.InventoryChanged+=(_,e)=>
        {
            if(Context.IsWorldReady&&e.IsLocalPlayer)
                foreach(var item in e.Added)LobsterMigration.RepairPreview(item);
        };
        helper.Events.GameLoop.DayStarted+=(_,_)=>Learn();
        helper.Events.Player.LevelChanged+=(_,e)=>{if(e.IsLocalPlayer&&e.Skill==SkillType.Fishing&&Context.IsWorldReady)LobsterData.Learn(e.Player);};
        helper.ConsoleCommands.Add("bsv_lobster_status",BsvI18n.Get(helper,"cmd.lobster-status"),(_,_)=>
            monitor.Log($"Giant Lobster: ocean capture={GiantLobsterRules.CrabChance:P0} of eligible ocean base opportunities (shared mutually exclusive rare selector); bait={GiantLobsterRules.Bait}; Spirit chance={GiantLobsterRules.SpiritChance:P0} per eligible fish opportunity; bait recipe at Fishing {LobsterBalance.BaitLevel}, dinner at Fishing {LobsterBalance.DinnerLevel}. native ordinary-weight compatibility fallbacks={BetterStardewValley.SpiritFish.SpiritSelection.WeightFallbacks}; Prices={LobsterBalance.FishPrice(LobsterBalance.VanillaLobsterPrice())}/60/700; dinner=160 energy/72 health, Fishing +{LobsterBalance.FishingBuff}, base duration=420 seconds (native modifiers apply); potato=(O){LobsterBalance.Potato}, kale=(O){LobsterBalance.Kale}.",LogLevel.Info));
        monitor.Log("Giant Lobster data and capture integration initialized.",LogLevel.Debug);
    }
    private Texture2D Texture(string name,int size)
    {
        string key=name+"/"+size;if(Textures.TryGetValue(key,out var value))return value;
        var source=Helper.ModContent.Load<Texture2D>("assets/giant-lobster/"+name+".png");
        if(size!=16)throw new ArgumentOutOfRangeException(nameof(size),"Native logical handle must be 16px.");
        // Native 16x16 pixel art (vanilla style) is used as-is; no HD adapter.
        if(source.Width==16&&source.Height==16)
        {
            var data=new Color[256];source.GetData(data);
            var native=new Texture2D(Game1.graphics.GraphicsDevice,16,16);native.SetData(data);
            Textures[key]=native;return native;
        }
        var texture=HdSprites.CreateSourceResolution(source,out var full);
        Textures[key]=texture;Textures[name+"/source"]=full;
        return texture;
    }
}

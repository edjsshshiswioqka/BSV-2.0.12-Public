using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Objects;
namespace BetterStardewValley.KingCrab;

internal sealed class KingCrabService
{
    internal static bool Ready {get;private set;}
    private static IModHelper? Helper;
    private readonly Dictionary<string,Texture2D> Textures=new();
    internal static string Text(string key)=>Helper==null?key:BsvI18n.Get(Helper,key);
    internal KingCrabService(IModHelper helper,IMonitor monitor,string modId)
    {
        Ready=false;Helper=helper;var h=new Harmony(modId+".KingCrab");
        try
        {
            var pot=DrawPatchTargets.Require(typeof(StardewValley.Objects.CrabPot),"DayUpdate",typeof(void));
            if(Harmony.GetPatchInfo(pot)?.Transpilers.Any(p=>p.PatchMethod.DeclaringType==typeof(GiantLobster.LobsterCrabPots))!=true)
                throw new InvalidOperationException("Shared native CrabPot selection patch is not installed; King Crab cannot safely start.");
            KingCrabFoodClock.Install(h);KingCrabTelevision.Install(h);KingCrabHardShell.Install(h,helper);
        }catch{h.UnpatchAll(h.Id);throw;}
        helper.Events.Content.AssetRequested+=(_,e)=>
        {
            if(e.NameWithoutLocale.StartsWith(KingCrabData.TextureRoot))
            {string name=e.NameWithoutLocale.Name[KingCrabData.TextureRoot.Length..];if(name is "fish" or "bowl")e.LoadFrom(()=>Texture(name,16),StardewModdingAPI.Events.AssetLoadPriority.Exclusive);}
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))e.Edit(a=>KingCrabData.Objects(a.AsDictionary<string,ObjectData>().Data,Text,KingCrabData.ApprovedBowl));
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/Fish"))e.Edit(a=>KingCrabData.Fish(a.AsDictionary<string,string>().Data),StardewModdingAPI.Events.AssetEditPriority.Late);
            else if(e.NameWithoutLocale.IsEquivalentTo("Data/CookingRecipes"))e.Edit(a=>KingCrabData.Cooking(a.AsDictionary<string,string>().Data,Text,KingCrabData.ApprovedBowl));
        };
        helper.Events.Content.AssetsInvalidated+=(_,e)=>{if(e.NamesWithoutLocale.Any(n=>n.StartsWith(KingCrabData.TextureRoot)))ClearTextures();};
        helper.Events.GameLoop.TimeChanged+=(_,_)=>{if(Context.IsWorldReady)KingCrabFoodClock.WorldTick(Game1.player);};
        helper.Events.GameLoop.DayEnding+=(_,_)=>{if(Context.IsWorldReady)KingCrabFoodClock.End(Game1.player);KingCrabTelevision.Cancel();};
        helper.Events.Player.Warped+=(_,e)=>{if(e.IsLocalPlayer)KingCrabTelevision.Cancel();};
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>KingCrabTelevision.Reset();
        helper.ConsoleCommands.Add("bsv_king_crab_status",BsvI18n.Get(helper,"cmd.king-crab-status"),(_,_)=>
            monitor.Log("King Crab: ocean base opportunities 1.5%, one shared rare selector; non-crab outcomes 98.5%, price 750g; inedible. Rice Bowl enabled: 850g, base health/energy 150/150. TV premiere year 1 winter 18; reruns every subsequent winter 18. Native food attributes +8 Defense/+4 Immunity; clock adapter implements 600 world minutes (seasoned native duration modifier retained).",LogLevel.Info));
        Ready=true;monitor.Log("King Crab, rice bowl and annual winter 18 TV special registered.",LogLevel.Debug);
    }
    private void ClearTextures()
    {
        foreach(var texture in Textures.Values)HdSprites.Unregister(texture);
        foreach(var texture in Textures.Values.Distinct())texture.Dispose();Textures.Clear();
    }
    private Texture2D Texture(string name,int size)
    {
        string key=name+"/"+size;if(Textures.TryGetValue(key,out var found))return found;
        var source=Helper!.ModContent.Load<Texture2D>("assets/king-crab/"+name+".png");
        if(size!=16)throw new ArgumentOutOfRangeException(nameof(size),"Native logical handle must be 16px.");
        if(source.Width==16&&source.Height==16)
        {
            var data=new Microsoft.Xna.Framework.Color[256];source.GetData(data);
            var native=new Texture2D(Game1.graphics.GraphicsDevice,16,16);native.SetData(data);
            Textures[key]=native;return native;
        }
        var texture=HdSprites.CreateSourceResolution(source,out var full);
        Textures[key]=texture;Textures[name+"/source"]=full;return texture;
    }
}

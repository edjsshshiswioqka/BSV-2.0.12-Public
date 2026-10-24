using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace BetterStardewValley.Farming;

internal sealed class FertilizerService
{
    internal const string Prefix="Eddie.BetterStardewValley/Farming/";
    internal const string SlotKey=Prefix+"Slots.v1", SoilKey=Prefix+"SoilId", CropKey=Prefix+"CropId", GenerationKey=Prefix+"Generation";
    private static FertilizerService? Instance;
    internal readonly IModHelper Helper;
    internal readonly IMonitor Monitor;
    internal readonly FertilizerNetwork Network;
    internal readonly HarvestNetwork Harvests;
    private sealed class Cache { internal string? Raw,Native;internal FertilizerSlots Slots=FertilizerRules.Native(null); }
    private static readonly ConditionalWeakTable<HoeDirt,Cache> Cached=new();
    private static bool Installing;
    private readonly HashSet<string> LoggedErrors=new();
    internal static bool Ready {get;private set;}
    internal FertilizerService(IModHelper helper,IMonitor monitor,string id)
    {
        Helper=helper;Monitor=monitor;Instance=this;Network=new(this);Harvests=new(this);
        var h=new Harmony(id+".Farming");
        try
        {
            Patch(h,typeof(HoeDirt),"CheckApplyFertilizerRules",nameof(CheckRules));
            Patch(h,typeof(HoeDirt),"plant",nameof(Plant),nameof(Planted));
            Patch(h,typeof(HoeDirt),"GetFertilizerQualityBoostLevel",null,nameof(QualityBoost));
            Patch(h,typeof(HoeDirt),"GetFertilizerSpeedBoost",null,nameof(SpeedBoost));
            foreach(string name in new[]{"seasonUpdate","dayUpdate","performToolAction","destroyCrop"})Patch(h,typeof(HoeDirt),name,null,nameof(Clean));
            Patch(h,typeof(HoeDirt),"initNetFields",null,nameof(Watch));
            Patch(h,typeof(HoeDirt),"DrawOptimized",null,nameof(Draw));
            Ready=true;
        }
        catch{h.UnpatchAll(h.Id);Instance=null;throw;}
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>Scan();
        helper.Events.World.LocationListChanged+=(_,_)=>Scan();
        helper.Events.GameLoop.DayStarted+=(_,_)=>{try{Harvests.PruneClaims();}catch(Exception ex){Error(ex);}};
        helper.Events.GameLoop.UpdateTicked+=(_,e)=>{if(e.IsMultipleOf(6)&&Context.IsWorldReady&&!IntegrationHost.Saving){Network.Tick();Harvests.Tick();}};
        helper.Events.Display.RenderedHud+=(_,e)=>Hover(e.SpriteBatch);
    }
    private static void Patch(Harmony h,Type type,string name,string? prefix=null,string? postfix=null)
        =>h.Patch(AccessTools.Method(type,name)??throw new MissingMethodException(type.Name,name),
            prefix:prefix==null?null:new(typeof(FertilizerService),prefix),postfix:postfix==null?null:new(typeof(FertilizerService),postfix));
    internal static FertilizerSlots Read(HoeDirt soil)
    {
        string? raw=soil.modData.GetValueOrDefault(SlotKey),native=soil.fertilizer.Value;
        var c=Cached.GetOrCreateValue(soil);
        if(c.Raw==raw&&c.Native==native)return c.Slots;
        c.Raw=raw;c.Native=native;c.Slots=FertilizerRules.Native(native);
        if(raw!=null&&raw.Length<2048)
        {
            try{var s=JsonSerializer.Deserialize<FertilizerSlots>(raw);if(s!=null&&FertilizerRules.Valid(s,native))c.Slots=s;}
            catch(JsonException){} // Preserve malformed extension; never consume into it.
        }
        return c.Slots;
    }
    internal static bool Healthy(HoeDirt soil)
    {
        if(!soil.modData.TryGetValue(SlotKey,out var raw))return true;
        try{return raw.Length<2048&&JsonSerializer.Deserialize<FertilizerSlots>(raw) is {} s&&FertilizerRules.Valid(s,soil.fertilizer.Value);}
        catch(JsonException){return false;}
    }
    internal static string Identity(HoeDirt soil)
    {
        if(soil.modData.TryGetValue(SoilKey,out var id))return id;
        if(!Context.IsMainPlayer)return "";
        return soil.modData[SoilKey]=Guid.NewGuid().ToString("N");
    }
    internal static void Initialize(HoeDirt soil)
    {
        if(!Context.IsMainPlayer)return;
        Identity(soil);
        if(!soil.modData.ContainsKey(SlotKey)&&FertilizerRules.Known(soil.fertilizer.Value))Write(soil,FertilizerRules.Native(soil.fertilizer.Value));
        EnsureCrop(soil.crop);
    }
    internal static void EnsureCrop(Crop? crop)
    {
        if(crop==null||crop.modData.ContainsKey(CropKey))return;
        crop.modData[CropKey]=Guid.NewGuid().ToString("N");
        crop.modData[GenerationKey]="0";
    }
    private static void Scan()
    {
        if(!Context.IsWorldReady||!Context.IsMainPlayer)return;
        Utility.ForEachLocation(location=>{location.ForEachDirt(soil=>{Initialize(soil);return true;},true);return true;},true,true);
    }
    internal static void Write(HoeDirt soil,FertilizerSlots slots)
    {
        if(!FertilizerRules.Valid(slots,slots.Primary))throw new InvalidDataException("Invalid fertilizer state.");
        Installing=true;
        try{soil.fertilizer.Value=slots.Primary;soil.modData[SlotKey]=JsonSerializer.Serialize(slots);}
        finally{Installing=false;}
    }
    private static void Watch(HoeDirt __instance)
    {
        __instance.fertilizer.fieldChangeVisibleEvent+=(_,oldValue,newValue)=>
        {
            if(Installing||!Context.IsMainPlayer||FertilizerRules.Id(oldValue)==FertilizerRules.Id(newValue))return;
            if(!__instance.modData.TryGetValue(SlotKey,out var raw))return;
            // XML can deserialize modData before fertilizer. A matching primary is the
            // same saved state, not a replacement. Never erase its secondary on load.
            if(FertilizerRules.Id(newValue)==""){__instance.modData.Remove(SlotKey);return;}
            try{if(JsonSerializer.Deserialize<FertilizerSlots>(raw)?.Primary!=FertilizerRules.Id(newValue))__instance.modData.Remove(SlotKey);}
            catch(JsonException){}
        };
    }
    private static void Clean(HoeDirt __instance)
    {
        if(Context.IsMainPlayer&&string.IsNullOrEmpty(FertilizerRules.Id(__instance.fertilizer.Value)))__instance.modData.Remove(SlotKey);
    }
    private static bool CheckRules(HoeDirt __instance,string fertilizerId,ref HoeDirtFertilizerApplyStatus __result)
    {
        if(!Ready||!FertilizerRules.Known(fertilizerId))return true;
        if(!Healthy(__instance)){__result=HoeDirtFertilizerApplyStatus.HasAnotherFertilizer;return false;}
        var s=Read(__instance);
        if(FertilizerRules.CanAdd(s,fertilizerId,__instance.crop?.currentPhase.Value))__result=HoeDirtFertilizerApplyStatus.Okay;
        else if(FertilizerRules.Quality(fertilizerId)>0&&s.Quality==""&&s.Primary!=""&&!FertilizerRules.Known(s.Primary))__result=HoeDirtFertilizerApplyStatus.HasAnotherFertilizer;
        else if(FertilizerRules.Quality(fertilizerId)>0&&s.Quality==""&&__instance.crop?.currentPhase.Value>0)__result=HoeDirtFertilizerApplyStatus.CropAlreadySprouted;
        else __result=HoeDirtFertilizerApplyStatus.HasAnotherFertilizer;
        return false;
    }
    private static bool Plant(HoeDirt __instance,string itemId,Farmer who,bool isFertilizer,ref bool __result)
    {
        if(!Ready||!isFertilizer||!FertilizerRules.Known(itemId))return true;
        // Return false to every native caller: only the approved transaction consumes.
        // Probe uses CanApply/CheckRules and never reaches this mutating entry point.
        __result=false;
        if(who?.IsLocalPlayer==true&&who.ActiveObject is {} active&&active.QualifiedItemId==FertilizerRules.Id(itemId))Instance!.Network.Request(__instance,who,active);
        return false;
    }
    private static void Planted(HoeDirt __instance,bool isFertilizer,bool __result)
    {if(__result&&!isFertilizer){EnsureCrop(__instance.crop);if(Context.IsMainPlayer)Initialize(__instance);}}
    private static void QualityBoost(HoeDirt __instance,ref int __result)
    {if(Ready){var s=Read(__instance);if(s.Dual)__result=Math.Max(__result,FertilizerRules.Quality(s.Quality));}}
    private static void SpeedBoost(HoeDirt __instance,ref float __result)
    {if(Ready){var s=Read(__instance);if(s.Dual)__result=Math.Max(__result,FertilizerRules.Speed(s.Speed) switch{1=>.1f,2=>.25f,3=>.33f,_=>0});}}
    private static void Draw(HoeDirt __instance,SpriteBatch __1)
    {
        try{DrawDual(__instance,__1);}catch(Exception ex){Instance?.Error(ex);}
    }
    /// <summary>
    /// The native soil already shows the first fertilizer's speckles. A second one adds its own native speckles on
    /// the same tile, drawn exactly like the native overlay (cursor sheet, current texture replacements, no Begin/End).
    /// </summary>
    private static void DrawDual(HoeDirt __instance,SpriteBatch fertBatch)
    {
        if(!Ready)return;var s=Read(__instance);
        if(!s.Dual)return;
        string second=s.Primary==s.Quality?s.Speed:s.Quality;
        if(Overlay(second) is not {} rect)return;
        var p=Game1.GlobalToLocal(Game1.viewport,__instance.Tile*64);
        fertBatch.Draw(Game1.mouseCursors,p,rect,Color.White,0,Vector2.Zero,4f,SpriteEffects.None,1.95E-08f);
    }
    /// <summary>Native HoeDirt.GetFertilizerSourceRect cell for each fertilizer (Stardew 1.6.15).</summary>
    internal static Rectangle? Overlay(string id)
    {
        int index=FertilizerRules.Id(id) switch
        {
            "(O)368"=>0,"(O)369"=>1,"(O)919"=>2,"(O)370"=>3,"(O)371"=>4,"(O)920"=>5,"(O)465"=>6,"(O)466"=>7,"(O)918"=>8,_=>-1
        };
        return index<0?null:new Rectangle(173+index/3*16,462+index%3*16,16,16);
    }
    private void Hover(SpriteBatch b)
    {
        if(!Context.IsWorldReady||Game1.activeClickableMenu!=null||Game1.currentLocation==null)return;
        var soil=Game1.currentLocation.GetHoeDirtAtTile(Helper.Input.GetCursorPosition().Tile);
        if(soil==null||!Read(soil).Dual)return;
        var s=Read(soil);var c=Configuration.SharedPreferences.Current;
        string State(bool value)=>BsvI18n.Get(Helper,value?"config.state-on":"config.state-off");
        string text=BsvI18n.Get(Helper,"farming.dual-status",new{quality=ItemRegistry.GetDataOrErrorItem(s.Quality).DisplayName,speed=ItemRegistry.GetDataOrErrorItem(s.Speed).DisplayName,
            fertilizer=State(c.HarvestBonus.EnableFertilizerYieldBonus),regrow=State(c.EnableBsvSpeedGro),extra=State(c.HarvestBonus.Enabled)});
        StardewValley.Menus.IClickableMenu.drawHoverText(b,text,Game1.smallFont);
    }
    internal void Notice(string key)=>Game1.showRedMessage(BsvI18n.Get(Helper,key));
    internal void Error(Exception ex){if(LoggedErrors.Add(ex.GetType().FullName+":"+ex.Message))Monitor.Log("BSV farming transaction paused; state retained: "+ex,LogLevel.Error); }
    internal static void SubmitHarvest(Crop crop,HoeDirt soil,Farmer who,IReadOnlyList<Item> items,string eventId,string seed,bool seeds,double chance,int regrow,StardewValley.Characters.JunimoHarvester? junimo,Vector2 position,bool drop)
        =>Instance?.Harvests.Submit(crop,soil,who,items,eventId,seed,seeds,chance,regrow,junimo,position,drop);
}

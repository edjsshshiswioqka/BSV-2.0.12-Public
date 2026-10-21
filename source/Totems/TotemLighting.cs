using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace BetterStardewValley.Totems;

// Visual state only, derived on each local screen from the replicated object and farm cooldown.
// No shared light net fields, saved light objects, new cooldowns or network messages.
internal sealed class TotemLighting
{
    internal const string Prefix="BSV/Totem/";
    internal const float RadiusPixels=160, CooldownStrength=.25f;
    private readonly PerScreen<HashSet<string>> Owned=new(()=>new());
    private bool Logged;
    internal TotemLighting() { }
    internal TotemLighting(IModHelper helper,IMonitor monitor)
    {
        void Refresh()
        {
            try { Update(); }
            catch(Exception ex){Clear();if(!Logged){Logged=true;monitor.Log("BSV totem lighting unavailable; totem gameplay is unchanged. "+ex,LogLevel.Warn);}}
        }
        helper.Events.GameLoop.OneSecondUpdateTicked+=(_,_)=>Refresh();
        helper.Events.GameLoop.DayStarted+=(_,_)=>Refresh();
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>Refresh();
        helper.Events.GameLoop.TimeChanged+=(_,_)=>Refresh();
        helper.Events.World.ObjectListChanged+=(_,_)=>Refresh();
        helper.Events.Player.Warped+=(_,e)=>{if(e.IsLocalPlayer){Clear();Refresh();}};
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{Clear();Logged=false;};
    }
    // Native subtractive lightmap tints; alpha controls intensity, not hue.
    internal static Color Tint(TotemKind kind,float fade,bool cooling)
    {
        var tint=kind switch
        {
            TotemKind.Harvest=>new Color(0,20,105),
            TotemKind.Growth=>new Color(95,20,105),
            TotemKind.Processing=>new Color(0,50,120),
            _=>new Color(75,35,0)
        };
        // Premultiplied-alpha RGB and alpha must fade together for the game's light draw.
        return new Color(tint.R,tint.G,tint.B,(byte)160)*Math.Clamp(fade*(cooling?CooldownStrength:1),0,1);
    }
    internal void Clear()
    {
        foreach(var id in Owned.Value)Game1.currentLightSources?.Remove(id);
        Owned.Value.Clear();
    }
    internal void Update()
    {
        var desired=new HashSet<string>();var location=Game1.currentLocation;
        if(Context.IsWorldReady && location!=null && location.IsOutdoors && Game1.isStartingToGetDarkOut(location)
            && Game1.lantern!=null && Game1.currentLightSources!=null)
        {
            float fade=Math.Clamp(Utility.CalculateMinutesBetweenTimes(Game1.getStartingToGetDarkTime(location),Game1.timeOfDay)/60f,0,1);
            var state=TotemStorage.Read();
            foreach(var pair in location.objects.Pairs)
            {
                if(!TotemRules.TryKind(pair.Value.QualifiedItemId,out var kind))continue;
                string id=Prefix+location.NameOrUniqueName+"/"+(int)pair.Key.X+"/"+(int)pair.Key.Y+"/"+kind;
                desired.Add(id);
                var center=TotemArt.LightCenter(kind,(int)pair.Key.X,(int)pair.Key.Y);
                var color=Tint(kind,fade,state.Remaining(kind,TotemStorage.Season,Game1.Date.TotalDays)>0);
                float radius=RadiusPixels*2/Game1.lantern.Width;
                if(!Game1.currentLightSources.TryGetValue(id,out var light))
                    Game1.currentLightSources[id]=light=new LightSource(id,LightSource.lantern,center,radius,color,
                        LightSource.LightContext.None,0,location.NameOrUniqueName);
                else{light.position.Value=center;light.radius.Value=radius;light.color.Value=color;light.lightTexture=Game1.lantern;}
            }
        }
        foreach(var old in Owned.Value)if(!desired.Contains(old))Game1.currentLightSources?.Remove(old);
        Owned.Value=desired;
    }
}

using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Machines;
using SObject = StardewValley.Object;

namespace BetterStardewValley.Brewing;

// BSV-owned adapter around native item, crafting, machine, collision and save systems.
internal sealed class BsvKegFeature
{
    internal const string TextureAsset = "Mods/" + WineRules.Prefix + "/Keg";
    internal const string Ingredients = "388 30 334 1 335 1 725 1";
    internal const int MinimumMinutes = 10;
    internal const double AlcoholSpeedup = .25;   // alcohol brews 25% faster in the BSV keg
    private readonly IModHelper Helper;

    internal BsvKegFeature(IModHelper helper)
    {
        Helper = helper;
        var target = DrawPatchTargets.Require(typeof(SObject), nameof(SObject.OutputMachine), typeof(bool),
            typeof(MachineData), typeof(MachineOutputRule), typeof(Item), typeof(Farmer), typeof(GameLocation), typeof(bool), typeof(bool));
        new Harmony(WineRules.Prefix + ".Brewing").Patch(target,
            postfix: new HarmonyMethod(typeof(BsvKegFeature), nameof(AfterOutput)) { priority = Priority.Last });
        helper.Events.Content.AssetRequested += Assets;
        helper.Events.Content.LocaleChanged += (_, _) =>
        {
            helper.GameContent.InvalidateCache("Data/BigCraftables");
            helper.GameContent.InvalidateCache("Data/CraftingRecipes");
        };
        helper.Events.GameLoop.SaveLoaded += (_, _) => Unlock();
        helper.Events.GameLoop.DayStarted += (_, _) => Unlock();
        BsvKegInstanceRepair.Install(helper);
        // Native LevelUpMenu reads the Farming 8 recipe before this fallback is needed.
        // Do not learn it on LevelChanged, which would hide the level-up recipe card.
    }

    internal static bool Alcohol(string? output) => output is "(O)348" or "(O)346" or "(O)303" or "(O)459"
        || output == WineRules.GrapeWine || output == WineRules.BloodOfJesus;
    internal static int Duration(int nativeMinutes) => (int)Math.Max(MinimumMinutes, Math.Round(nativeMinutes * (1 - AlcoholSpeedup)));
    internal static void AfterOutput(SObject __instance, bool probe, bool heldObjectOnly, bool __result)
    {
        if (!__result || probe || heldObjectOnly || __instance.QualifiedItemId != WineRules.BsvKeg
            || !Alcohol(__instance.heldObject.Value?.QualifiedItemId)) return;
        // The original method has already evaluated native/custom ready-time modifiers.
        // Runs once for a new batch, never from minutesElapsed, collection, save or load.
        __instance.MinutesUntilReady = Duration(__instance.MinutesUntilReady);
        __instance.readyForHarvest.Value = false;
    }
    internal static string Recipe(string name) => Ingredients + "/Home/" + WineRules.BsvKegId + "/true/Farming 8/" + name;
    internal static void Unlock()
    {
        if (Context.IsWorldReady && Game1.player?.farmingLevel.Value >= 8
            && !Game1.player.craftingRecipes.ContainsKey(WineRules.BsvKegId))
            Game1.player.craftingRecipes.Add(WineRules.BsvKegId, 0);
    }
    private void Assets(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(TextureAsset))
            e.LoadFrom(CreateTexture, AssetLoadPriority.Exclusive);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))
            e.Edit(a => Register(a.AsDictionary<string, BigCraftableData>().Data, k => BsvI18n.Get(Helper, k)), BetterStardewValley.BsvAssets.EditPriority);
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
            e.Edit(a => a.AsDictionary<string, string>().Data[WineRules.BsvKegId] = Recipe(BsvI18n.Get(Helper, "bsv-keg.name")), BetterStardewValley.BsvAssets.EditPriority);
    }
    internal static void Register(IDictionary<string, BigCraftableData> data, Func<string,string> translate)
    {
        var own = WineData.Copy(data["12"]);
        own.Name = "BSV Keg"; own.DisplayName = translate("bsv-keg.name");
        own.Description = translate("bsv-keg.description"); own.Texture = TextureAsset; own.SpriteIndex = 0;
        data[WineRules.BsvKegId] = own;
    }
    // BSV keg look: warm tan-brown wood and polished copper hoops/tap, recoloured from the installed game's keg at
    // runtime (no Stardew texture is distributed). Wood = saturated orange pixels; metal = the greyish ones;
    // the blue ground shadow is kept. Light comes from the left, so the left side of the metal gets a glint.
    private static readonly Color[] Wood={new(40,20,10),new(80,44,22),new(128,78,40),new(172,118,64),new(208,158,96),new(234,196,136)};
    private static readonly float[] WoodStops={0,48,96,144,208,255};
    private static readonly Color[] Copper={new(34,14,8),new(104,44,20),new(180,94,48),new(250,176,120)};
    private static readonly float[] MetalStops={0,40,80,140};
    private static Color Ramp(Color[] ramp,float[] stops,float light)
    {
        for(int i=0;i<ramp.Length-1;i++)
            if(light<stops[i+1]||i==ramp.Length-2)
                return Color.Lerp(ramp[i],ramp[i+1],Math.Clamp((light-stops[i])/(stops[i+1]-stops[i]),0,1));
        return ramp[^1];
    }
    internal static Color Recolor(Color c,int x,int width)
    {
        if(c.A==0)return c;
        float alpha=c.A/255f;
        float r=c.R/alpha,g=c.G/alpha,b=c.B/alpha;   // the texture is premultiplied
        if(b>r*1.6f)return c;                            // ground shadow
        float max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b));
        float light=Math.Clamp(.2126f*r+.7152f*g+.0722f*b,0,255);
        Color result;
        if(max>0&&(max-min)/max>.4f)result=Ramp(Wood,WoodStops,light);
        else result=Ramp(Copper,MetalStops,light+(x<=width*5/16?30:x>=width*11/16?-10:0));
        return new Color((byte)(result.R*alpha),(byte)(result.G*alpha),(byte)(result.B*alpha),c.A);
    }
    internal static Color[] RecolorFrames(Color[] pixels,int width)
    {
        var frames=new Color[pixels.Length*2];
        for(int i=0;i<pixels.Length;i++)frames[i]=frames[i+pixels.Length]=Recolor(pixels[i],i%width,width);
        return frames;
    }
    private static Texture2D CreateTexture()
    {
        var native = ItemRegistry.GetDataOrErrorItem(WineRules.Keg);
        if (native.IsErrorItem) throw new InvalidOperationException("Vanilla Keg texture unavailable.");
        var source = native.GetTexture(); var rect = native.GetSourceRect();
        // No Stardew texture is distributed. Read the installed game's sprite at runtime.
        var pixels = new Color[rect.Width * rect.Height]; source.GetData(0, rect, pixels, 0, pixels.Length);
        var frames = RecolorFrames(pixels, rect.Width);
        var texture = new Texture2D(Game1.graphics.GraphicsDevice, rect.Width, rect.Height * 2);
        texture.SetData(frames); return texture;
    }
}

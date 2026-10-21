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
    internal const int MinimumMinutes = 10, AlcoholReduction = 1440;
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
    internal static int Duration(int nativeMinutes) => (int)Math.Max(MinimumMinutes, (long)nativeMinutes - AlcoholReduction);
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
            e.Edit(a => Register(a.AsDictionary<string, BigCraftableData>().Data, k => BsvI18n.Get(Helper, k)));
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
            e.Edit(a => a.AsDictionary<string, string>().Data[WineRules.BsvKegId] = Recipe(BsvI18n.Get(Helper, "bsv-keg.name")));
    }
    internal static void Register(IDictionary<string, BigCraftableData> data, Func<string,string> translate)
    {
        var own = WineData.Copy(data["12"]);
        own.Name = "BSV Keg"; own.DisplayName = translate("bsv-keg.name");
        own.Description = translate("bsv-keg.description"); own.Texture = TextureAsset; own.SpriteIndex = 0;
        data[WineRules.BsvKegId] = own;
    }
    internal static Color DarkWood(Color c)
    {
        // Recolor only warm wood. The previous ramp compressed the native middle/high
        // tones into shadows (luma 144 -> RGB 112/68/37), hiding grain and the tap.
        // Keep deep outlines and neutral/cool metal unchanged; restore wood contrast
        // in the texture once, not by tinting each menu/held/world rendering path.
        if(c.A==0 || c.R<=c.B*1.15f || c.R<=c.G*1.02f)return c;
        float alpha=c.A/255f;
        float light=Math.Clamp((.2126f*c.R+.7152f*c.G+.0722f*c.B)/alpha,0,255);
        Color wood;
        if(light<48)wood=Color.Lerp(new Color(24,13,9),new Color(56,32,21),light/48);
        else if(light<96)wood=Color.Lerp(new Color(56,32,21),new Color(100,55,28),(light-48)/48);
        else if(light<144)wood=Color.Lerp(new Color(100,55,28),new Color(154,89,43),(light-96)/48);
        else if(light<208)wood=Color.Lerp(new Color(154,89,43),new Color(200,139,77),(light-144)/64);
        else wood=Color.Lerp(new Color(200,139,77),new Color(225,177,116),(light-208)/47);
        return new Color((byte)(wood.R*alpha),(byte)(wood.G*alpha),(byte)(wood.B*alpha),c.A);
    }
    internal static Color[] RecolorFrames(Color[] pixels)
    {
        var frames=new Color[pixels.Length*2];
        for(int i=0;i<pixels.Length;i++)frames[i]=frames[i+pixels.Length]=DarkWood(pixels[i]);
        return frames;
    }
    private static Texture2D CreateTexture()
    {
        var native = ItemRegistry.GetDataOrErrorItem(WineRules.Keg);
        if (native.IsErrorItem) throw new InvalidOperationException("Vanilla Keg texture unavailable.");
        var source = native.GetTexture(); var rect = native.GetSourceRect();
        // No Stardew texture is distributed. Read the installed game's sprite at runtime.
        var pixels = new Color[rect.Width * rect.Height]; source.GetData(0, rect, pixels, 0, pixels.Length);
        var frames = RecolorFrames(pixels);
        var texture = new Texture2D(Game1.graphics.GraphicsDevice, rect.Width, rect.Height * 2);
        texture.SetData(frames); return texture;
    }
}

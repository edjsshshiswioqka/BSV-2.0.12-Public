using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using SObject = StardewValley.Object;

namespace BetterStardewValley.Totems;

// A — exact author artwork, technical crops only. Normal SObject persistence/placement/pickup.
internal sealed class TotemArt
{
    private static TotemArt Instance = null!;
    private readonly IModHelper Helper;
    private readonly Dictionary<(TotemKind, bool), Texture2D> Textures = new();
    private readonly Dictionary<TotemKind, Rectangle> WorldBounds = new();
    internal const int TargetWorldWidth = 128, HeldHeight = 128;
    [ThreadStatic] private static int CursorDepth;
    [ThreadStatic] private static bool CursorHasPreview;
    private readonly record struct CursorState(int Depth, bool Preview);
    private const string AssetRoot = "Mods/Eddie.BetterStardewValley/Totems/";
    internal TotemArt(IModHelper helper, Harmony h)
    {
        Instance = this; Helper = helper;
        h.Patch(AccessTools.Method(typeof(SObject), nameof(SObject.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) }), prefix: new HarmonyMethod(typeof(TotemArt), nameof(World)));
        h.Patch(DrawPatchTargets.ObjectMenu(), prefix: new HarmonyMethod(typeof(TotemArt), nameof(Menu)));
        h.Patch(DrawPatchTargets.Require(typeof(Game1), nameof(Game1.drawMouseCursor), typeof(void)),
            prefix: new HarmonyMethod(typeof(TotemArt), nameof(BeginCursor)), finalizer: new HarmonyMethod(typeof(TotemArt), nameof(EndCursor)));
        h.Patch(AccessTools.Method(typeof(SObject), nameof(SObject.drawWhenHeld)), prefix: new HarmonyMethod(typeof(TotemArt), nameof(Held)));
        h.Patch(AccessTools.Method(typeof(SObject), nameof(SObject.placementAction)), prefix: new HarmonyMethod(typeof(TotemArt), nameof(Place)));
        h.Patch(AccessTools.Method(typeof(Utility), nameof(Utility.playerCanPlaceItemHere)), postfix: new HarmonyMethod(typeof(TotemArt), nameof(CanPlace)));
        helper.Events.Content.AssetRequested += Assets;
        helper.Events.Content.AssetsInvalidated += (_, _) => { Textures.Clear(); WorldBounds.Clear(); };
    }
    private void Assets(object? sender, AssetRequestedEventArgs e)
    {
        foreach (var kind in Enum.GetValues<TotemKind>())
            if (e.NameWithoutLocale.IsEquivalentTo(AssetRoot + kind))
            { e.LoadFromModFile<Texture2D>($"assets/totems/{kind.ToString().ToLowerInvariant()}-native.png", AssetLoadPriority.Exclusive); return; }
        if (e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables")) e.Edit(a =>
        {
            var data = a.AsDictionary<string, BigCraftableData>().Data;
            foreach (var kind in Enum.GetValues<TotemKind>())
                data[TotemRules.Id(kind)] = new BigCraftableData
                {
                    Name = "BSV " + kind + " Totem", DisplayName = BsvI18n.Get(Helper, "totem." + kind.ToString().ToLowerInvariant() + ".name"),
                    Description = BsvI18n.Get(Helper, "totem." + kind.ToString().ToLowerInvariant() + ".description"),
                    Texture = AssetRoot + kind, SpriteIndex = 0, CanBePlacedIndoors = false, CanBePlacedOutdoors = true,
                    // Zero is a non-sale placeholder, not an author-designed purchase price. No shop entry; recipes are supplied by TotemUnlockService.
                    Price = 0, Fragility = 0
                };
        });
    }
    private Texture2D Texture(TotemKind kind, bool icon)
    {
        var key = (kind, icon);
        if (!Textures.TryGetValue(key, out var t)) Textures[key] = t = Helper.ModContent.Load<Texture2D>($"assets/totems/{kind.ToString().ToLowerInvariant()}-{(icon ? "icon" : "world")}.png");
        return t;
    }
    private static Color Tint()
    { return new Color(140, 140, 140); }
    internal static Color ColorFor(TotemKind kind)
    {
        if (!Context.IsWorldReady) return Color.White;
        try { return TotemStorage.Read().Remaining(kind, TotemStorage.Season, Game1.Date.TotalDays) == 0 ? Color.White : Tint(); }
        catch { return Tint(); }
    }
    internal static Rectangle OpaqueBounds(Color[] pixels, int width, int height)
    {
        int left=width,top=height,right=-1,bottom=-1;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            if(pixels[y*width+x].A!=0){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
        if(right<left)throw new InvalidOperationException("Empty totem world artwork.");
        return new Rectangle(left,top,right-left+1,bottom-top+1);
    }
    private Rectangle Bounds(TotemKind kind)
    {
        if(!WorldBounds.TryGetValue(kind,out var rect))
        {
            var texture=Texture(kind,false);var pixels=new Color[texture.Width*texture.Height];texture.GetData(pixels);
            WorldBounds[kind]=rect=OpaqueBounds(pixels,texture.Width,texture.Height);
        }
        return rect;
    }
    // Preserve the single-frame author's silhouette. Prefer whole source-pixel blocks
    // near two tiles wide: the current 60px bodies render at 2x, i.e. 120px wide.
    // Height follows the cropped body; never stretch X and Y independently.
    internal static Vector2 WorldScale(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        float scale = TargetWorldWidth / (float)bounds.Width;
        return new Vector2(scale >= 1 ? MathF.Floor(scale) : scale);
    }
    internal static Vector2 GroundAnchor(int x,int y) => new(x*64+32,(y+1)*64);
    internal static Vector2 LightCenter(TotemKind kind,int x,int y)
    {
        var bounds = Instance.Bounds(kind);
        return GroundAnchor(x,y) - new Vector2(0,bounds.Height * WorldScale(bounds).Y / 2f);
    }
    private static void BeginCursor(out CursorState __state)
    { __state = new(CursorDepth, CursorHasPreview); CursorDepth++; CursorHasPreview = false; }
    private static void EndCursor(CursorState __state)
    { CursorDepth = __state.Depth; CursorHasPreview = __state.Preview; }
    private static void DrawWorld(SpriteBatch batch,TotemKind kind,Vector2 ground,float alpha,float layer,bool held=false)
    {
        var bounds=Instance.Bounds(kind);
        var scale=held?new Vector2(HeldHeight/(float)bounds.Height):WorldScale(bounds);
        batch.Draw(Instance.Texture(kind,false),ground,bounds,ColorFor(kind)*alpha,0,
            new Vector2(bounds.Width/2f,bounds.Height),scale,SpriteEffects.None,layer);
    }
    private static bool Place(SObject __instance, GameLocation location, ref bool __result)
    {
        if (!TotemRules.TryKind(__instance.QualifiedItemId, out _) || ReferenceEquals(location, Game1.getFarm())) return true;
        __result = false; return false;
    }
    private static void CanPlace(GameLocation location, Item item, ref bool __result)
    { if (TotemRules.TryKind(item.QualifiedItemId, out _) && !ReferenceEquals(location, Game1.getFarm())) __result = false; }
    private static bool World(SObject __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
    {
        if (!TotemRules.TryKind(__instance.QualifiedItemId, out var kind)) return true;
        var at = Game1.GlobalToLocal(Game1.viewport, GroundAnchor(x,y));
        DrawWorld(spriteBatch,kind,at,alpha,Math.Max(0,((y+1)*64-1)/10000f));
        // Native placement preview calls Object.draw, then draws the carried icon.
        // Suppress only that redundant icon inside this cursor render pass.
        if (CursorDepth > 0 && ReferenceEquals(Game1.player.ActiveObject, __instance)) CursorHasPreview = true;
        return false;
    }
    private static bool Held(SObject __instance, SpriteBatch spriteBatch, Vector2 objectPosition, Farmer f)
    {
        if (!TotemRules.TryKind(__instance.QualifiedItemId, out var kind)) return true;
        // Keep the native held 64x128 anchor, fitting the actual artwork rather than its canvas.
        DrawWorld(spriteBatch,kind,objectPosition+new Vector2(32,128),1,Math.Max(0,(f.StandingPixel.Y+3)/10000f),held:true);
        return false;
    }
    private static bool Menu(SObject __instance, SpriteBatch spriteBatch, Vector2 location, float scaleSize, float transparency, float layerDepth, StackDrawType drawStackNumber, Color color)
    {
        if (!TotemRules.TryKind(__instance.QualifiedItemId, out var kind)) return true;
        if (CursorDepth > 0 && CursorHasPreview && Game1.activeClickableMenu == null
            && ReferenceEquals(Game1.player.ActiveObject, __instance)) return false;
        spriteBatch.Draw(Instance.Texture(kind, true), location + new Vector2(32), null, color * transparency, 0, new Vector2(32), scaleSize, SpriteEffects.None, layerDepth);
        __instance.DrawMenuIcons(spriteBatch, location, scaleSize, transparency, layerDepth, drawStackNumber, color);
        return false;
    }
}

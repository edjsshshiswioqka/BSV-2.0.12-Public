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
    internal const int TargetWorldWidth = 180, HeldHeight = 128;
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
    // near three tiles wide: the current 60px bodies render at 3x, i.e. 180px wide.
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
        if(!held)
        {
            // Soft ground shadow under the stone base, the game's own shadow sprite.
            var sb=Game1.shadowTexture.Bounds;
            batch.Draw(Game1.shadowTexture,ground-new Vector2(0,6),sb,Color.White*(0.45f*alpha),0,new Vector2(sb.Width/2f,sb.Height/2f),
                new Vector2(bounds.Width*scale.X*0.85f/sb.Width,24f/sb.Height),SpriteEffects.None,Math.Max(0,layer-0.0001f));
        }
        batch.Draw(Instance.Texture(kind,false),ground,bounds,ColorFor(kind)*alpha,0,
            new Vector2(bounds.Width/2f,bounds.Height),scale,SpriteEffects.None,layer);
    }
    /// <summary>World pixels covered by the artwork of a totem standing on (x,y).</summary>
    internal static Rectangle ArtRect(TotemKind kind,int x,int y)
    {
        var bounds=Instance.Bounds(kind);var scale=WorldScale(bounds);
        int w=(int)(bounds.Width*scale.X),h=(int)(bounds.Height*scale.Y);var g=GroundAnchor(x,y);
        return new Rectangle((int)g.X-w/2,(int)g.Y-h,w,h);
    }
    /// <summary>Tiles under the artwork (each covered by at least a quarter tile), the totem's own tile included.</summary>
    internal static IEnumerable<Vector2> CoveredTiles(TotemKind kind,int x,int y)
    {
        var r=ArtRect(kind,x,y);
        for(int ty=(int)Math.Floor(r.Top/64f);ty<=y;ty++)
            for(int tx=(int)Math.Floor(r.Left/64f);tx*64<r.Right;tx++)
            {
                var cell=new Rectangle(tx*64,ty*64,64,64);var hit=Rectangle.Intersect(cell,r);
                if(hit.Width>=16&&hit.Height>=16)yield return new Vector2(tx,ty);
            }
    }
    private static bool Behind(TotemKind kind,int x,int y)
    {
        // Like native trees: fade while the local player stands behind the artwork.
        var who=Game1.player;if(who?.currentLocation==null)return false;
        var art=ArtRect(kind,x,y);var box=who.GetBoundingBox();
        return box.Intersects(new Rectangle(art.X,art.Y,art.Width,art.Height-16)) && who.StandingPixel.Y<(y+1)*64-8;
    }
    private static bool Place(SObject __instance, GameLocation location, ref bool __result)
    {
        if (!TotemRules.TryKind(__instance.QualifiedItemId, out _) || ReferenceEquals(location, Game1.getFarm())) return true;
        __result = false; return false;
    }
    private static void CanPlace(GameLocation location, Item item, int x, int y, ref bool __result)
    {
        if (!__result || item == null) return;
        if (TotemRules.TryKind(item.QualifiedItemId, out var kind))
        {
            if (!ReferenceEquals(location, Game1.getFarm())) { __result = false; return; }
            __result = TotemRoomFree(location, kind, x / 64, y / 64);
            return;
        }
        if (!ReferenceEquals(location, Game1.getFarm()) || item is not SObject o || Overlookable(o)) return;
        if (UnderTotem(location, new Vector2(x / 64, y / 64))) __result = false;
    }
    // Seeds, fertilizer and floor paths may still go under a totem's artwork.
    private static bool Overlookable(SObject o) => o.Category is SObject.SeedsCategory or SObject.fertilizerCategory
        || StardewValley.TerrainFeatures.Flooring.GetFloorPathItemLookup().ContainsKey(o.ItemId);
    internal static bool UnderTotem(GameLocation location, Vector2 tile)
    {
        foreach (var pair in location.objects.Pairs)
            if (TotemRules.TryKind(pair.Value.QualifiedItemId, out var kind) && pair.Key != tile
                && CoveredTiles(kind, (int)pair.Key.X, (int)pair.Key.Y).Contains(tile)) return true;
        return false;
    }
    private static bool TotemRoomFree(GameLocation location, TotemKind kind, int x, int y)
    {
        var mine = CoveredTiles(kind, x, y).ToHashSet();
        foreach (var tile in mine)
        {
            if (tile == new Vector2(x, y)) continue;
            if (location.objects.ContainsKey(tile) || location.getBuildingAt(tile) != null) return false;
            if (location.terrainFeatures.TryGetValue(tile, out var f) && f is StardewValley.TerrainFeatures.Tree or StardewValley.TerrainFeatures.FruitTree) return false;
            if (location.furniture.Any(fu => fu.GetBoundingBox().Intersects(new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64)))) return false;
        }
        // Two totems never overlap each other's artwork.
        foreach (var pair in location.objects.Pairs)
            if (TotemRules.TryKind(pair.Value.QualifiedItemId, out var other) && pair.Key != new Vector2(x, y)
                && CoveredTiles(other, (int)pair.Key.X, (int)pair.Key.Y).Any(mine.Contains)) return false;
        return true;
    }
    private static bool World(SObject __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
    {
        if (!TotemRules.TryKind(__instance.QualifiedItemId, out var kind)) return true;
        var at = Game1.GlobalToLocal(Game1.viewport, GroundAnchor(x,y));
        bool placed = Game1.currentLocation?.objects.TryGetValue(new Vector2(x, y), out var here) == true && ReferenceEquals(here, __instance);
        if (placed && Behind(kind, x, y)) alpha *= 0.4f;
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

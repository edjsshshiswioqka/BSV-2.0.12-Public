using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using Tile = BetterStardewValley.HorseRules.Tile;

namespace BetterStardewValley;

internal sealed partial class HorseFeature
{
    private sealed class Summon
    {
        internal readonly HorseRequest Request;
        internal long Actor => Request.Actor;
        internal readonly Horse Horse;
        internal readonly GameLocation Location;
        internal readonly Stable Stable;
        internal readonly bool WasFrozen;
        internal double Start, LastTick, NextPlan, BlockedSince, DepartStart;
        internal bool Started, Departing;
        internal Vector2 TargetPlayer;
        internal Queue<Vector2> Route = new();
        internal Summon(HorseRequest request, Horse horse, GameLocation location, Stable stable, double now)
        {
            if (!IsRealManagedHorse(horse) || IsTractorStable(stable))
                throw new InvalidOperationException("BSV cannot control a tractor.");
            Request = request; Horse = horse; Location = location; Stable = stable;
            Start = LastTick = now; WasFrozen = Frozen(horse);
            horse.Halt(); Frozen(horse) = true;
            horse.modData[RunKey] = request.Id;
        }
    }

    private static Vector2 Pixel(Tile t) => new(t.X * 64, t.Y * 64);
    private static Tile Grid(Vector2 p) => new((int)Math.Floor(p.X / 64), (int)Math.Floor(p.Y / 64));
    private static HorseRules.Bounds VisualBounds(Vector2 p) => new((int)p.X - 48, (int)p.Y - 112, 192, 208);
    private static Rectangle Body(Horse horse, Vector2 p)
    {
        var box = horse.GetBoundingBox();
        box.Offset((int)(p.X - horse.Position.X), (int)(p.Y - horse.Position.Y));
        return box;
    }
    private static bool Mountable(GameLocation location, Horse horse, Farmer who, Vector2 p)
    {
        var box = Body(horse, p);
        // Native GameLocation.checkAction uses StandingPixel and radius 1.
        if (!Safe(location, horse, p) || !Utility.withinRadiusOfPlayer(box.Center.X, box.Center.Y, 1, who)) return false;
        var a = who.GetBoundingBox().Center; var b = box.Center;
        for (float t = 0; t <= 1; t += .125f)
        {
            var tile = new Vector2((int)((a.X + (b.X-a.X)*t)/64), (int)((a.Y + (b.Y-a.Y)*t)/64));
            if (!location.isTileOnMap(tile) || !location.isTilePassable(tile)
                || location.isWaterTile((int)tile.X, (int)tile.Y)
                || location.objects.TryGetValue(tile, out var obj) && !obj.isPassable()) return false;
        }
        return true;
    }

    private static bool Safe(GameLocation location, Horse horse, Vector2 p)
    {
        var box = Body(horse, p);
        var back = location.Map.GetLayer("Back");
        if (back == null || box.Left < 0 || box.Top < 0 || box.Right >= back.LayerWidth * 64 || box.Bottom >= back.LayerHeight * 64) return false;
        for (int x = box.Left / 64; x <= box.Right / 64; x++)
        for (int y = box.Top / 64; y <= box.Bottom / 64; y++)
        {
            // Native collision treats some missing Back tiles as clear; summons must not.
            if (back.Tiles[x, y] == null || location.doesTileHaveProperty(x, y, "Water", "Back") != null) return false;
        }
        // Read-only native collision covers Buildings, objects, resource clumps, trees and terrain.
        // pathfinding+skipCollisionEffects avoids pushing animals, opening doors or damaging crops.
        if (location.isCollidingPosition(box, Game1.viewport, false, 0, false, horse,
            pathfinding: true, projectile: false, ignoreCharacterRequirement: false, skipCollisionEffects: true)) return false;
        // Native pathfinding skips moving characters. Explicitly include them for a riderless horse.
        foreach (var npc in location.characters)
            if (!ReferenceEquals(npc, horse) && !npc.IsInvisible && box.Intersects(npc.GetBoundingBox())) return false;
        foreach (var farmer in location.farmers)
            if (box.Intersects(farmer.GetBoundingBox())) return false;
        return true;
    }
    private static bool SegmentSafe(GameLocation location, Horse horse, Vector2 from, Vector2 to)
    {
        // Axis-aligned motion, at most four pixels per collision probe. The real mover uses the same order.
        var p = from;
        while (Math.Abs(p.X - to.X) > .01f)
        { p.X += Math.Clamp(to.X - p.X, -4, 4); if (!Safe(location, horse, p)) return false; }
        while (Math.Abs(p.Y - to.Y) > .01f)
        { p.Y += Math.Clamp(to.Y - p.Y, -4, 4); if (!Safe(location, horse, p)) return false; }
        return true;
    }

    private bool HiddenFromEveryone(GameLocation? location, HorseRules.Bounds visual)
    {
        if (location is null) return false;
        foreach (var farmer in Game1.getOnlineFarmers().Where(f => f.currentLocation == location))
        {
            if (!Views.TryGetValue(farmer.UniqueMultiplayerID, out var view) || Now - view.Time > 1
                || view.View.Location != location.NameOrUniqueName) return false;
            // Small extra buffer protects camera movement during network/viewport update latency.
            if (visual.Intersects(Bounds(view.View).Inflate(96))) return false;
        }
        return true;
    }

    private List<Tile>? FindRoute(Summon state, Farmer who, bool fromCurrent)
    {
        var location = state.Location; var horse = state.Horse;
        var layer = location.Map.GetLayer("Back");
        if (layer == null) return null;
        Tile player = Grid(who.Position), current = Grid(horse.Position);
        var cache = new Dictionary<Tile, bool>();
        bool Clear(Tile t)
        {
            if (!cache.TryGetValue(t, out bool clear)) cache[t] = clear = Safe(location, horse, Pixel(t));
            return clear;
        }
        bool Edge(Tile a, Tile b)
        {
            // Tile endpoints alone leave a 32px gap between vertical horse footprints.
            return Safe(location, horse, (Pixel(a) + Pixel(b)) / 2);
        }
        bool Entry(Tile t) => Math.Abs(t.X - current.X) <= 1 && Math.Abs(t.Y - current.Y) <= 1
            && SegmentSafe(location, horse, horse.Position, Pixel(t));
        var stops = new List<Tile>();
        if (state.Departing)
        {
            // Bounded local search, never a map-wide per-frame scan.
            for (int x = Math.Max(0,current.X-24); x < Math.Min(layer.LayerWidth,current.X+25); x++)
            for (int y = Math.Max(0,current.Y-24); y < Math.Min(layer.LayerHeight,current.Y+25); y++)
            {
                var t = new Tile(x,y);
                if (HiddenFromEveryone(location, VisualBounds(Pixel(t)))) stops.Add(t);
            }
        }
        else
        {
            for (int x = player.X-3; x <= player.X+2; x++)
            for (int y = player.Y-2; y <= player.Y+2; y++)
            {
                var t = new Tile(x,y);
                if (Mountable(location,horse,who,Pixel(t))) stops.Add(t);
            }
        }
        return HorseRules.Route(stops, Entry, Clear, Edge, layer.LayerWidth, layer.LayerHeight, horse.HorseId.GetHashCode());
    }

    private bool StartRoute(Summon s, Farmer who)
    {
        if (!IsRealManagedHorse(s.Horse) || IsTractorStable(s.Stable)) return false;
        var horse = s.Horse;
        List<Tile>? route = null;
        // Coming: a horse somebody can see runs from where it is. Otherwise (another map, or off-screen) it
        // appears on the nearest walkable tile just outside every viewer's view and runs in from there.
        bool visible = horse.currentLocation == s.Location && !HiddenFromEveryone(s.Location, VisualBounds(horse.Position));
        if (s.Departing || visible) route = FindRoute(s, who, true);
        if (route == null && !s.Departing && !visible)
        {
            route = SpawnRoute(s, who);
            if (route == null) return false;
            var entry = route[0];
            if (horse.currentLocation != s.Location) Game1.warpCharacter(horse, s.Location, new Vector2(entry.X, entry.Y));
            horse.Position = Pixel(entry);
            horse.faceDirection(2);
        }
        if (route == null) return false;
        s.Route = new Queue<Vector2>(route.Select(Pixel));
        s.TargetPlayer = who.Position; s.Started = true; s.NextPlan = Now + .6;
        return true;
    }

    /// <summary>Path from an off-screen tile (nearest to the player, hidden from every player in the location)
    /// to a mounting spot next to the player. On a map smaller than the screen, the farthest reachable tile.</summary>
    private List<Tile>? SpawnRoute(Summon state, Farmer who)
    {
        var location = state.Location; var horse = state.Horse;
        var layer = location.Map.GetLayer("Back");
        if (layer == null) return null;
        Tile player = Grid(who.Position);
        var cache = new Dictionary<Tile, bool>();
        bool Clear(Tile t)
        {
            if (!cache.TryGetValue(t, out bool clear)) cache[t] = clear = Safe(location, horse, Pixel(t));
            return clear;
        }
        bool Edge(Tile a, Tile b) => Safe(location, horse, (Pixel(a) + Pixel(b)) / 2);
        var stops = new List<Tile>();
        for (int x = player.X - 3; x <= player.X + 2; x++)
        for (int y = player.Y - 2; y <= player.Y + 2; y++)
        {
            var t = new Tile(x, y);
            if (Mountable(location, horse, who, Pixel(t))) stops.Add(t);
        }
        if (stops.Count == 0) return null;
        return HorseRules.Route(stops, t => HiddenFromEveryone(location, VisualBounds(Pixel(t))), Clear, Edge, layer.LayerWidth, layer.LayerHeight, horse.HorseId.GetHashCode())
            ?? HorseRules.RouteFromFarthest(stops, Clear, Edge, layer.LayerWidth, layer.LayerHeight);
    }

    /// <summary>The horse has left everyone's view: it goes home to its stable (native grabHorse), out of the world
    /// the player is in, until it is called again.</summary>
    private void ReturnHome(Summon s)
    {
        var stable = s.Stable;
        Finish(s, "");
        if (IsTractorStable(stable) || !IsRealManagedHorse(s.Horse) || s.Horse.rider != null) return;
        try { stable.grabHorse(); }
        catch (Exception ex) { WarnOnce("return-home", ex); }
    }

    private void Advance(Summon s, double now)
    {
        if (!Context.IsMainPlayer) return;
        if (!IsRealManagedHorse(s.Horse) || IsTractorStable(s.Stable)) { Finish(s, "horse.call.no_horse"); return; }
        var horse = s.Horse; var who = Game1.GetPlayer(s.Actor, onlyOnline: true);
        if (s.Stable.GetParentLocation()?.buildings.Contains(s.Stable) != true || !HorseRules.Owns(s.Actor, s.Stable.owner.Value, horse.ownerId.Value))
        { Finish(s, ""); return; }
        // The caller left while the horse was leaving: it simply goes home.
        if (who?.currentLocation != s.Location || !Allowed(s.Location))
        { if (s.Departing && horse.currentLocation == s.Location) ReturnHome(s); else Finish(s, ""); return; }
        // Mounted (by the caller, or by anyone the host allows): the run just ends, no message.
        if (horse.rider != null || who.mount != null || horse.mounting.Value || horse.dismounting.Value || horse.mutex.IsLocked())
        { Finish(s, ""); return; }
        double elapsed = now - s.LastTick; s.LastTick = now;
        if (!Game1.IsMultiplayer && Game1.activeClickableMenu != null)
        { s.Start += elapsed; return; }
        if (!s.Departing && now - s.Start > 40) { Finish(s, "horse.call.no_path"); return; }
        if (s.Departing && (horse.currentLocation != s.Location || HiddenFromEveryone(s.Location, VisualBounds(horse.Position))))
        { ReturnHome(s); return; } // already out of everyone's view
        // A horse that could not find a way out within 25 s is sent home anyway (never left hiding in a corner).
        if (s.Departing && now - s.DepartStart > 25) { ReturnHome(s); return; }
        if (!s.Started)
        {
            if (now - s.Start < .45) return;
            if (!StartRoute(s, who))
            {
                if (s.Departing) { s.Start = now + 1.5; return; }
                Finish(s, "horse.call.no_path"); return;
            }
        }
        if (horse.currentLocation != s.Location) { Finish(s, ""); return; }
        bool movedTarget = !s.Departing && Vector2.DistanceSquared(who.Position, s.TargetPlayer) >= 48 * 48;
        if (now >= s.NextPlan && (movedTarget || s.BlockedSince > 0))
        {
            s.NextPlan = now + .6;
            var route = FindRoute(s, who, true); // once on-screen, never reposition to solve a blockage
            if (route != null)
            {
                s.Route = new Queue<Vector2>(route.Select(Pixel));
                s.TargetPlayer = who.Position; s.BlockedSince = 0;
            }
            else if (s.BlockedSince == 0) s.BlockedSince = now;
        }
        if (s.BlockedSince > 0 && now - s.BlockedSince > 2)
        {
            if (s.Departing) { s.Started = false; s.Start = now + 1.5; s.BlockedSince = 0; return; }
            Finish(s, "horse.call.no_path"); return;
        }
        float remaining = (float)(Math.Clamp(elapsed, 0, .05) * 320 * (1 + Configuration.SharedPreferences.Effective(Config).HorseSpeedBonus)); // default +20%, bounded real-time catch-up
        while (remaining > .001f && s.Route.Count > 0)
        {
            Vector2 target = s.Route.Peek(), p = horse.Position;
            if (Vector2.DistanceSquared(p, target) < .01f) { s.Route.Dequeue(); continue; }
            Vector2 next = p;
            float step = Math.Min(4, remaining);
            if (Math.Abs(target.X - p.X) > .01f) next.X += Math.Clamp(target.X - p.X, -step, step);
            else next.Y += Math.Clamp(target.Y - p.Y, -step, step);
            if (!Safe(s.Location, horse, next))
            { if (s.BlockedSince == 0) s.BlockedSince = now; return; }
            Vector2 delta = next - p;
            int face = Math.Abs(delta.X) > .001f ? (delta.X > 0 ? 1 : 3) : (delta.Y > 0 ? 2 : 0);
            if (horse.FacingDirection != face) horse.faceDirection(face);
            // Only the host writes the original Character NetPosition. Clients use native interpolation.
            horse.Position = next;
            remaining -= delta.Length();
        }
        if (s.Route.Count == 0)
        {
            if (movedTarget && now < s.NextPlan) return;
            if (s.Departing)
            {
                if (HiddenFromEveryone(s.Location, VisualBounds(horse.Position))) ReturnHome(s);
                else { s.Started = false; s.Start = now; }
                return;
            }
            if (!Mountable(s.Location, horse, who, horse.Position))
            { s.BlockedSince = now; s.NextPlan = now; return; }
            Finish(s, "");
        }
    }

    private void Finish(Summon state, string message)
    {
        if (!Active.Remove(state.Horse.HorseId)) return;
        var horse = state.Horse;
        // Drop only BSV's in-memory controller if a marker arrives late. Never clean
        // fields on a tractor, including legacy HorseRun/freeze/sprite state.
        if (!IsRealManagedHorse(horse) || IsTractorStable(state.Stable))
        { Reply(state.Request, message); return; }
        horse.modData.Remove(RunKey);
        Frozen(horse) = state.WasFrozen;
        if (horse.rider == null && !horse.mounting.Value && !horse.dismounting.Value)
        {
            horse.Halt(); horse.Sprite.CurrentAnimation = null;
            horse.Sprite.CurrentFrame = HorseRules.IdleFrame(horse.FacingDirection);
        }
        Reply(state.Request, message);
    }
}

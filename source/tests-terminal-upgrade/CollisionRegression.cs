using BetterStardewValley.Orders;
using System.Reflection;
using Microsoft.Xna.Framework;
using StardewValley;
using SObject = StardewValley.Object;

partial class Program
{
    static bool NativeHit(Vector2 tile) => Farm.isCollidingPosition(
        new Rectangle((int)tile.X * 64 + 8, (int)tile.Y * 64 + 8, 48, 48),
        new(0, 0, 1536, 1280), true, 0, false, Host, false, false, false, true);

    static void CollisionRegressionChecks()
    {
        Reset(); var anchor = Place(); var pos = anchor.TileLocation;
        // Current author design: 1x1 collision and 2x2 artwork. Old four-cell
        // regression is now a shrink-migration test, not a request to restore it.
        anchor.boundingBox.Value = new(256,256,64,0);
        Check(!TerminalEntity.Complete(Farm, anchor), "completeness check rejects zero-height serialized anchor collider");
        World.Repair(Farm);
        Check(TerminalEntity.Complete(Farm, anchor) && NativeHit(pos) && !NativeHit(pos+new Vector2(0,1)), "repair restores only the actual native collision cell");
        anchor.TileLocation = pos + new Vector2(1,0);
        Check(!TerminalEntity.Complete(Farm, anchor), "mispositioned anchor does not pass complete geometry check");
        World.Repair(Farm);
        Check(anchor.TileLocation == pos && NativeHit(pos), "repair restores a displaced anchor without an extra terminal");
        anchor.isTemporarilyInvisible = true;
        Check(!TerminalEntity.Complete(Farm, anchor), "invisible/passable anchor rejected instead of silently becoming a walking tile");
        World.Repair(Farm);
        Check(!anchor.isTemporarilyInvisible && !anchor.isPassable(), "repaired anchor is a solid native object");

        // Existing 2x1 device: remove only its old BSV reservation, not its neighbors.
        Reset(); anchor = Place(); string id = TerminalEntity.Id(anchor);
        anchor.boundingBox.Value = new Rectangle(256,256,128,64);
        Farm.objects[new(5,4)] = TerminalEntity.NewPart(Farm,new(5,4),anchor);
        anchor.modData["player-note"] = "preserved";
        var obstacle = new SObject("390", 17); Farm.objects[new(4, 5)] = obstacle;
        World.Repair(Farm);
        Check(!TerminalEntity.Pending(anchor) && Farm.objects.Count() == 2 && TerminalEntity.Resolve(Farm, new(5, 4), out var root)
            && ReferenceEquals(root, anchor) && ReferenceEquals(Farm.objects[new(4, 5)], obstacle) && obstacle.Stack==17, "2x1 shrink migration keeps device and neighbor, removes obsolete BSV reservation only");
        Check(!Notices.Contains(NativeTranslationFixture.Get("orders.terminal-migrate").ToString()) && anchor.modData["player-note"] == "preserved", "valid shrink needs no relocation and preserves metadata");
        Check(anchor.GetBoundingBox() == new Rectangle(256, 256, 64, 64) && anchor.boundingBox.Value == anchor.GetBoundingBox(), "anchor and serialized box never claim visual-only cells");
        Host.Items[0] = new StardewValley.Tools.Pickaxe(); string recovered = "";
        Check(World.Pickup(Farm, new(5, 4), Host, id, raw => recovered = raw) == null
            && TerminalItem.Read(recovered).Stack == 1 && TerminalItem.Read(recovered).modData["player-note"] == "preserved"
            && Farm.objects.Count() == 1 && ReferenceEquals(Farm.objects[new(4, 5)], obstacle), "pickup via old second cell returns one device and removes no neighboring item");

        Reset(); anchor = Place(); var invalid = TerminalEntity.NewPart(Farm,new(4,5),anchor); Farm.objects[new(4,5)] = invalid;
        invalid.boundingBox.Value = new Rectangle(256, 320, 64, 0);
        var furniture = new StardewValley.Objects.Furniture(); furniture.TileLocation = new(4, 5);
        furniture.boundingBox.Value = new Rectangle(256, 320, 64, 64); Farm.furniture.Add(furniture);
        World.Repair(Farm);
        Check(!TerminalEntity.Pending(anchor) && !Farm.objects.ContainsKey(new(4, 5)) && Farm.furniture.Contains(furniture),
            "shrink repair removes only old invalid reservation; never deletes existing furniture");

        foreach (var t in new[] { new Vector2(4, 4), new Vector2(5, 4), new Vector2(4, 5), new Vector2(5, 5) })
        {
            Reset(); var item = Item(); item.Stack = 3; Host.Items[0] = item;
            Farm.objects[t] = new SObject("390", 8);
            World.Network.Request(Farm, new(4, 4));
            if (t == new Vector2(4,4))
                Check(item.Stack == 3 && Farm.objects.Count() == 1 && !Host.modData.ContainsKey(TerminalNetwork.PendingKey), "blocked anchor refuses placement before inventory consumption");
            else
            {
                World.Network.Tick();
                Check(item.Stack == 2 && Farm.objects.Count() == 2 && Farm.objects[t].Stack==8
                    && !Host.modData.ContainsKey(TerminalNetwork.PendingKey), "visual-only neighbor does not block one-cell placement or lose items: " + t);
            }
            Reset(); Farm.map.GetLayer("Back").Tiles[(int)t.X, (int)t.Y].Properties["Water"] = "T";
            Check(TerminalEntity.CanPlace(Farm, new(4, 4), Host) == (t != new Vector2(4,4)), "water rejects actual anchor but does not create a four-cell footprint: " + t);
            Reset(); var layer = Farm.map.GetLayer("Buildings");
            layer.Tiles[(int)t.X, (int)t.Y] = new xTile.Tiles.StaticTile(layer, Farm.map.TileSheets[0], xTile.Tiles.BlendMode.Alpha, 0);
            Check(TerminalEntity.CanPlace(Farm, new(4, 4), Host) == (t != new Vector2(4,4)), "wall rejects actual anchor, leaving visual-only placement rules unchanged: " + t);
        }
    }

    static void TerminalAfkRoutingChecks()
    {
        Reset(); var anchor = Place(); Put(Host, Farm, 3, 4);
        var assembly = Assembly.LoadFrom(System.IO.Path.Combine(AppContext.BaseDirectory, "BetterStardewValley.dll"));
        var worldType = assembly.GetType("BetterStardewValley.AfkFishing.AfkWorld", true)!;
        var nodeType = assembly.GetType("BetterStardewValley.AfkFishing.TileNode", true)!;
        var memoryType = assembly.GetType("BetterStardewValley.AfkFishing.AfkNavigationMemory", true)!;
        var world = Activator.CreateInstance(worldType, Any, null, new object[] { Host }, null)!;
        foreach (var tile in TerminalEntity.Tiles(anchor.TileLocation))
            Check(!(bool)worldType.GetMethod("StaticClear", Any)!.Invoke(null, new object[] { Farm, new Point((int)tile.X, (int)tile.Y), Host, true })!, "production AFK static collision rejects terminal tile " + tile);
        object Node(int x, int y) => Activator.CreateInstance(nodeType, new object[] { "Farm", x, y })!;
        var goals = (System.Collections.IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(nodeType, typeof(int)))!;
        goals.Add(Node(7, 4), 0);
        var search = worldType.GetMethod("BeginRoute", Any)!.Invoke(world, new object[] { Node(3, 4), goals, Activator.CreateInstance(memoryType, true)!, 0d })!;
        var advance = search.GetType().GetMethod("Advance", Any)!;
        for (int i = 0; i < 60 && search.GetType().GetProperty("Status", Any)!.GetValue(search)!.ToString() == "Searching"; i++)
            advance.Invoke(search, new object[] { 256, 2.5d });
        var route = (System.Collections.IEnumerable?)search.GetType().GetProperty("Route", Any)!.GetValue(search);
        Check(route != null, "real BSV local route search finds a legal terminal detour");
        var footprint = TerminalEntity.Tiles(anchor.TileLocation).ToHashSet(); int steps = 0;
        foreach (var step in route!)
        {
            var to = step.GetType().GetProperty("To")!.GetValue(step)!;
            int x = (int)nodeType.GetProperty("X")!.GetValue(to)!, y = (int)nodeType.GetProperty("Y")!.GetValue(to)!;
            Check(!footprint.Contains(new Vector2(x, y)), "detour does not cross the physical terminal cell"); steps++;
        }
        Check(steps == 6, "open synthetic farm routes around terminal using shortest legal six steps");
    }
}

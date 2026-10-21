using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Xml.Serialization;
using BetterStardewValley;
using BetterStardewValley.Orders;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;
using SObject = StardewValley.Object;

partial class Program
{
    static void GeometryChecks()
    {
        var a = Place(); var tile = a.TileLocation;
        Check(Farm.objects.Count() == 1 && Farm.objects.Values.Count(o => !TerminalEntity.IsPart(o)) == 1, "one anchor and no visual-only collision reservations");
        Check(TerminalEntity.Bounds(tile) == new Rectangle(256, 256, 64, 64)
            && TerminalEntity.VisualBounds(tile) == new Rectangle(256, 256, 128, 128), "current design separates 1x1 physical bounds from 2x2 artwork");
        foreach (var cell in TerminalEntity.Tiles(tile))
        {
            Check(TerminalEntity.Resolve(Farm, cell, out var found) && ReferenceEquals(found, a), "every occupied tile resolves to the same terminal: " + cell);
            Check(Farm.IsTileOccupiedBy(cell) && !Farm.objects[cell].isPassable(), "native tile occupancy blocks walking/placement: " + cell);
            var body = new Rectangle((int)cell.X * 64 + 8, (int)cell.Y * 64 + 8, 48, 48);
            bool hit=Farm.isCollidingPosition(body, new(0, 0, 1536, 1280), true, 0, false, Host, false, false, false, true);
            Check(hit, "actual native Farmer collision hits the occupied anchor: " + cell);
            Check(Farm.objects[cell].GetBoundingBox() == (cell == tile ? TerminalEntity.Bounds(tile) : TerminalEntity.CellBounds(cell)), "anchor owns the full footprint; each reservation owns its full cell");
            Game1.activeClickableMenu = null; Farm.objects[cell].checkForAction(Host);
            Check(Game1.activeClickableMenu is OrdersMenu m && m.X == 4 && m.Y == 4 && m.Location == "Farm", "click routes directly to same Orders UI coordinates: " + cell);
            Check(!new SObject("390", 1).canBePlacedHere(Farm, cell), "other object cannot overlap a reserved cell");
        }
        Game1.activeClickableMenu = null;
        Check(!a.performToolAction(new Pickaxe()) && !a.onExplosion(Host) && Farm.objects.Count() == 1, "native tool/explosion cannot split or duplicate terminal");
        foreach (var visualOnly in new[] { new Vector2(5,4), new Vector2(4,5), new Vector2(5,5) })
            Check(TerminalEntity.Resolve(Farm, visualOnly, out var root) && ReferenceEquals(root,a)
                && !Farm.objects.ContainsKey(visualOnly) && !NativeHit(visualOnly), "visual-only area is clickable but does not reserve collision: " + visualOnly);
        foreach (var edge in new[] { new Point(3, 4), new Point(6, 5), new Point(4, 3), new Point(5, 6) })
        { Put(Host, Farm, edge.X, edge.Y); Check(TerminalEntity.CanUse(Host, "Farm", 4, 4), "interaction from all sides of full footprint: " + edge); }
        Put(Host, Farm, 8, 4); Check(!TerminalEntity.CanUse(Host, "Farm", 4, 4), "remote interaction rejected");
        Reset(); Put(Host, Home, 3, 4); Check(TerminalEntity.CanPlace(Home, new(4, 4), Host), "own farmhouse is a legal placement map"); Place();
        Put(Guest, Home, 3, 4); Check(!TerminalEntity.Allowed(Home, Guest), "another player's farmhouse is rejected for placement and Orders interaction");
        foreach (string name in new[] { "Town", "Forest", "Beach", "Mountain", "Mine", "UndergroundMine120", "Desert", "IslandWest", "IslandFarmHouse", "Shed", "Farm" })
        {
            var other = new GameLocation(); other.name.Value = name; other.map = Map(); Put(Host, other, 3, 4);
            Check(!TerminalEntity.CanPlace(other, new(4, 4), Host), "non-home-farm map cannot bypass by name: " + name);
            var item = Item(); int before = item.Stack;
            Check(World.Place(other, new(4, 4), Host, Guid.NewGuid().ToString("N"), item, () => { }) == "orders.terminal-map"
                && item.Stack == before && other.objects.Count() == 0, "host refuses illegal map without consuming/placing item: " + name);
        }
        Reset(); var second = new SObject("390", 9); Farm.objects[new(5, 5)] = second;
        Check(TerminalEntity.CanPlace(Farm, new(4, 4), Host), "visual-only neighbor does not enlarge physical placement requirements");
        Check(World.Place(Farm, new(4, 4), Host, Guid.NewGuid().ToString("N"), Item(), () => { }) == null && ReferenceEquals(Farm.objects[new(5, 5)], second), "single-cell placement preserves real visual-area neighbor and its interaction priority");
        Check(!TerminalEntity.Resolve(Farm,new(5,5),out _), "real neighboring object is not mistaken for terminal artwork");
        Reset(); var furniture = new Furniture(); furniture.TileLocation = new(4, 4); furniture.boundingBox.Value = new(256, 256, 128, 64); Farm.furniture.Add(furniture);
        Check(!TerminalEntity.CanPlace(Farm, new(4, 4), Host), "furniture intersection blocks terminal placement");
        Reset(); Farm.map.GetLayer("Back").Tiles[4, 4].Properties["Water"] = "T";
        Check(!TerminalEntity.CanPlace(Farm, new(4, 4), Host), "water in any footprint cell blocks placement");
        Reset(); Farm.warps.Add(new Warp(4, 4, "Town", 1, 1, false));
        Check(!TerminalEntity.CanPlace(Farm, new(4, 4), Host), "exit/warp tile cannot be covered");
        Reset(); Put(Host, Farm, 22, 18); Check(!TerminalEntity.CanPlace(Farm, new(24, 19), Host), "actual anchor outside map is rejected");
    }
    static void MigrationChecks()
    {
        Reset(); var a = Item(); a.TileLocation = new(4, 4); a.modData["external-metadata"] = "keep-中文"; Farm.objects[a.TileLocation] = a;
        World.Repair(Farm);
        Check(ReferenceEquals(a, Farm.objects[new(4, 4)]) && TerminalEntity.Complete(Farm, a) && Farm.objects.Count() == 1, "legacy 1x1 is stamped in place without adding reservations or replacing item");
        Check(a.modData["external-metadata"] == "keep-中文" && Host.modData.ContainsKey(OrderTerminal.LegacyDeliveryKey), "legacy migration preserves metadata and existing delivery marker");
        string id = TerminalEntity.Id(a); for (int i = 0; i < 5; i++) World.Repair(Farm);
        Check(Farm.objects.Count() == 1 && TerminalEntity.Id(a) == id, "repeated migration/reload does not duplicate anchor or parts");
        var serializer = new XmlSerializer(typeof(SObject));
        var saved = Farm.objects.Pairs.Select(p => { using var w = new StringWriter(); serializer.Serialize(w, p.Value); return (p.Key, Raw: w.ToString()); }).ToArray();
        Farm.objects.Clear(); foreach (var p in saved) { using var reader = new StringReader(p.Raw); Farm.objects.Add(p.Key, (SObject)serializer.Deserialize(reader)!); }
        World.Repair(Farm); a = Farm.objects[new(4, 4)];
        Check(TerminalEntity.Complete(Farm, a) && TerminalEntity.Id(a) == id && Farm.objects.Count() == 1, "native Object XML roundtrip restores single-cell structure and identity");
        var copy = TerminalItem.Read(TerminalItem.Pack(a));
        Check(copy.QualifiedItemId == a.QualifiedItemId && copy.Stack == 1 && copy.modData["external-metadata"] == "keep-中文" && TerminalEntity.Id(copy) == "", "portable item preserves non-geometry state but strips world reservation metadata");
        Reset(); a = Item(); a.TileLocation = new(4, 4); Farm.objects[a.TileLocation] = a;
        var block = new SObject("390", 18); Farm.objects[new(5, 5)] = block; World.Repair(Farm);
        Check(!TerminalEntity.Pending(a) && Farm.objects.Count() == 2 && ReferenceEquals(Farm.objects[new(5, 5)], block) && block.Stack == 18, "neighbor outside 1x1 footprint needs no expansion and remains untouched");
        Game1.activeClickableMenu = null; a.checkForAction(Host);
        Check(Game1.activeClickableMenu is OrdersMenu && !Notices.Contains(NativeTranslationFixture.Get("orders.terminal-migrate").ToString()), "valid legacy anchor remains usable without a false expansion notice");
        Game1.activeClickableMenu = null; Host.Items[0] = new Pickaxe(); string recovered = "";
        Check(World.Pickup(Farm, a.TileLocation, Host, TerminalEntity.Id(a), raw => recovered = raw) == null
            && TerminalItem.Read(recovered).Stack == 1 && Farm.objects.Count() == 1 && ReferenceEquals(Farm.objects[new(5, 5)], block), "blocked migration safely recovers exactly one terminal");
        Reset(); a = Place(); Farm.objects.Remove(a.TileLocation); World.Repair(Farm);
        Check(Farm.objects.Count() == 0, "destroyed/removed anchor leaves no orphan collision reservations");
        Reset(); a = Place(); Farm.objects.Remove(new(4, 4)); Farm.objects[new(8, 4)] = a; a.TileLocation = new(8, 4); World.Repair(Farm);
        Check(TerminalEntity.Complete(Farm, a) && Farm.objects.Count() == 1 && !Farm.objects.ContainsKey(new(5, 5)), "external same-map anchor move retains one-cell geometry with no old reservations");
        Reset(); a = Place(); var illegal = new GameLocation(); illegal.name.Value = "Town"; illegal.map = Map();
        Farm.objects.Remove(a.TileLocation); illegal.objects[new(4, 4)] = a; World.Repair(Farm); World.Repair(illegal); Put(Host, illegal, 3, 4);
        Check(Farm.objects.Count() == 0 && illegal.objects.Count() == 1 && TerminalEntity.Pending(a) && !TerminalEntity.CanUse(Host, "Town", 4, 4), "external cross-map move cannot enable terminal on a prohibited map");
        Host.Items[0] = new Pickaxe(); recovered = "";
        Check(World.Pickup(illegal, a.TileLocation, Host, TerminalEntity.Id(a), raw => recovered = raw) == null && TerminalItem.Read(recovered).Stack == 1, "illegal externally moved terminal can be recovered without enabling Orders there");
    }
    static TerminalRequest Request(Farmer who, string action, Vector2 tile, string id = "", long seq = 1) => new(1, Guid.NewGuid().ToString("N"), seq,
        who.UniqueMultiplayerID, action, who.currentLocation.NameOrUniqueName, (int)tile.X, (int)tile.Y, id, action == "place" ? TerminalItem.Pack(Item()) : "");
    static void Pending(Farmer who, TerminalRequest request) => who.modData[TerminalNetwork.PendingKey] = JsonSerializer.Serialize(request);
    static void NetworkChecks()
    {
        Reset(); Host.Items[0]=Item(); Host.Items[0].modData["portable-note"]="kept";
        World.Network.Request(Farm,new(4,4)); World.Network.Tick();
        Check(Farm.objects.Count()==1 && Host.Items.Count(OrderTerminal.IsTerminal)==0 && !Host.modData.ContainsKey(TerminalNetwork.PendingKey),"player request performs escrow, host placement and receipt completion through normal tick");
        var placed=Farm.objects[new(4,4)]; Host.Items[0]=new Pickaxe();
        World.Network.Request(Farm,placed.TileLocation,placed); World.Network.Tick();
        Check(Farm.objects.Count()==0 && Host.Items.Count(OrderTerminal.IsTerminal)==1 && Host.Items.Single(OrderTerminal.IsTerminal).modData["portable-note"]=="kept","normal pickup request returns one original portable item and metadata");
        Host.CurrentToolIndex=Host.Items.Select((i,n)=>(i,n)).Single(p=>OrderTerminal.IsTerminal(p.i)).n;
        World.Network.Request(Farm,new(4,4)); World.Network.Tick();
        Check(Farm.objects.Count()==1 && TerminalEntity.Complete(Farm,Farm.objects[new(4,4)]),"picked-up terminal can be placed again as one full entity");
        Reset(); var r = Request(Host, "place", new(4, 4)); Pending(Host, r); World.Network.Process(Host); World.Network.Process(Host);
        Check(Farm.objects.Count() == 1 && TerminalNetwork.Receipt(Host.UniqueMultiplayerID)!.Accepted, "replayed host placement intent creates one single-cell entity only");
        World.Network.Receive(Host); World.Network.Receive(Host);
        Check(!Host.modData.ContainsKey(TerminalNetwork.PendingKey) && Host.Items.All(i => i == null), "accepted placement clears escrow without refunding or duplicating an item");
        var a = Farm.objects[new(4, 4)]; Host.Items[0] = new Pickaxe(); Guest.Items[0] = new Pickaxe(); Put(Guest, Farm, 3, 4);
        var ra = Request(Host, "pickup", a.TileLocation, TerminalEntity.Id(a), 2); var rb = Request(Guest, "pickup", a.TileLocation, TerminalEntity.Id(a), 1);
        Pending(Host, ra); Pending(Guest, rb); World.Network.Process(Host); World.Network.Process(Guest);
        Put(Host,Farm,3,4); World.Network.Receive(Host); Put(Guest,Farm,3,4); World.Network.Receive(Guest); Put(Host,Farm,3,4); World.Network.Receive(Host);
        Check(Farm.objects.Count() == 0 && Host.Items.Concat(Guest.Items).Count(OrderTerminal.IsTerminal) == 1
            && !TerminalNetwork.Receipt(Guest.UniqueMultiplayerID)!.Accepted, "two actors racing pickup receive exactly one terminal in total");
        Reset(); r = Request(Host, "place", new(4, 4)); Pending(Host, r); Farm.objects[new(4, 4)] = new SObject("390", 30);
        World.Network.Process(Host); World.Network.Receive(Host); World.Network.Receive(Host);
        Check(Host.Items.Count(OrderTerminal.IsTerminal) == 1 && Farm.objects.Count() == 1 && Farm.objects[new(4, 4)].Stack == 30, "obstacle appearing after client request yields one refund and preserves obstacle");
        Reset(); r = Request(Host, "place", new(4, 4)); Pending(Host, r); Farm.objects[new(4, 4)] = new SObject("390", 1);
        for (int i = 0; i < 12; i++) Host.Items[i] = new SObject("390", 999);
        World.Network.Process(Host); World.Network.Receive(Host);
        Check(Host.modData.ContainsKey(TerminalNetwork.PendingKey) && Host.Items.All(i => i.Stack == 999), "full backpack retains refused placement escrow without deletion or overwrite");
        Host.Items[5] = null; World.Network.Receive(Host); World.Network.Receive(Host);
        Check(Host.Items.Count(OrderTerminal.IsTerminal) == 1 && !Host.modData.ContainsKey(TerminalNetwork.PendingKey), "freeing a slot completes refund once");
        Reset(); r = Request(Host, "place", new(4, 4)); Pending(Host, r); HostAuthority = false; Game1.multiplayerMode=1; World.Network.Process(Host); World.Repair(Farm);
        Check(Farm.objects.Count() == 0 && Host.modData.ContainsKey(TerminalNetwork.PendingKey), "farmhand cannot mutate world placement or migration");
        HostAuthority = true; Game1.multiplayerMode=0; IntegrationHost.Saving = true; World.Network.Process(Host); Check(Farm.objects.Count() == 0, "save barrier defers terminal world transaction");
        IntegrationHost.Saving = false; World.Network.Process(Host); var receipt = TerminalNetwork.Receipt(Host.UniqueMultiplayerID)!;
        // Recreate the controller; durable fields stand in for a disconnected client / reload.
        var fresh = new TerminalNetwork((StardewModdingAPI.IModHelper)Recorder.Make(typeof(StardewModdingAPI.IModHelper)), World);
        fresh.Process(Host); fresh.Receive(Host);
        Check(Farm.objects.Count() == 1 && TerminalNetwork.Receipt(Host.UniqueMultiplayerID) == receipt && !Host.modData.ContainsKey(TerminalNetwork.PendingKey), "new controller/reload recovers persisted result without a second mutation");
        Reset(); bool threw = false;
        try { World.Place(Farm, new(4, 4), Host, Guid.NewGuid().ToString("N"), Item(), () => { Host.modData[TerminalNetwork.ReceiptKey(Host.UniqueMultiplayerID)] = "partial"; throw new Exception("injected receipt failure"); }); }
        catch { threw = true; }
        Check(threw && Farm.objects.Count() == 0 && !Host.modData.ContainsKey(TerminalNetwork.ReceiptKey(Host.UniqueMultiplayerID)), "fault after placement before receipt rolls back physical anchor and receipt");
        a = Place(); Host.Items[0] = new Pickaxe(); threw = false;
        try { World.Pickup(Farm, a.TileLocation, Host, TerminalEntity.Id(a), _ => throw new Exception("injected pickup failure")); } catch { threw = true; }
        Check(threw && TerminalEntity.Complete(Farm, a) && Farm.objects.Count() == 1, "fault during pickup receipt restores full entity without giving a second item");
        Reset(); Host.Items[0] = Item(); int initial = Host.Items[0].Stack;
        var bad = new GameLocation(); bad.name.Value = "Beach"; bad.map = Map(); Put(Host, bad, 3, 4);
        World.Network.Request(bad, new(4, 4));
        Check(Host.Items[0].Stack == initial && !Host.modData.ContainsKey(TerminalNetwork.PendingKey) && Notices.Contains(NativeTranslationFixture.Get("orders.terminal-map").ToString()), "invalid player placement displays localized notice and never escrows inventory");
    }
    static void LightChecks()
    {
        Reset(); var a = Place(); Game1.timeOfDay = 1200; World.Lights.Update();
        Check(Game1.currentLightSources.Count == 0, "outdoor daytime creates no unnecessary light halo");
        Game1.timeOfDay = 2200; World.Lights.Update();
        Check(Game1.currentLightSources.Count == 1 && Farm.sharedLights.Count() == 0, "night creates one local native light, no replicated duplicates");
        var light = Game1.currentLightSources.Values.Single(); string lightId = light.Id;
        Check(light.textureIndex.Value == LightSource.lantern && ReferenceEquals(light.lightTexture, Game1.lantern), "light uses original soft circular lantern / Glow Ring texture");
        Check(light.position.Value == new Vector2(320, 320) && light.radius.Value * light.lightTexture.Width / 2 == 256, "light centered on terminal with four-tile texture radius");
        Check(light.color.Value == TerminalLighting.WarmTint && light.color.Value.A < 255 && light.onlyLocation.Value == "Farm", "warm subtractive tint and soft alpha restricted to Farm");
        for (int i = 0; i < 30; i++) World.Lights.Update();
        Check(Game1.currentLightSources.Count == 1 && ReferenceEquals(Game1.currentLightSources[lightId], light), "repeated updates reuse light instance and stable identity");
        var anotherScreen = new TerminalLighting(); anotherScreen.Update();
        Check(Game1.currentLightSources.Count == 1, "two observers of same replicated anchor cannot duplicate dictionary light");
        Farm.objects.Remove(new(4, 4)); Farm.objects[new(8, 4)] = a; a.TileLocation = new(8, 4); World.Repair(Farm); World.Lights.Update();
        Check(Game1.currentLightSources.Count == 1 && Game1.currentLightSources[lightId].position.Value == new Vector2(576, 320), "moved terminal updates light position with no old-position ghost");
        Put(Host, Farm, 7, 4); Host.Items[0] = new Pickaxe(); World.Pickup(Farm, a.TileLocation, Host, TerminalEntity.Id(a), _ => { }); World.Lights.Update();
        Check(Game1.currentLightSources.Count == 0, "pickup removes light");
        Reset(); a = Place(); Game1.timeOfDay = 2200; World.Lights.Update(); Farm.objects.Remove(a.TileLocation); World.Repair(Farm); World.Lights.Update();
        Check(Game1.currentLightSources.Count == 0, "anchor destruction removes light and reservations");
        Reset(); a = Place(); Game1.timeOfDay = 2200; World.Lights.Update(); Put(Host, Home, 3, 4); World.Lights.Update();
        Check(Game1.currentLightSources.Count == 0, "map switch removes outdoor light from local registry");
        Put(Host, Farm, 3, 4); Game1.currentLightSources.Clear(); World.Lights.Update();
        Check(Game1.currentLightSources.Count == 1, "return/load rebuilds cleared light registry from native saved anchor");
        Game1.timeOfDay = 600; World.Lights.Update(); Check(Game1.currentLightSources.Count == 0, "next morning clears nighttime light");
        Reset(); Put(Host, Home, 3, 4); Place(); Game1.timeOfDay = 2200; World.Lights.Update();
        Check(Game1.currentLightSources.Count == 0, "indoor placement does not illuminate a large outdoor-sized area");
    }
    record DrawCall(Vector2 Position, Rectangle? Source, float Scale, float Depth);
    static readonly List<DrawCall> Draws = new();
    static bool RecordDraw(Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
    { Draws.Add(new(position, sourceRectangle, scale, layerDepth)); return false; }
    static void DrawingChecks(Harmony harmony)
    {
        Reset(); var a = Place();
        Recorder.Textures["assets/orders/terminal-world.png"] = Texture(64, 128);
        Recorder.Textures["assets/orders/terminal-icon.png"] = Texture(64, 64);
        var draw = typeof(SpriteBatch).GetMethod("Draw", new[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float) })!;
        harmony.Patch(draw, prefix: new(typeof(Program), nameof(RecordDraw)));
        var batch = (SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch)); GC.SuppressFinalize(batch);
        Game1.viewport = new(0, 0, 1536, 1280); Draws.Clear();
        foreach (var p in Farm.objects.Pairs) p.Value.draw(batch, (int)p.Key.X, (int)p.Key.Y, 1);
        Check(Draws.Count == 1 && Draws[0].Source == TerminalEntity.WorldSource && Draws[0].Scale == 2 && Draws[0].Position == new Vector2(256, 256), "placed world draws existing lower 64x64 artwork once at exact 2x2 anchor");
        Draws.Clear(); OrderTerminal.DrawPlaced(batch, new(4, 4), .65f, false);
        Check(Draws.Count == 1 && Draws[0].Position == new Vector2(256, 256) && Draws[0].Source == TerminalEntity.WorldSource && Draws[0].Scale == 2, "placement ghost uses identical source, anchor and scale as placed terminal");
        Draws.Clear(); var item = Item(); item.drawInMenu(batch, new(0, 0), 1, 1, 1, StackDrawType.Hide, Color.White, false);
        Check(Draws.Count == 1 && Draws[0].Scale == 1 && Draws[0].Position == new Vector2(32, 32), "dedicated 64x64 menu/inventory icon stays unchanged");
        PreviewRegressionChecks(harmony, batch);
    }
}

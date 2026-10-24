using Eddie.FishingMachine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using SObject=StardewValley.Object;
namespace Eddie.FishingMachine;
internal sealed class WorldService
{
    private readonly ModEntry Mod;
    private readonly PerScreen<Chest?> OpeningChest=new(()=>null);
    private readonly PerScreen<long> OpenGeneration=new(()=>0);
    internal WorldService(ModEntry m)=>Mod=m;
    private static GridTile Grid(Vector2 p)=>new((int)p.X,(int)p.Y);
    private static Vector2 Vector(GridTile p)=>new(p.X,p.Y);
    internal static Facing GetFacing(SObject part)=>DirectionRules.Parse(part.modData.TryGetValue(ModEntry.Tag+"Facing",out var value)?value:null);
    internal static bool IsExtended(SObject part)=>part.modData.TryGetValue(ModEntry.Tag+"Schema",out var schema) && schema=="5";
    internal static GridTile[] Footprint(Chest c)=>SavedFootprint.Tiles(Grid(c.TileLocation),GetFacing(c),IsExtended(c));
    internal bool InReach(Farmer who,Chest c)=>SingleViewPlacement.InReach(Footprint(c),Grid(who.Tile))
        && who.currentLocation!=null && !Water(who.currentLocation,Grid(who.Tile));
    internal static bool Water(GameLocation l,GridTile p)
    {
        var v=Vector(p);
        // Like crab pots, shoreline artwork in the Buildings map layer is not an obstacle.
        // Passable water overlays (e.g. bridges) and explicitly non-fishable water are excluded.
        return l.isTileOnMap(v) && l.isWaterTile(p.X,p.Y)
            && l.doesTileHaveProperty(p.X,p.Y,"NoFishing","Back")==null
            && l.doesTileHaveProperty(p.X,p.Y,"Passable","Buildings")==null;
    }
    private static bool Usable(GameLocation l,GridTile p)
    {
        var v=Vector(p);
        return l.isTileOnMap(v) && !l.objects.ContainsKey(v)
            && !l.IsTileOccupiedBy(v,CollisionMask.All & ~CollisionMask.Flooring,useFarmerTile:true)
            && !l.IsLocationSpecificPlacementRestriction(v)
            && (Water(l,p) || !l.isWaterTile(p.X,p.Y) && l.isTilePassable(v)
                && l.doesTileHaveProperty(p.X,p.Y,"NoFurniture","Back")==null);
    }
    internal bool CanPlace(GameLocation l,Vector2 p,Facing facing,bool crab=false)=>facing==SingleViewPlacement.Orientation && l.canFishHere()
        && SingleViewPlacement.CanPlace(Grid(p),x=>Water(l,x),x=>Usable(l,x))
        && (!crab || SpriteGeometry.CrabWaterValid(Grid(p),x=>Water(l,x)));
    internal bool CanPlace(GameLocation l,Vector2 p,bool crab=false)=>CanPlace(l,p,SingleViewPlacement.Orientation,crab);
    internal bool InReach(Farmer who,Vector2 a,Facing facing)=>SingleViewPlacement.InReach(PreparationRules.Footprint(Grid(a)),Grid(who.Tile))
        && who.currentLocation!=null && !Water(who.currentLocation,Grid(who.Tile));
    internal (Vector2 Tile,Facing Facing) PlacementTarget(ICursorPosition cursor)
    {
        var l=Game1.currentLocation;
        var clicked=Grid(Game1.options.gamepadControls?cursor.GrabTile:cursor.Tile);
        var target=SingleViewPlacement.Find(clicked,Grid(Game1.player.Tile),p=>Water(l,p),p=>Usable(l,p) && (!ModEntry.IsCrab(Game1.player.ActiveObject)||Water(l,p)),
            p=>!ModEntry.IsCrab(Game1.player.ActiveObject)||SpriteGeometry.CrabWaterValid(p,x=>Water(l,x)));
        return (Vector(target??clicked),SingleViewPlacement.Orientation);
    }
    internal void CancelOpening(){OpenGeneration.Value++;OpeningChest.Value=null;}
    internal void Reset(){CancelOpening();}
    internal void Place(ICursorPosition cursor)
    {var target=PlacementTarget(cursor);Mod.Actions.Place(Game1.currentLocation,target.Tile,target.Facing);}
    internal void Remove(Chest c)=>Mod.Actions.Remove(c);

    // Only the host reaches these mutators. The requesting client has already put one machine in escrow.
    internal string? PlaceHost(GameLocation l,Vector2 a,Facing facing,Farmer who,string requestId,string? machineState,Action commitReceipt,string itemId=ModEntry.Id)
    {
        bool crab=itemId==ModEntry.CrabId;
        if(!Context.IsMainPlayer || !MachineKinds.Matches(itemId,machineState))return "network.error";
        if(who.currentLocation!=l || !CanPlace(l,a,facing,crab) || !InReach(who,a,facing))return "placement.invalid";
        var initial=machineState==null?new ProductionState{Kind=crab?MachineKinds.Crab:MachineKinds.Fishing}:ProductionState.Load(machineState);
        string identity=crab && Guid.TryParseExact(initial.PersistentId,"N",out _)?initial.PersistentId:requestId;
        if(Mod.Machines.All.Any(e=>e.Key.modData[ModEntry.Tag+"MachineId"]==identity))return "network.error";
        if(crab){initial.PersistentId=identity;initial.OwnerId=who.UniqueMultiplayerID;machineState=initial.Save();}
        var entries=new List<(Vector2 Tile,SObject Object)>();
        foreach(var p in PreparationRules.Footprint(Grid(a)))
        {
            var v=Vector(p);
            SObject o=p==Grid(a)?new Chest(true,v,itemId):new SObject(v,itemId);
            o.Location=l;o.owner.Value=who.UniqueMultiplayerID;
            o.modData[ModEntry.Tag+"Schema"]=SingleViewPlacement.Schema;
            o.modData[ModEntry.Tag+"MachineId"]=identity;
            o.modData[ModEntry.Tag+"Role"]=p==Grid(a)?"anchor":"reserved";
            o.modData[ModEntry.Tag+"AnchorX"]=((int)a.X).ToString(System.Globalization.CultureInfo.InvariantCulture);
            o.modData[ModEntry.Tag+"AnchorY"]=((int)a.Y).ToString(System.Globalization.CultureInfo.InvariantCulture);
            o.modData[ModEntry.Tag+"Facing"]=facing.ToString().ToLowerInvariant();
            if(o is Chest chest && machineState!=null)
            {
                chest.modData[ProductionService.StateKey]=machineState;
                chest.SpecialChestType=Chest.SpecialChestTypes.BigChest;
            }
            entries.Add((v,o));
        }
        try
        {
            foreach(var e in entries)l.objects.Add(e.Tile,e.Object);
            commitReceipt();
            foreach(var e in entries)Mod.Machines.Add(l,e.Object);
            return null;
        }
        catch(Exception ex)
        {
            foreach(var e in entries)if(l.objects.TryGetValue(e.Tile,out var o) && ReferenceEquals(o,e.Object))l.objects.Remove(e.Tile);
            Mod.Monitor.Log($"Placement rolled back: {ex}",LogLevel.Error);return "network.error";
        }
    }
    internal bool TryAnchor(GameLocation l,Vector2 tile,out Chest? anchor)
    {
        anchor=null;
        if(!l.objects.TryGetValue(tile,out var part) || !ModEntry.IsPart(part))return false;
        if(!part.modData.TryGetValue(ModEntry.Tag+"AnchorX",out var xs) || !part.modData.TryGetValue(ModEntry.Tag+"AnchorY",out var ys)
            || !int.TryParse(xs,out int x) || !int.TryParse(ys,out int y))return false;
        if(!l.objects.TryGetValue(new(x,y),out var o) || !ModEntry.IsAnchor(o)
            || o.modData[ModEntry.Tag+"MachineId"]!=part.modData[ModEntry.Tag+"MachineId"])return false;
        anchor=(Chest)o;return true;
    }
    internal void Open(Chest c)
    {
        if(!Context.IsWorldReady || Mod.Saving || !Context.IsPlayerFree || OpeningChest.Value!=null)return;
        if(c.Location!=Game1.currentLocation || !InReach(Game1.player,c)){Mod.Message("access.bank");return;}
        OpeningChest.Value=c;
        long generation=++OpenGeneration.Value;
        c.GetMutex().RequestLock(()=>
        {
            if(generation!=OpenGeneration.Value)
            {if(c.GetMutex().IsLockHeld())c.GetMutex().ReleaseLock();return;}
            OpeningChest.Value=null;
            if(!Context.IsWorldReady || Mod.Saving || !Context.IsPlayerFree || c.Location!=Game1.currentLocation || !InReach(Game1.player,c)
                || !c.Location.objects.TryGetValue(c.TileLocation,out var o) || !ReferenceEquals(c,o))
            {if(c.GetMutex().IsLockHeld())c.GetMutex().ReleaseLock();return;}
            try {c.SpecialChestType=Chest.SpecialChestTypes.BigChest;Mod.OpenChest.Value=c;c.ShowMenu();Game1.playSound("openChest");}
            catch(Exception ex){Mod.ReleaseChest();Mod.Monitor.Log($"Could not open fishing machine: {ex}",LogLevel.Error);}
        },()=>
        {
            if(generation!=OpenGeneration.Value)return;
            OpeningChest.Value=null;if(Context.IsWorldReady)Mod.Message("access.busy");
        });
    }
    internal void RemoveHost(GameLocation l,Vector2 a,string machineId,Farmer who,Action<string> commitReceipt,Action<string?> done)
    {
        if(!Context.IsMainPlayer){done("network.error");return;}
        if(!TryAnchor(l,a,out var anchor) || anchor!.modData[ModEntry.Tag+"MachineId"]!=machineId)
        {done("remove.gone");return;}
        var c=anchor;
        // Resolve pending vanilla open requests before attempting to acquire this chest's mutex.
        c.GetMutex().Update(l);
        if(c.GetMutex().IsLocked()){done("access.busy");return;}
        c.GetMutex().RequestLock(()=>
        {
            string? result=null;
            try
            {
                if(!Context.IsWorldReady || Mod.Saving || who.currentLocation!=l || !InReach(who,c))result="access.bank";
                else if(!l.objects.TryGetValue(c.TileLocation,out var current) || !ReferenceEquals(c,current))result="remove.gone";
                else if(who.CurrentTool is not (StardewValley.Tools.Axe or StardewValley.Tools.Pickaxe))result="remove.tool";
                else
                {
                    // Host-owned maintenance latch: finish already-paid production, but no
                    // refuelling/restart/upgrade can race the player's attempt to empty it.
                    // The first hit switches the machine into "preparing pickup": tell the player in a dialog box
                    // (not a brief red line) so an accidental hit is noticed and can be undone with R / Y.
                    bool latched = !c.modData.ContainsKey(ProductionService.RecoveryKey);
                    c.modData[ProductionService.RecoveryKey] = "true";
                    if (Mod.Production.BlocksRemoval(c)) { result=(latched?"dialog:":"")+"remove.finishing"; return; }
                    if (!c.isEmpty()) { result=(latched?"dialog:":"")+"remove.empty-first"; return; }
                    var returned = ItemRegistry.Create(c.QualifiedItemId);
                    returned.modData[ProductionService.StateKey] = Mod.Production.Portable(c);
                    var entries=new List<(Vector2 Tile,SObject Object)>();
                    foreach(var p in Footprint(c))
                    {
                        var v=Vector(p);
                        if(!l.objects.TryGetValue(v,out var o) || !ModEntry.IsPart(o) || o.modData[ModEntry.Tag+"MachineId"]!=machineId)
                        {result="remove.damaged";break;}
                        entries.Add((v,o));
                    }
                    if(result==null)
                    {
                        try{string portable=Mod.Production.Portable(c);foreach(var e in entries)l.objects.Remove(e.Tile);commitReceipt(portable);Mod.Machines.Remove(c);}
                        catch
                        {
                            foreach(var e in entries)if(!l.objects.ContainsKey(e.Tile))l.objects.Add(e.Tile,e.Object);
                            throw;
                        }
                    }
                }
            }
            catch(Exception ex){Mod.Monitor.Log($"Removal rolled back: {ex}",LogLevel.Error);result="network.error";}
            finally{c.GetMutex().ReleaseLock(); done(result);}
        },()=>done("access.busy"));
        c.GetMutex().Update(l);
    }
    internal void DrawMachine(SpriteBatch b,Vector2 a,float alpha=1,Chest? chest=null,Facing? preview=null)
    {
        string phase=Mod.Production.Phase(chest);
        bool bait=chest!=null && Mod.Production.HasBait(chest);
        int bob=chest!=null&&!ModEntry.IsCrab(chest)?Mod.Art.BobOffset(a):0;
        Mod.Art.Draw(b,a,phase,bait,alpha,bob,chest);

    }
    internal void DrawPreview(SpriteBatch b)
    {
        if(!Context.IsWorldReady || !Context.IsPlayerFree || !ModEntry.IsMachine(Game1.player.ActiveObject))return;
        var target=PlacementTarget(Mod.Helper.Input.GetCursorPosition());
        var a=target.Tile;
        var direction=target.Facing;
        bool ok=!Mod.Actions.HasPending && CanPlace(Game1.currentLocation,a,direction,ModEntry.IsCrab(Game1.player.ActiveObject)) && InReach(Game1.player,a,direction);
        foreach(var p in PreparationRules.Footprint(Grid(a)))b.Draw(Game1.mouseCursors,Game1.GlobalToLocal(Game1.viewport,Vector(p)*64),
            new Rectangle(ok?194:210,388,16,16),Color.White,0,Vector2.Zero,4,SpriteEffects.None,1);
        DrawMachine(b,a,.55f,preview:direction);
        string label=Mod.T("placement.hint");
        Utility.drawTextWithShadow(b,label,Game1.smallFont,Game1.GlobalToLocal(Game1.viewport,a*64)+new Vector2(0,144),Color.White);
    }
    internal void Audit()
    {
        if(!Context.IsMainPlayer || !Context.IsWorldReady)return;
        foreach(var entry in Mod.Machines.All)
        {
            var c=entry.Key;var l=entry.Value;
            if(Footprint(c).Any(p=>!TryAnchor(l,Vector(p),out var same) || !ReferenceEquals(same,c)))
                Mod.Monitor.Log($"Damaged footprint in {l.NameOrUniqueName} at {c.TileLocation}; no unrelated tiles changed.",LogLevel.Warn);
        }
    }
    // Read-only diagnostics at the real candidate anchor. Never starts a cycle or moves inventory.
    internal void Diagnose(Vector2 a)
    {
        if(!Context.IsWorldReady){Mod.Monitor.Log(Mod.T("diag.load-save"),LogLevel.Info);return;}
        var l=Game1.currentLocation;
        Mod.Monitor.Log($"Map={l.NameOrUniqueName}, type={l.GetType().FullName}, original Sewer={BetterStardewValley.MutantLobsterKing.MutantService.IsSewer(l)}, anchor={a}, canFishHere={l.canFishHere()}, reach={InReach(Game1.player,a,SingleViewPlacement.Orientation)}",LogLevel.Info);
        foreach(var p in SpriteGeometry.CrabWaterTiles(Grid(a)))
        {
            var v=Vector(p);bool on=l.isTileOnMap(v);
            Mod.Monitor.Log($"tile={p.X},{p.Y}; footprint={PreparationRules.Footprint(Grid(a)).Contains(p)}; onMap={on}; rawWater={on&&l.isWaterTile(p.X,p.Y)}; legalWater={Water(l,p)}; usable={on&&Usable(l,p)}; object={l.objects.ContainsKey(v)}; NoFishing={l.doesTileHaveProperty(p.X,p.Y,"NoFishing","Back")}; bridgePassable={l.doesTileHaveProperty(p.X,p.Y,"Passable","Buildings")}; action={l.doesTileHaveProperty(p.X,p.Y,"Action","Buildings")}; warp={l.warps.Any(w=>w.X==p.X&&w.Y==p.Y)}",LogLevel.Info);
        }
        Mod.Monitor.Log($"Fishing placement={CanPlace(l,a,false)}; crab placement={CanPlace(l,a,true)}; crab extra-water={SpriteGeometry.CrabWaterValid(Grid(a),p=>Water(l,p))}",LogLevel.Info);
        var habitats=l.GetCrabPotFishForTile(a).ToHashSet(StringComparer.Ordinal);
        var crab=CrabRules.NativePool(DataLoader.Fish(Game1.content),habitats);
        var fish=FishPool.Get(l,a,SingleViewPlacement.Orientation,Game1.player);
        Mod.Monitor.Log($"Crab habitat={string.Join(',',habitats)}; ordinary crab pool={string.Join(',',crab.Select(p=>p.Id))}; fishing Lv1 eligible={string.Join(',',fish.Select(p=>p.Id))}; empty pool prevents start without consuming bait. Diagnostics are not a completed placement/production test.",LogLevel.Info);
    }
}

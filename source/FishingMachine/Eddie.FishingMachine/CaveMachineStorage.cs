using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using System.Reflection;
using SObject=StardewValley.Object;
namespace Eddie.FishingMachine;

/// <summary>Native save inventory holds detached snapshots of machines in generated, unsaved cave maps.</summary>
internal sealed class CaveMachineStorage
{
    internal const string InventoryId=ModEntry.Tag+"CaveMachines", LocationKey=ModEntry.Tag+"CaveLocation";
    private readonly ModEntry Mod;
    private readonly Dictionary<string,Chest> Pending=new();
    internal bool Active {get;private set;}
    internal CaveMachineStorage(ModEntry mod)=>Mod=mod;
    internal static bool Generated(GameLocation l)=>l is MineShaft or VolcanoDungeon;
    internal static bool HasMachine(GameLocation l)=>l.objects.Values.Any(ModEntry.IsAnchor);
    internal bool Protect(GameLocation l)=>Active && Context.IsWorldReady && Context.IsMainPlayer && HasMachine(l);
    private static string Id(Chest c)=>c.modData[ModEntry.Tag+"MachineId"];

    private static Item Copy(Item item)
    {
        if(item is not Chest source)
        {var one=item.getOne();one.Stack=item.Stack;return one;}
        var copy=new Chest(true,source.TileLocation,source.ItemId){SpecialChestType=source.SpecialChestType,Stack=source.Stack,Quality=source.Quality};
        copy.owner.Value=source.owner.Value;
        copy.playerChoiceColor.Value=source.playerChoiceColor.Value;
        copy.modData.CopyFrom(source.modData);
        foreach(var i in source.Items)if(i!=null)copy.Items.Add(Copy(i));
        return copy;
    }
    internal void Save()
    {
        if(!Active || !Context.IsMainPlayer)return;
        // Fully prepare before replacing saved snapshots. Never share a NetFields tree between two owners.
        var next=new Dictionary<string,Chest>();
        foreach(var pair in Pending)next.Add(pair.Key,(Chest)Copy(pair.Value));
        foreach(var pair in Mod.Machines.All)
        {
            var c=pair.Key;var l=pair.Value;
            if(!Generated(l) || !l.objects.TryGetValue(c.TileLocation,out var current) || !ReferenceEquals(c,current))continue;
            var copy=(Chest)Copy(c);copy.modData[LocationKey]=l.NameOrUniqueName;
            next[Id(c)]=copy;
        }
        var bank=Game1.MasterPlayer.team.GetOrCreateGlobalInventory(InventoryId);
        var before=bank.ToArray();
        try {bank.Clear();foreach(var c in next.Values)bank.Add(c);}
        catch {bank.Clear();foreach(var item in before)bank.Add(item);throw;}
    }
    internal void Load()
    {
        Pending.Clear();Active=Context.IsMainPlayer;
        if(!Active)return;
        foreach(var item in Game1.MasterPlayer.team.GetOrCreateGlobalInventory(InventoryId))
            if(item is Chest c && ModEntry.IsAnchor(c) && c.modData.ContainsKey(LocationKey))
                Pending.TryAdd(Id(c),(Chest)Copy(c));
        Restore();
    }
    private static GameLocation? Find(string name)
    {
        if(MineShaft.IsGeneratedLevel(name,out _))return MineShaft.GetMine(name);
        if(VolcanoDungeon.IsGeneratedLevel(name,out _))return VolcanoDungeon.GetLevel(name);
        return Game1.getLocationFromName(name);
    }
    internal void Restore()
    {
        if(!Active || !Context.IsWorldReady || !Context.IsMainPlayer)return;
        foreach(var pair in Pending.ToArray())
        {
            try
            {
                var c=pair.Value;var l=Find(c.modData[LocationKey]);
                if(l==null)continue;
                // Repeated load hooks must not instantiate another copy of an existing machine.
                if(l.objects.Values.Any(o=>ModEntry.IsAnchor(o) && o.modData[ModEntry.Tag+"MachineId"]==pair.Key))
                {Pending.Remove(pair.Key);continue;}
                if(!Mod.World.CanPlace(l,c.TileLocation,ModEntry.IsCrab(c)))
                {
                Mod.Monitor.Log(Mod.T("diag.cave-retained")+$"\nMachine {pair.Key}: {l.NameOrUniqueName} {c.TileLocation} occupied/unfishable.",LogLevel.Warn);
                    continue;
                }
                Install(c,l,c.TileLocation);Pending.Remove(pair.Key);
            }
            catch(Exception ex){Mod.Monitor.Log($"Cave machine {pair.Key} retained for recovery: {ex}",LogLevel.Error);}
        }
    }
    private void Install(Chest saved,GameLocation l,Vector2 tile)
    {
        var c=(Chest)Copy(saved);c.Location=l;c.TileLocation=tile;
        c.modData.Remove(LocationKey);
        string id=Id(c);
        var placed=new List<(Vector2 Tile,SObject Item)>();
        foreach(var p in PreparationRules.Footprint(new((int)tile.X,(int)tile.Y)))
        {
            var pos=new Vector2(p.X,p.Y);
            SObject part=pos==tile?c:new SObject(pos,c.ItemId);
            part.Location=l;part.owner.Value=c.owner.Value;
            part.modData[ModEntry.Tag+"MachineId"]=id;
            part.modData[ModEntry.Tag+"Schema"]=SingleViewPlacement.Schema;
            part.modData[ModEntry.Tag+"Role"]=pos==tile?"anchor":"reserved";
            part.modData[ModEntry.Tag+"AnchorX"]=((int)tile.X).ToString(System.Globalization.CultureInfo.InvariantCulture);
            part.modData[ModEntry.Tag+"AnchorY"]=((int)tile.Y).ToString(System.Globalization.CultureInfo.InvariantCulture);
            part.modData[ModEntry.Tag+"Facing"]="right";
            placed.Add((pos,part));
        }
        try
        {
            foreach(var p in placed)l.objects.Add(p.Tile,p.Item);
            Mod.Machines.Add(l,c);
        }
        catch
        {
            foreach(var p in placed)if(l.objects.TryGetValue(p.Tile,out var same) && ReferenceEquals(same,p.Item))l.objects.Remove(p.Tile);
            Mod.Machines.Remove(c);throw;
        }
    }
    internal void Recover()
    {
        if(!Context.IsWorldReady || !Context.IsMainPlayer || Mod.Saving)return;
        if(Pending.Count==0){Mod.Monitor.Log(Mod.T("cave.none"),LogLevel.Info);return;}
        var l=Game1.currentLocation;var player=Game1.player;
        var tiles=from y in Enumerable.Range((int)player.Tile.Y-4,9) from x in Enumerable.Range((int)player.Tile.X-4,9)
                  let p=new Vector2(x,y) orderby Math.Abs(x-player.Tile.X)+Math.Abs(y-player.Tile.Y) select p;
        foreach(var p in tiles)
        {
            if(!Mod.World.InReach(player,p,Facing.Right) || !Mod.World.CanPlace(l,p,ModEntry.IsCrab(Pending.First().Value)))continue;
            var next=Pending.First();Install(next.Value,l,p);Pending.Remove(next.Key);
            Mod.Monitor.Log(Mod.T("cave.recovered"),LogLevel.Info);return;
        }
        Mod.Monitor.Log(Mod.T("cave.water"),LogLevel.Info);
    }
    internal void Reset()
    {
        Active=false;Pending.Clear();
        if(!Context.IsMainPlayer)return;
        // Release only cave instances that our cleanup patches kept alive this session.
        foreach(var mine in MineShaft.activeMines.Where(HasMachine).ToArray())
        {MineShaft.activeMines.Remove(mine);mine.OnRemoved();}
        foreach(var cave in VolcanoDungeon.activeLevels.Where(HasMachine).ToArray())
        {VolcanoDungeon.activeLevels.Remove(cave);cave.OnRemoved();}
    }
}

/// <summary>Let vanilla clean unused floors; temporarily exclude floors containing BSV machines.</summary>
[HarmonyPatch]
internal static class KeepMachineMinesPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MineShaft),"clearActiveMines");
        yield return AccessTools.Method(typeof(MineShaft),"clearInactiveMines");
    }
    static void Prefix(out List<MineShaft> __state)
    {
        __state=MineShaft.activeMines.Where(m=>ModEntry.Instance.Caves.Protect(m)).ToList();
        foreach(var m in __state)MineShaft.activeMines.Remove(m);
    }
    static Exception? Finalizer(Exception? __exception,List<MineShaft>? __state)
    {
        if(__state!=null)foreach(var m in __state)if(!MineShaft.activeMines.Contains(m))MineShaft.activeMines.Add(m);
        return __exception;
    }
}
[HarmonyPatch(typeof(VolcanoDungeon),nameof(VolcanoDungeon.ClearAllLevels))]
internal static class KeepMachineVolcanoPatch
{
    static void Prefix(out List<VolcanoDungeon> __state)
    {
        __state=VolcanoDungeon.activeLevels.Where(m=>ModEntry.Instance.Caves.Protect(m)).ToList();
        foreach(var m in __state)VolcanoDungeon.activeLevels.Remove(m);
    }
    static Exception? Finalizer(Exception? __exception,List<VolcanoDungeon>? __state)
    {
        if(__state!=null)foreach(var m in __state)if(!VolcanoDungeon.activeLevels.Contains(m))VolcanoDungeon.activeLevels.Add(m);
        return __exception;
    }
}

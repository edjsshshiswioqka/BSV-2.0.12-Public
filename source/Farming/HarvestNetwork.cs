using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace BetterStardewValley.Farming;

internal sealed record HarvestRequest(int Version,string Id,long Actor,string Crop,long Generation,string Location,int X,int Y,string Seed,int Quality,string Products,string Hut,bool Drop);
internal sealed record HarvestReceipt(string Id,string Products,bool Duplicate);
internal sealed record HarvestClaim(long Generation,string Request);

internal sealed class HarvestNetwork
{
    internal const string Outbox=FertilizerService.Prefix+"Harvest/",Receipts=FertilizerService.Prefix+"HarvestReceipt/",Claims=FertilizerService.Prefix+"CropClaim/";
    private readonly FertilizerService Service;
    private static readonly Lazy<XmlSerializer> Serializer=new(()=>new(typeof(Item[])));
    internal HarvestNetwork(FertilizerService service)=>Service=service;
    internal static string Pack(IEnumerable<Item> items)
    {
        using var writer=new StringWriter(CultureInfo.InvariantCulture);
        Serializer.Value.Serialize(writer,items.ToArray());return writer.ToString();
    }
    internal static Item[] Unpack(string raw)
    {
        if(raw.Length>2_000_000)throw new InvalidDataException("Oversized harvest products; retained.");
        using var reader=XmlReader.Create(new StringReader(raw),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2_000_000});
        var items=Serializer.Value.Deserialize(reader) as Item[]??throw new InvalidDataException("Invalid harvest products.");
        if(items.Length>4096||items.Any(i=>i==null||i.Stack<1||i.Stack>100000))throw new InvalidDataException("Invalid harvest stack.");
        return items;
    }
    internal void Submit(Crop crop,HoeDirt soil,Farmer who,IReadOnlyList<Item> items,string eventId,string seed,bool seeds,double chance,int regrow,JunimoHarvester? junimo,Vector2 position,bool drop)
    {
        if(!Guid.TryParseExact(crop.modData.GetValueOrDefault(FertilizerService.CropKey),"N",out _))return;
        var parts=eventId.Split('/');if(parts.Length!=2||!long.TryParse(parts[1],out long generation))throw new InvalidDataException("Missing harvest generation.");
        if(junimo!=null&&!Context.IsMainPlayer)return;
        who=junimo==null?who:Game1.MasterPlayer;
        string id=Guid.NewGuid().ToString("N");
        var r=new HarvestRequest(1,id,who.UniqueMultiplayerID,parts[0],generation,soil.Location.NameOrUniqueName,(int)soil.Tile.X,(int)soil.Tile.Y,
            seed,soil.GetFertilizerQualityBoostLevel(),Pack(items),junimo?.home?.id.Value.ToString("N")??"",drop);
        who.modData[Outbox+id]=JsonSerializer.Serialize(r);
        // Preserve identity on the crop itself. Concurrent attempts observed the same generation;
        // the host claim permits only one award. The next regrowth observes a new generation.
        crop.modData[FertilizerService.GenerationKey]=checked(generation+1).ToString(CultureInfo.InvariantCulture);
        Tick();
    }
    internal void Tick()
    {
        if(!Context.IsWorldReady||IntegrationHost.Saving)return;
        if(Context.IsMainPlayer)
            foreach(var who in Game1.getAllFarmers())
                foreach(string key in who.modData.Keys.Where(k=>k.StartsWith(Outbox,StringComparison.Ordinal)).Take(32).ToArray())
                    try{Process(who,key);}catch(Exception ex){Service.Error(ex);}
        if(Context.IsMainPlayer)
        {
            var pending=Game1.getAllFarmers().SelectMany(f=>f.modData.Keys.Where(k=>k.StartsWith(Outbox,StringComparison.Ordinal))).Select(k=>k[Outbox.Length..]).ToHashSet(StringComparer.Ordinal);
            foreach(string key in Game1.MasterPlayer.modData.Keys.Where(k=>k.StartsWith(Receipts,StringComparison.Ordinal)&&!pending.Contains(k[Receipts.Length..])).Take(32).ToArray())
                Game1.MasterPlayer.modData.Remove(key);
        }
        var local=Game1.player;
        foreach(string key in local.modData.Keys.Where(k=>k.StartsWith(Outbox,StringComparison.Ordinal)).Take(32).ToArray())
            try{Receive(local,key);}catch(Exception ex){Service.Error(ex);}
    }
    internal void Process(Farmer who,string key)
    {
        if(!Context.IsMainPlayer)return;
        var r=JsonSerializer.Deserialize<HarvestRequest>(who.modData[key])??throw new InvalidDataException("Empty harvest request.");
        if(r.Version!=1||r.Actor!=who.UniqueMultiplayerID||key!=Outbox+r.Id||!Guid.TryParseExact(r.Id,"N",out _)||!Guid.TryParseExact(r.Crop,"N",out _)
            ||r.Generation<0||r.Quality is <0 or >3||r.Location.Length>256)throw new InvalidDataException("Invalid harvest request; retained.");
        string receiptKey=Receipts+r.Id;
        if(Game1.MasterPlayer.modData.ContainsKey(receiptKey))return;
        var previous=Game1.MasterPlayer.modData.TryGetValue(Claims+r.Crop,out var raw)?JsonSerializer.Deserialize<HarvestClaim>(raw):null;
        if(previous!=null&&r.Generation<=previous.Generation)
        {Game1.MasterPlayer.modData[receiptKey]=JsonSerializer.Serialize(new HarvestReceipt(r.Id,Pack(Array.Empty<Item>()),true));return;}
        var items=Unpack(r.Products);
        if(!Crop.TryGetData(r.Seed,out var cropData))throw new InvalidDataException("Crop data removed before bonus settlement; request preserved.");
        string harvestId=FertilizerRules.Id(cropData.HarvestItemId);
        if(items.Any(i=>i.QualifiedItemId!=harvestId))throw new InvalidDataException("Harvest output identity changed; request preserved.");
        var config=Configuration.SharedPreferences.Current;
        int multiplier=HarvestRules.YieldMultiplier(r.Quality,Random.Shared.NextDouble(),config.HarvestBonus);
        var rewards=new List<Item>();
        foreach(var item in items)for(int n=1;n<multiplier;n++){var copy=item.getOne();copy.Stack=item.Stack;rewards.Add(copy);}
        if(config.EnableHarvestSeeds&&ItemRegistry.GetData(FertilizerRules.Id(r.Seed))!=null&&HarvestRules.Wins(Random.Shared.NextDouble(),config.SeedChance))
            rewards.Add(ItemRegistry.Create(FertilizerRules.Id(r.Seed),1));
        string encoded=JsonSerializer.Serialize(new HarvestReceipt(r.Id,Pack(rewards),false));
        string? before=Game1.MasterPlayer.modData.GetValueOrDefault(Claims+r.Crop);
        try
        {
            Game1.MasterPlayer.modData[Claims+r.Crop]=JsonSerializer.Serialize(new HarvestClaim(r.Generation,r.Id));
            Game1.MasterPlayer.modData[receiptKey]=encoded;
        }
        catch
        {
            if(before==null)Game1.MasterPlayer.modData.Remove(Claims+r.Crop);else Game1.MasterPlayer.modData[Claims+r.Crop]=before;
            Game1.MasterPlayer.modData.Remove(receiptKey);throw;
        }
    }
    internal void Receive(Farmer who,string key)
    {
        if(!who.IsLocalPlayer)return;
        var r=JsonSerializer.Deserialize<HarvestRequest>(who.modData[key])!;
        if(!Game1.MasterPlayer.modData.TryGetValue(Receipts+r.Id,out var raw))return;
        var receipt=JsonSerializer.Deserialize<HarvestReceipt>(raw)!;
        if(receipt.Id!=r.Id)return;
        var location=Game1.getLocationFromName(r.Location);if(location==null)return;
        var items=Unpack(receipt.Products);
        Chest? chest=null;
        if(r.Hut!="")
        {
            if(!Context.IsMainPlayer)return;
            Utility.ForEachBuilding<JunimoHut>(hut=>{if(hut.id.Value.ToString("N")==r.Hut){chest=hut.GetOutputChest();return false;}return true;},true);
            // If the hut was demolished after harvest, deliver the owed extras as
            // debris at the recorded crop position instead of leaving an unclaimable receipt.
        }
        var inventory=who.Items.ToArray();var stacks=inventory.Select(i=>i?.Stack??0).ToArray();
        var hutItems=chest?.Items.ToArray();var hutStacks=hutItems?.Select(i=>i?.Stack??0).ToArray();
        var debris=location.debris.ToArray();string pending=who.modData[key];
        try
        {
            foreach(var item in items)
            {
                Item? remainder=item;
                if(chest!=null)remainder=chest.addItem(item);
                else if(!r.Drop&&r.Hut=="")remainder=who.addItemToInventory(item);
                if(remainder!=null)Game1.createItemDebris(remainder,new(r.X*64+32,r.Y*64+32),-1,location);
            }
            who.modData.Remove(key);
        }
        catch
        {
            who.Items.Clear();for(int i=0;i<inventory.Length;i++){if(inventory[i]!=null)inventory[i].Stack=stacks[i];who.Items.Add(inventory[i]);}
            if(chest!=null&&hutItems!=null){chest.Items.Clear();for(int i=0;i<hutItems.Length;i++){if(hutItems[i]!=null)hutItems[i].Stack=hutStacks![i];chest.Items.Add(hutItems[i]);}}
            foreach(var added in location.debris.Where(d=>!debris.Contains(d)).ToArray())location.debris.Remove(added);
            who.modData[key]=pending;throw;
        }
        // Host receipts stay durable for replay rejection; claims prevent duplicate peers.
    }
}

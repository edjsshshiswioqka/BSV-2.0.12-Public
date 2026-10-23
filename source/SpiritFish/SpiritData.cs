using StardewValley.GameData.Objects;
using StardewValley.GameData.Locations;
using StardewValley.GameData.FishPonds;
using Newtonsoft.Json;
using StardewValley;

namespace BetterStardewValley.SpiritFish;

internal static class SpiritData
{
    internal static void Objects(IDictionary<string,ObjectData> data, Func<string,string> tr)
    {
        // Edible like the loaded Legend (vanilla 500 energy / 225 health). Smoked Spirit Fish then follows the
        // native x1.5 rule instead of the inedible-fish rule (price x0.3 = 4500 energy).
        int edibility=data.TryGetValue(SpiritRules.LegendFish,out var legend)&&legend.Edibility>0?legend.Edibility:SpiritRules.LegendEdibilityFallback;
        int PriceOf(string id,int fallback)=>data.TryGetValue(id,out var o)?o.Price:fallback;
        data[SpiritRules.Id] = new ObjectData { Name="Spirit Fish", DisplayName=tr("spirit.fish.name"), Description=tr("spirit.fish.description"),
            Type="Fish", Category=-4, Price=SpiritRules.Price, Edibility=edibility, Texture=SpiritRules.TextureRoot+"fish", SpriteIndex=0,
            ContextTags=new(){SpiritRules.Tag,"fish_legendary","fish_has_roe","color_purple"}, ExcludeFromRandomSale=true };
        data[SpiritRules.SoupId] = new ObjectData { Name="Spirit Fish Soup", DisplayName=tr("spirit.soup.name"), Description=tr("spirit.soup.description"),
            Type="Cooking", Category=-7, Price=SpiritRules.SoupPrice(SpiritRules.Price,PriceOf(SpiritRules.Truffle,625),PriceOf(SpiritRules.Morel,150)), Edibility=90, Texture=SpiritRules.TextureRoot+"soup", SpriteIndex=0,
            ContextTags=new(){"bsv_seafood"}, ExcludeFromRandomSale=true };
    }
    internal static void Fish(IDictionary<string,string> data)
    {
        if(!data.TryGetValue("163",out var legend))throw new InvalidOperationException("Spirit Fish requires the loaded Legend Data/Fish row.");
        var fields=legend.Split('/');
        if(fields.Length<13 || !int.TryParse(fields[1],out _))throw new InvalidOperationException("Unsupported Legend Data/Fish schema.");
        // Inherit the actual loaded Legend difficulty, movement and size; no guessed minigame constants.
        fields[0]="Spirit Fish";fields[5]="1800 2600";fields[6]="spring summer fall winter";fields[7]="both";fields[10]="1";fields[12]="10";
        data[SpiritRules.Id]=string.Join('/',fields);
    }
    internal static void Locations(IDictionary<string,LocationData> data)
    {
        // No probabilistic location row: native candidate retries would multiply the chance.
        // Only remove BSV's own former row. Selection now occurs once, before the rod sees its fish.
        foreach(var location in data.Values)location.Fish?.RemoveAll(p=>p.Id==SpiritRules.Id);
    }

    internal static void Cooking(IDictionary<string,string> data,Func<string,string> tr) =>
        data[SpiritRules.SoupId]=$"{SpiritRules.Id} 1 430 1 257 1/Home/{SpiritRules.SoupId}/s Fishing 10/{tr("spirit.soup.name")}";
    internal static void Ponds(IList<FishPondData> data)
    {
        // Clone the loaded legendary pond's economy, rather than copying shipped game data.
        var tags=ItemContextTagManager.GetBaseContextTags("(O)163");
        var original=data.Where(p=>p.Id!=SpiritRules.Id && p.RequiredTags!=null && ItemContextTagManager.DoAllTagsMatch(p.RequiredTags,tags)).OrderBy(p=>p.Precedence).FirstOrDefault()
            ??throw new InvalidOperationException("No loaded legendary fish pond template; Spirit pond registration stopped safely.");
        var pond=JsonConvert.DeserializeObject<FishPondData>(JsonConvert.SerializeObject(original))!;
        pond.Id=SpiritRules.Id;pond.RequiredTags=new(){SpiritRules.Tag};pond.Precedence=int.MinValue+100;
        pond.MaxPopulation=1;pond.PopulationGates=null;
        pond.WaterColor=new(){new(){Id=SpiritRules.Id,Color="55 215 245",MinPopulation=1}};
        SpiritPondRewards.Apply(pond);
        for(int i=data.Count-1;i>=0;i--)if(data[i].Id==SpiritRules.Id)data.RemoveAt(i);
        data.Add(pond);
    }
}

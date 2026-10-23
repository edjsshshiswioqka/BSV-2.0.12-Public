using Eddie.FishingMachine.Core;
using StardewValley.GameData.Objects;
namespace BetterStardewValley.KingCrab;

// Author-approved 850g, base stamina/health 150 each. Native energy uses Edibility 60;
// KingCrabFoodClock supplies the separate health amount (vanilla otherwise uses 45% of energy).
internal sealed record BowlBalance(int Price,int Edibility);
internal static class KingCrabData
{
    internal const string TextureRoot="Mods/Eddie.BetterStardewValley/KingCrab/";
    internal static BowlBalance ApprovedBowl {get;}=new(950,60);
    internal static void Objects(IDictionary<string,ObjectData> data,Func<string,string> tr,BowlBalance? bowl)
    {
        void Add(string id,ObjectData value)
        {
            if(data.TryGetValue(id,out var old)&&old.Name!=value.Name)throw new InvalidOperationException("King Crab ID collision: "+id);
            data[id]=value;
        }
        Add(KingCrabRules.Id,new(){Name="King Crab",DisplayName=tr("kingcrab.fish.name"),Description=tr("kingcrab.fish.description"),
            Type="Fish",Category=-4,Price=KingCrabRules.Price,Edibility=KingCrabRules.RawEdibility,Texture=TextureRoot+"fish",SpriteIndex=0,
            ExcludeFromRandomSale=true,ContextTags=new(){"bsv_seafood","bsv_king_crab"}});
        if(bowl==null)return;
        if(bowl.Price<=0||bowl.Edibility<=0)throw new ArgumentOutOfRangeException(nameof(bowl),"Approved positive price and food edibility are required.");
        Add(KingCrabRules.BowlId,new(){Name="King Crab Rice Bowl",DisplayName=tr("kingcrab.bowl.name"),Description=tr("kingcrab.bowl.description"),
            Type="Cooking",Category=-7,Price=bowl.Price,Edibility=bowl.Edibility,IsDrink=false,Texture=TextureRoot+"bowl",SpriteIndex=0,
            ExcludeFromRandomSale=true,ContextTags=new(){"bsv_seafood"},Buffs=new(){new(){Id=KingCrabRules.BowlId,Duration=KingCrabRules.Minutes,
                CustomAttributes=new(){Defense=KingCrabRules.Defense,Immunity=KingCrabRules.Immunity}}}});
    }
    internal static void Fish(IDictionary<string,string> data)
    {
        if(!data.TryGetValue("715",out var row))throw new InvalidOperationException("Loaded Lobster trap data missing.");
        var f=row.Split('/');if(f.Length<5||f[1]!="trap")throw new InvalidOperationException("Unsupported trap data schema.");
        f[0]="King Crab";f[2]="0";f[4]="ocean";data[KingCrabRules.Id]=string.Join('/',f);
    }
    internal static void Cooking(IDictionary<string,string> data,Func<string,string> tr,BowlBalance? bowl)
    {
        if(bowl!=null)data[KingCrabRules.BowlId]=$"{KingCrabRules.Id} 1 423 1 136 1/Home/{KingCrabRules.BowlId} 1/none/{tr("kingcrab.bowl.name")}";
    }
}

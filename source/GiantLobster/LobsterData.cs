using Eddie.FishingMachine.Core;
using StardewValley;
using StardewValley.GameData.Objects;
namespace BetterStardewValley.GiantLobster;

internal static class LobsterBalance
{
    internal const string Potato="192", Kale="250";
    internal const int OldFishPrice=450,BaitPrice=60,DinnerPrice=700,FoodEdibility=64;
    internal const double PriceFactor=1.5;   // Giant Lobster sells for 1.5x a vanilla Lobster (120g -> 180g)
    internal const int BaitLevel=8,DinnerLevel=7;
    internal static int VanillaLobsterPrice()=>Game1.objectData!=null&&Game1.objectData.TryGetValue("715",out var o)&&o.Price>0?o.Price:120;
    internal static int FishPrice(int vanillaLobster)=>(int)(vanillaLobster*PriceFactor);
    // 1.6.15 food data Duration uses 700 ms units, not seconds. Native food/seasoning
    // modifiers and paused buff ticking remain native; base duration = 420,000 ms.
    internal const double EdibleFactor=1.5;
    internal const int FishingBuff=3,DurationGameMinutes=600,DinnerCount=1;
}
internal static class LobsterData
{
    internal const string TextureRoot="Mods/Eddie.BetterStardewValley/GiantLobster/";
    internal static void Objects(IDictionary<string,ObjectData> data,Func<string,string> tr)
    {
        void Add(string id,string name,string kind,string type,int category,int price,int edibility)
        {
            if(data.TryGetValue(id,out var old)&&old.Name!=name)throw new InvalidOperationException("Giant Lobster ID collision: "+id);
            data[id]=new(){Name=name,DisplayName=tr("lobster."+kind+".name"),Description=tr("lobster."+kind+".description"),Type=type,Category=category,
                Price=price,Edibility=edibility,Texture=TextureRoot+kind,SpriteIndex=0,ExcludeFromRandomSale=true,
                ContextTags=kind=="bait"?new(){"bsv_giant_lobster_bait"}:new(){"bsv_seafood"}};
        }
        // Edible at 1.5x a vanilla Lobster. Vanilla Lobster is inedible raw; eaten smoked it counts as price x0.3
        // (120g -> 36 = 90 energy / 40 health), so the Giant Lobster gets 54 (135 / 60). Smoking it then
        // follows the native x1.5 rule (81 = 203 / 91).
        int lobsterPrice=data.TryGetValue("715",out var vanilla)&&vanilla.Price>0?vanilla.Price:120;
        Add(GiantLobsterRules.Id,"Giant Lobster","fish","Fish",-4,LobsterBalance.FishPrice(lobsterPrice),(int)(lobsterPrice*.3*LobsterBalance.EdibleFactor));
        Add(GiantLobsterRules.BaitId,"Giant Lobster Bait","bait","Basic",-21,LobsterBalance.BaitPrice,-300);
        data[GiantLobsterRules.BaitId].CanBeGivenAsGift=false;
        Add(GiantLobsterRules.DinnerId,"Lobster Dinner","dinner","Cooking",-7,LobsterBalance.DinnerPrice,LobsterBalance.FoodEdibility);
        data[GiantLobsterRules.DinnerId].Buffs=new(){new(){Id=GiantLobsterRules.DinnerId,Duration=LobsterBalance.DurationGameMinutes,
            CustomAttributes=new(){FishingLevel=LobsterBalance.FishingBuff}}};
    }
    internal static void Fish(IDictionary<string,string> data)
    {
        if(!data.TryGetValue("715",out var lobster))throw new InvalidOperationException("Loaded vanilla Lobster Data/Fish row is missing.");
        var fields=lobster.Split('/');if(fields.Length<5||fields[1]!="trap")throw new InvalidOperationException("Unsupported vanilla Lobster schema.");
        fields[0]="Giant Lobster";fields[2]="0";fields[4]="ocean";
        // Registration only: both production adapters use a fixed 3% roll before ordinary
        // species/junk selection, and exclude this row from all ordinary fallback pools.
        data[GiantLobsterRules.Id]=string.Join('/',fields);
    }
    internal static void Crafting(IDictionary<string,string> data,Func<string,string> tr)=>
        data[GiantLobsterRules.BaitId]=$"{GiantLobsterRules.Id} 1/Field/{GiantLobsterRules.BaitId} 5/false/s Fishing {LobsterBalance.BaitLevel}/{tr("lobster.bait.name")}";
    internal static void Cooking(IDictionary<string,string> data,Func<string,string> tr)=>
        data[GiantLobsterRules.DinnerId]=$"{GiantLobsterRules.Id} 1 256 1 {LobsterBalance.Potato} 1 {LobsterBalance.Kale} 1/Home/{GiantLobsterRules.DinnerId} {LobsterBalance.DinnerCount}/s Fishing {LobsterBalance.DinnerLevel}/{tr("lobster.dinner.name")}";
    internal static void Learn(Farmer farmer)
    {
        if(farmer.fishingLevel.Value>=LobsterBalance.BaitLevel)farmer.craftingRecipes.TryAdd(GiantLobsterRules.BaitId,0);
        if(farmer.fishingLevel.Value>=LobsterBalance.DinnerLevel)farmer.cookingRecipes.TryAdd(GiantLobsterRules.DinnerId,0);
    }
}

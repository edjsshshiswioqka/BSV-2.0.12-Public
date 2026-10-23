using Eddie.FishingMachine.Core;
using StardewValley.GameData.Objects;
using StardewValley.GameData.FishPonds;
namespace BetterStardewValley.LobsterKing;

internal sealed record SashimiBalance(int Edibility);

internal static class KingData
{
    internal const string TextureRoot="Mods/Eddie.BetterStardewValley/LobsterKing/",Tag="bsv_lobster_king";
    // Edibility 80 is native 200 energy. KingSashimi mirrors native energy for health.
    internal static SashimiBalance ApprovedSashimi=>new(80);
    internal static void Objects(IDictionary<string,ObjectData> data,Func<string,string> tr,SashimiBalance? sashimi=null)
    {
        if(data.TryGetValue(LobsterKingRules.Id,out var old)&&old.Name!="Lobster King")throw new InvalidOperationException("Lobster King ID collision.");
        data[LobsterKingRules.Id]=new(){Name="Lobster King",DisplayName=tr("king.fish.name"),Description=tr("king.fish.description"),
            Type="Fish",Category=-4,Price=LobsterKingRules.Price,Edibility=-300,Texture=TextureRoot+"fish",SpriteIndex=0,ExcludeFromRandomSale=true,
            ContextTags=new(){Tag,"bsv_seafood"}};
        // Callers explicitly opt into the approved food definition.
        if(sashimi==null)return;
        if(sashimi.Edibility<=0)throw new ArgumentOutOfRangeException(nameof(sashimi));
        if(data.TryGetValue(LobsterKingRules.SashimiId,out var prior)&&prior.Name!="Lobster King Sashimi")throw new InvalidOperationException("King sashimi ID collision.");
        data[LobsterKingRules.SashimiId]=new(){Name="Lobster King Sashimi",DisplayName=tr("king.sashimi.name"),Description=tr("king.sashimi.description"),
            Type="Cooking",Category=-7,Price=2000,Edibility=sashimi.Edibility,IsDrink=false,Texture=TextureRoot+"sashimi",SpriteIndex=0,
            ExcludeFromRandomSale=true,Buffs=new(){new(){Id=LobsterKingRules.SashimiId,Duration=600,CustomAttributes=new(){Attack=6,Speed=2}}}};
    }
    internal static void Cooking(IDictionary<string,string> data,Func<string,string> tr,bool enabled)
    {if(enabled)data[LobsterKingRules.SashimiId]=$"{LobsterKingRules.Id} 1/Home/{LobsterKingRules.SashimiId} 1/none/{tr("king.sashimi.name")}";}
    internal static void Fish(IDictionary<string,string> data)
    {
        if(!data.TryGetValue("715",out var lobster))throw new InvalidOperationException("Loaded Lobster trap row missing.");
        var fields=lobster.Split('/');if(fields.Length<5||fields[1]!="trap")throw new InvalidOperationException("Unsupported Lobster schema.");
        fields[0]="Lobster King";fields[2]="0";fields[4]="ocean";data[LobsterKingRules.Id]=string.Join('/',fields);
    }
    internal static FishPondData Pond(IReadOnlyList<string> gate)
    {
        if(gate==null||gate.Count==0||gate.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("Native Lobster pond upgrade candidates are unavailable.");
        return new(){Id=LobsterKingRules.Id,RequiredTags=new(){Tag},Precedence=int.MinValue+101,
            MaxPopulation=2,PopulationGates=new(){[2]=gate.ToList()},SpawnTime=5,
            BaseMinProduceChance=0,BaseMaxProduceChance=0,ProducedItems=new()};
    }
    internal static void Ponds(IList<FishPondData> data,IReadOnlyList<string>? approvedGate)
    {
        for(int n=data.Count-1;n>=0;n--)if(data[n].Id==LobsterKingRules.Id)data.RemoveAt(n);
        if(approvedGate!=null)data.Add(Pond(approvedGate));
    }
    internal static void Ponds(IList<FishPondData> data)
    {
        // Do not load other assets while editing this one. The native-data postfix
        // resolves the current Lobster gate after all content edits have completed.
        for(int n=data.Count-1;n>=0;n--)if(data[n].Id==LobsterKingRules.Id)data.RemoveAt(n);
        data.Add(new(){Id=LobsterKingRules.Id,RequiredTags=new(){Tag},Precedence=int.MinValue+101,
            MaxPopulation=2,PopulationGates=new(),SpawnTime=5,
            BaseMinProduceChance=0,BaseMaxProduceChance=0,ProducedItems=new()});
    }
}

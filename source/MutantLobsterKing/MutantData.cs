using Eddie.FishingMachine.Core;
using StardewValley.GameData.Objects;
[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("MutantNativeChecks")]
namespace BetterStardewValley.MutantLobsterKing;

internal static class MutantData
{
    internal const string Texture="Mods/Eddie.BetterStardewValley/MutantLobsterKing";
    internal static void Objects(IDictionary<string,ObjectData> data,Func<string,string> tr)
    {
        if(data.TryGetValue(MutantLobsterRules.Id,out var old)&&old.Name!="Mutant Lobster King")
            throw new InvalidOperationException("Mutant Lobster King stable ID collision.");
        data[MutantLobsterRules.Id]=new(){Name="Mutant Lobster King",DisplayName=tr("mutant.name"),Description=tr("mutant.description"),
            Type="Basic",Category=0,Price=MutantLobsterRules.Price,Edibility=-300,Texture=Texture,SpriteIndex=0,
            CanBeGivenAsGift=false,ExcludeFromRandomSale=true,ContextTags=new(){"fish_pond_ignore","bsv_mutant_lobster_collectible"}};
        // Intentionally no Data/Fish, recipes, ponds, Roe, mail or legendary registrations.
    }
}

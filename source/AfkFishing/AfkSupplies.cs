using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;
using Newtonsoft.Json;
using SObject = StardewValley.Object;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    private sealed record BaitPreference(string Id,int Quality,string? Parent,int? Preserve,Dictionary<string,string> Metadata);
    // Only stacking identity is saved. Refilling still consumes actual inventory under the
    // existing ownership/lock checks; the one-item comparison template is never awarded.
    private void RememberBait()
    {
        if (Game1.player.CurrentTool is not FishingRod rod || !rod.CanUseBait()) return;
        if (rod.attachments[0] is SObject bait && bait.GetType()==typeof(SObject)
            && bait.Category == SObject.baitCategory && !bait.questItem.Value && !bait.isRecipe.Value)
        {
            if(Current.BaitTemplate?.canStackWith(bait)==true && !string.IsNullOrEmpty(Current.Data.BaitPreference))return;
            Current.BaitTemplate = (SObject)bait.getOne();
            string saved=JsonConvert.SerializeObject(new BaitPreference(bait.QualifiedItemId,bait.Quality,bait.preservedParentSheetIndex.Value,
                bait.preserve.Value.HasValue?(int)bait.preserve.Value.Value:null,bait.modData.Keys.OrderBy(k=>k,StringComparer.Ordinal).ToDictionary(k=>k,k=>bait.modData[k])));
            if(saved!=Current.Data.BaitPreference){Current.Data.BaitPreference=saved;Persist();}
        }
        else if(rod.attachments[0]==null && Current.BaitTemplate==null && !string.IsNullOrEmpty(Current.Data.BaitPreference))
        {
            try
            {
                if(Current.Data.BaitPreference.Length>16384)throw new JsonSerializationException("Bait preference is too large.");
                var saved=JsonConvert.DeserializeObject<BaitPreference>(Current.Data.BaitPreference,new JsonSerializerSettings{MaxDepth=8,TypeNameHandling=TypeNameHandling.None});
                if(saved==null||string.IsNullOrEmpty(saved.Id)||!saved.Id.StartsWith("(O)",StringComparison.Ordinal)||saved.Metadata==null
                    ||saved.Quality is not(0 or 1 or 2 or 4)||saved.Preserve.HasValue&&!Enum.IsDefined(typeof(SObject.PreserveType),saved.Preserve.Value))return;
                var template=ItemRegistry.Create<SObject>(saved.Id,quality:saved.Quality,allowNull:true);
                if(template==null||template.GetType()!=typeof(SObject)||template.Category!=SObject.baitCategory)return;
                template.preservedParentSheetIndex.Value=saved.Parent;template.preserve.Value=saved.Preserve.HasValue?(SObject.PreserveType)saved.Preserve.Value:null;
                foreach(var pair in saved.Metadata)template.modData[pair.Key]=pair.Value;
                Current.BaitTemplate=template;
            }
            catch(JsonException){Current.Data.BaitPreference="";Persist();}
        }
    }
    private bool NeedsBait() => Current.BaitTemplate != null && Game1.player.CurrentTool is FishingRod rod
        && rod.CanUseBait() && (rod.attachments[0]?.Stack ?? 0) < Current.BaitTemplate.maximumStackSize();
    private bool TryTopUpBait(Chest chest)
        => TryTopUpBaitFrom(chest.GetItemsForPlayer(Game1.player.UniqueMultiplayerID));
    private bool TryTopUpBaitFrom(IList<Item> items)
    {
        if (!NeedsBait() || Game1.player.CurrentTool is not FishingRod rod) return false;
        var template = Current.BaitTemplate!; var attached = rod.attachments[0];
        if (attached != null && !template.canStackWith(attached)) return false;
        for (int slot = 0; slot < items.Count; slot++)
        {
            if (items[slot] is not SObject item || item.GetType() != typeof(SObject) || item.questItem.Value
                || item.isRecipe.Value || item.Category != SObject.baitCategory || !template.canStackWith(item)) continue;
            int old = item.Stack, before = attached?.Stack ?? 0;
            int take = Math.Min(old, Math.Max(0, template.maximumStackSize() - before));
            if (take == 0) continue;
            var target = attached ?? (SObject)item.getOne();
            try
            {
                target.Stack = before + take; rod.attachments[0] = target;
                item.Stack -= take; if (item.Stack == 0) items[slot] = null;
                return true;
            }
            catch
            {
                item.Stack = old; items[slot] = item;
                if (attached != null) attached.Stack = before;
                rod.attachments[0] = attached; throw;
            }
        }
        return false;
    }
}

using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Objects;
using Eddie.FishingMachine.Core;
using MachineMod = Eddie.FishingMachine.ModEntry;

namespace BetterStardewValley;

/// <summary>Read-only, primitive snapshots for integrations and future HUD adapters.</summary>
public sealed class BsvApi
{
    public int GetApiVersion() => 1;
    public IDictionary<string, string>? GetFishingMachineState(GameLocation location, Vector2 tile)
    {
        var mod = MachineMod.Instance;
        if (!Context.IsWorldReady || mod?.Ready != true || !mod.World.TryAnchor(location, tile, out var chest)) return null;
        try
        {
            var state = mod.Production.Read(chest!);
            return new Dictionary<string, string>
            {
                ["Kind"] = state.Kind, ["Level"] = state.Level.ToString(), ["Phase"] = state.Phase,
                ["CompletedCycles"] = state.CompletedCycles.ToString(), ["LevelCycles"] = state.LevelCycles.ToString(),
                ["RequiredBait"] = mod.Production.RequiredBait(chest!).ToString(),
                ["MinutesRemaining"] = Math.Max(0, state.DueAt - Eddie.FishingMachine.ProductionService.Now).ToString(),
                ["PendingItems"] = state.Pending.Sum(p => (long)p.Count).ToString(),
                ["Capacity"] = chest!.GetActualCapacity().ToString(), ["OwnerId"] = chest.owner.Value.ToString(),
                ["ProductionOwnerId"] = state.OwnerId.ToString(), ["EffectiveRolls"] = (state.Crab?.Rolls??state.BaitUsed).ToString(),
                ["ActualBaitUsed"] = state.BaitUsed.ToString(), ["Luremaster"] = (state.Crab?.Luremaster??false).ToString(),
                ["ArtOCrabbing"] = (state.Crab?.Book??false).ToString(), ["AutoTrash"] = state.AutoTrash.ToString(), ["StatusText"] = mod.Production.Status(chest)
            };
        }
        catch { return new Dictionary<string, string> { ["Kind"] = MachineMod.IsCrab(chest)?MachineKinds.Crab:MachineKinds.Fishing, ["Phase"] = "error" }; }
    }
    public IDictionary<string, string>? GetTeaState(GameLocation location, Vector2 tile)
    {
        if (!Context.IsWorldReady || !TeaFeature.IsInitialized) return null;
        Bush? bush = location.terrainFeatures.TryGetValue(tile, out var feature) ? feature as Bush : null;
        if (bush == null && location.objects.TryGetValue(tile, out var obj) && obj is IndoorPot pot) bush = pot.bush.Value;
        if (!TeaFeature.Managed(bush)) return null;
        var state = TeaFeature.Read(bush!);
        return new Dictionary<string, string>
        {
            ["Kind"] = "TeaBush", ["Growth"] = state.Growth.ToString(), ["GrowthRequired"] = TeaAppleRules.GrowthDays.ToString(),
            ["Regrow"] = state.Regrow.ToString(), ["Ready"] = state.Ready.ToString(), ["Dead"] = state.Dead.ToString(),
            ["WateredToday"] = (TeaFeature.Get(bush!, TeaAppleRules.WaterKey) == TeaFeature.Today).ToString()
        };
    }
}

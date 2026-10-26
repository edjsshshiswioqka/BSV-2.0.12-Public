namespace BetterStardewValley;

public sealed class ModConfig
{
    public int ConfigSchemaVersion { get; set; } = 5;
    public bool AutoRestartProduction { get; set; } = true;
    public bool EnableAllProfessions { get; set; } = true;
    /// <summary>Skills that keep the native one-of-two choice although All Professions is on ("Farming", "Mining", "Foraging", "Fishing", "Combat" or a SpaceCore skill id).</summary>
    public string[] AllProfessionsExcludedSkills { get; set; } = Array.Empty<string>();
    public bool EnableHorseSpeedBonus { get; set; } = true;
    public bool EnableOrdersSystem { get; set; } = true;
    /// <summary>Applied to unexpanded maps. Already expanded saves keep their recorded layout.</summary>
    public bool EnableGreenhouseExpansion { get; set; } = true;
    public bool EnableCellarExpansion { get; set; } = true;
    public bool EnableCellarAppearance { get; set; } = true;
    public bool EnableBsvSpeedGro { get; set; } = true;
    public HarvestBonusOptions HarvestBonus { get; set; } = new();
    public double HorseSpeedBonus { get; set; } = .20;
    /// <summary>Exact qualified object IDs excluded from newly generated contracts only.</summary>
    public string[] OrderItemExclusions { get; set; } = Array.Empty<string>();
    public int HomeTerminalPlacementDistance { get; set; } = 3;
    public AfkFishing.AfkOptions AfkFishing { get; set; } = new();
    public bool EnableHarvestSeeds { get; set; } = true;
    public double SeedChance { get; set; } = 0.5;
    public bool EnableAnnualLegendaryFish { get; set; } = true;
    public StardewModdingAPI.SButton CallHorseKey { get; set; } = StardewModdingAPI.SButton.H; // switched to J at runtime only while another mod uses H
    public double CallHorseCooldownSeconds { get; set; } = 1;
    public int HorseStopDistance { get; set; } = 3;
    public int HorseSpawnOutsideViewportMinTiles { get; set; } = 2;
    public int HorseSpawnOutsideViewportMaxTiles { get; set; } = 5;
    public bool EnableHorseNameUI { get; set; } = true;
    /// <summary>Draw BSV horses 15% larger (riders keep their size). Visual only.</summary>
    public bool EnableLargeHorses { get; set; } = true;
    /// <summary>Host setting: only a horse's owner may ride it. Off: anyone may ride any horse (calling stays owner-only).</summary>
    public bool OnlyOwnersRide { get; set; } = true;
    public string[] HorseCallBlacklist { get; set; } = System.Array.Empty<string>();
}

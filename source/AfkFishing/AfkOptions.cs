using StardewModdingAPI;
namespace BetterStardewValley.AfkFishing;
public sealed class AfkOptions
{
    public bool Enabled { get; set; } = true;
    public bool ReturnToFishingSpotNextDay { get; set; } = true;
    public bool EnableChestConnection { get; set; } = true;
    public double ReturnStaminaThreshold { get; set; } = .05;
    public SButton ToggleKey { get; set; } = SButton.F8;
    public bool ShowIndicator { get; set; } = true;
}

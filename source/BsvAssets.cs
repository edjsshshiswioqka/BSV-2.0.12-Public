using StardewModdingAPI.Events;

namespace BetterStardewValley;

/// <summary>
/// Asset edit order shared by every BSV content edit. BSV applies after other mods' edits (including Content
/// Patcher packs that ask for "Late" or "Late + a little"), so another mod cannot silently overwrite the
/// entries BSV owns or the vanilla values BSV changes (prices, recipes, fish data, greenhouse and cellar maps).
/// Other mods still edit everything BSV leaves alone, and BSV's own textures can still be retextured.
/// </summary>
internal static class BsvAssets
{
    internal const AssetEditPriority EditPriority = AssetEditPriority.Late + 100;
}

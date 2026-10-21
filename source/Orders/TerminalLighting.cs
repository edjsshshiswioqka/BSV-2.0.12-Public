using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace BetterStardewValley.Orders;

// Per-screen visual lights derived from replicated anchors. Never create shared/network lights.
internal sealed class TerminalLighting
{
    internal const string Prefix = "BSV/OrderTerminal/";
    internal const float RadiusPixels = 4 * 64;
    // Native lightmap uses subtractive tint, as the vanilla glow ring does.
    internal static readonly Color WarmTint = new(0, 24, 80, 180);
    private readonly PerScreen<HashSet<string>> Owned = new(() => new());
    internal void Clear()
    {
        foreach (string id in Owned.Value) Game1.currentLightSources?.Remove(id);
        Owned.Value.Clear();
    }
    internal void Update()
    {
        var desired = new HashSet<string>();
        var location = Game1.currentLocation;
        if (Context.IsWorldReady && TerminalEntity.FarmMap(location) && Game1.isStartingToGetDarkOut(location)
            && Game1.lantern != null && Game1.currentLightSources != null)
        {
            float fade = Math.Clamp(Utility.CalculateMinutesBetweenTimes(Game1.getStartingToGetDarkTime(location), Game1.timeOfDay) / 60f, 0, 1);
            foreach (var pair in location.objects.Pairs)
            {
                var obj = pair.Value;
                if (!OrderTerminal.IsTerminal(obj) || !TerminalEntity.Complete(location, obj)) continue;
                string id = Prefix + location.NameOrUniqueName + "/" + TerminalEntity.Id(obj);
                desired.Add(id);
            Vector2 center = pair.Key * 64 + new Vector2(64, 0);
                float radius = RadiusPixels * 2 / Game1.lantern.Width;
                if (!Game1.currentLightSources.TryGetValue(id, out var light))
                    Game1.currentLightSources[id] = light = new LightSource(id, LightSource.lantern, center, radius, WarmTint * fade,
                        LightSource.LightContext.None, 0, location.NameOrUniqueName);
                else
                {
                    light.position.Value = center; light.radius.Value = radius; light.color.Value = WarmTint * fade;
                    light.lightTexture = Game1.lantern;
                }
            }
        }
        foreach (string old in Owned.Value) if (!desired.Contains(old)) Game1.currentLightSources?.Remove(old);
        Owned.Value = desired;
    }
}

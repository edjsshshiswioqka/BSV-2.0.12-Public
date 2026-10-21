using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;

namespace BetterStardewValley;

internal sealed partial class HorseFeature
{
    private static void AfterHorseUpdate(Horse __instance, GameTime time)
    {
        var mod = Instance;
        if (mod is null || !mod.Ready || !Context.IsWorldReady) return;
        var animations = mod.Local.Value.Animations;
        if (!IsRealManagedHorse(__instance)) { animations.Remove(__instance.HorseId); return; }
        var horse = __instance;
        if (horse.rider != null || horse.mounting.Value || horse.dismounting.Value)
        { animations.Remove(horse.HorseId); return; }
        if (!horse.modData.TryGetValue(RunKey, out var token))
        {
            // The replicated end marker can arrive before NetPosition finishes interpolating.
            // Keep the native run cycle through that tail instead of sliding in an idle frame.
            if (animations.TryGetValue(horse.HorseId, out var tail) && horse.position.Field.IsInterpolating()) token = tail.Token;
            else
            {
                if (animations.Remove(horse.HorseId))
                { horse.Sprite.CurrentAnimation = null; horse.Sprite.CurrentFrame = HorseRules.IdleFrame(horse.FacingDirection); }
                return;
            }
        }
        if (!animations.TryGetValue(horse.HorseId, out var old) || old.Token != token)
            old = (token, horse.Position, 0);
        bool moving = Vector2.DistanceSquared(old.Position, horse.Position) > .01f;
        double phase = old.Phase + (moving ? time.ElapsedGameTime.TotalMilliseconds : 0);
        // Native Horse uses rider-only animation selection. Reuse its exact unmodified six frames
        // without inventing a rider, replacing the sprite, or interfering with native net interpolation.
        horse.Sprite.CurrentAnimation = null;
        horse.Sprite.CurrentFrame = moving ? HorseRules.RunFrame(horse.FacingDirection, (int)(phase / 70)) : HorseRules.IdleFrame(horse.FacingDirection);
        horse.flip = horse.FacingDirection == 3;
        horse.drawOffset = horse.flip ? Vector2.Zero : new Vector2(-16, 0);
        animations[horse.HorseId] = (token, horse.Position, phase % 420);
    }

    private void DrawNames(object? sender, RenderedWorldEventArgs e)
    {
        if (!Ready || !Context.IsWorldReady || !Config.EnableHorseNameUI || Game1.eventUp || Game1.activeClickableMenu != null) return;
        var player = Game1.player;
        Vector2 cursor = Helper.Input.GetCursorPosition().AbsolutePixels;
        foreach (var horse in Game1.currentLocation.characters.OfType<Horse>().Where(IsRealManagedHorse))
        {
            var visual = VisualBounds(horse.Position);
            bool hover = new Rectangle(visual.X, visual.Y, visual.Width, visual.Height).Contains((int)cursor.X, (int)cursor.Y);
            if (!hover && Vector2.DistanceSquared(player.Position, horse.Position) > 4 * 64 * 4 * 64) continue;
            string name = string.IsNullOrWhiteSpace(horse.Name) ? T("horse.ui.unnamed") : horse.Name.Trim();
            string label = T("horse.ui.name", new { horseName = name });
            var owner = horse.getOwner();
            if (owner != null) label += "\n" + T("horse.ui.owner", new { owner = owner.Name });
            else if (horse.ownerId.Value == 0) label += "\n" + T("horse.ui.unclaimed");
            Vector2 size = Game1.smallFont.MeasureString(label);
            Vector2 at = Game1.GlobalToLocal(Game1.viewport, horse.Position + new Vector2(48, -100));
            at.X = Math.Clamp(at.X - size.X / 2, 8, Math.Max(8, Game1.viewport.Width - size.X - 8));
            at.Y = Math.Max(8, at.Y - size.Y);
            var outer = new Rectangle((int)at.X - 7, (int)at.Y - 5, (int)size.X + 14, (int)size.Y + 10);
            e.SpriteBatch.Draw(Game1.staminaRect, outer, new Color(83, 50, 29) * .85f);
            outer.Inflate(-2, -2);
            e.SpriteBatch.Draw(Game1.staminaRect, outer, new Color(245, 225, 183) * .92f);
            Utility.drawTextWithShadow(e.SpriteBatch, label, Game1.smallFont, at, Game1.textColor);
        }
    }
}

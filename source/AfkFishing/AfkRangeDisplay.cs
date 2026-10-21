using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace BetterStardewValley.AfkFishing;

internal sealed partial class AfkFishingService
{
    private void RefreshArea(bool force = false)
    {
        var s = Current; var who = Game1.player;
        if (!s.Data.Enabled || !Config.AfkFishing.EnableChestConnection)
        { s.Area=null;s.Nearby.Clear();s.Standby.Clear();ClearChest();if(s.LockPending){s.LockToken++;s.LockPending=false;}return; }
        if (!force && s.Area?.Center == AfkWorld.At(who) && Now < s.AreaAt) return;
        s.Area = AfkChestArea.Inspect(who.currentLocation, who.TilePoint, who);
        s.AreaAt = Now + 0.2;
        // During our own callback don't discard the binding merely because we hold its lock.
        if (s.LockPending)
        {
            s.Nearby.RemoveAll(c => !AfkChestArea.Accessible(c, who, ReferenceEquals(c, s.Chest)));
            s.Standby.IntersectWith(s.Nearby);
            if (s.Chest != null && !AfkChestArea.Accessible(s.Chest, who, true)) ClearChest();
            return;
        }
        s.Nearby = AfkChestArea.Candidates(who, s.Chest);
        if (s.Nearby.Count == 0) ClearChest();
        else BindChest(s.Nearby[0]);
        bool knownOutput = s.Catches.HasOutput(who.Items) || Pending(false)?.Count > 0;
        s.Standby = s.Nearby.Where(c => !ReferenceEquals(c, s.Chest)
            && (knownOutput ? CanStoreOutput(c) : ChestHasCapacityHint(c))).ToHashSet();
    }
    private string AreaText()
    {
        var s = Current;
        if(!Config.AfkFishing.EnableChestConnection)return Tr("afk.area.disabled");
        if (s.Area == null) return "";
        if (s.Chest != null) return Tr("afk.area.connected", new { backups = s.Standby.Count });
        if (s.LockPending) return Tr("afk.area.waiting-lock");
        bool busy = s.Area.Cells.Where(c => c.Ground == AfkGround.Valid)
            .Select(c => Game1.player.currentLocation.getObjectAtTile(c.Tile.X, c.Tile.Y))
            .OfType<StardewValley.Objects.Chest>().Any(c => AfkWorld.EligibleChest(c) && c.GetMutex().IsLocked());
        return Tr(busy ? "afk.area.busy" : "afk.area.no-chest");
    }
    private void DrawRange(object? sender, RenderedWorldEventArgs e)
    {
        if (!Context.IsWorldReady || !Current.Data.Enabled || Game1.eventUp || Game1.activeClickableMenu != null
            || Game1.newDay || Game1.fadeToBlack || Game1.currentLocation?.Map == null) return;
        // This belongs to AFK, not InformationHost. No Automate/UI2 gate or input hook.
        RefreshArea();
        var area = Current.Area;
        if (area == null) return;
        foreach (var cell in area.Cells)
        {
            Color color = cell.Ground == AfkGround.Valid ? new Color(50, 194, 166) : new Color(225, 78, 65);
            var chest = Current.Nearby.FirstOrDefault(c => c.TileLocation == new Vector2(cell.Tile.X, cell.Tile.Y));
            if (cell.Ground == AfkGround.Valid && chest != null) color = ReferenceEquals(chest, Current.Chest) ? Color.Gold : Current.Standby.Contains(chest) ? Color.CornflowerBlue : Color.Gray;
            var local = Game1.GlobalToLocal(Game1.viewport, new Vector2(cell.Tile.X * 64, cell.Tile.Y * 64));
            var r = new Rectangle((int)local.X + 1, (int)local.Y + 1, 62, 62);
            // Borders and tiny corner accents only: never a filled panel over actors/bobbers.
            var c = color * 0.72f;
            e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, r.Width, 2), c);
            e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), c);
            e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, 2, r.Height), c);
            e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(r.Right - 2, r.Y, 2, r.Height), c);
        }
    }
}

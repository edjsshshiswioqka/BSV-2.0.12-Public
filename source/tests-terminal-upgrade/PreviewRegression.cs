using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using BetterStardewValley.Orders;
using SObject = StardewValley.Object;

partial class Program
{
    static readonly List<Rectangle> PreviewCells = new();
    static bool RecordCell(Texture2D texture, Rectangle destinationRectangle, Color color)
    { PreviewCells.Add(destinationRectangle); return false; }
    static void PreviewRegressionChecks(Harmony harmony, SpriteBatch batch)
    {
        var rectDraw = typeof(SpriteBatch).GetMethod("Draw", new[] { typeof(Texture2D), typeof(Rectangle), typeof(Color) })!;
        harmony.Patch(rectDraw, prefix: new(typeof(Program), nameof(RecordCell)));
        Reset(); var item = Item(); Host.Items[0] = item;
        Game1.staminaRect = Texture(1, 1);
        var begin = typeof(OrderTerminal).GetMethod("BeginCursor", Any)!;
        var end = typeof(OrderTerminal).GetMethod("EndCursor", Any)!;
        object?[] state = { null }; begin.Invoke(null, state);
        try
        {
            Draws.Clear(); PreviewCells.Clear(); item.drawPlacementBounds(batch, Farm);
            Check(PreviewCells.SequenceEqual(new[] { new Rectangle(256, 256, 64, 64) }), "actual placement preview paints only the single physical cell at the same top-left anchor");
            Check(Draws.Count == 1 && Draws[0].Position == new Vector2(256, 256) && Draws[0].Scale == 2, "world preview draws once with unchanged four-tile artwork");
            ((Item)item).drawInMenu(batch, new Vector2(280, 260), 1, .5f, .999f);
            Check(Draws.Count == 1, "native five-argument cursor item draw cannot add the second overlapping terminal icon");
            Game1.activeClickableMenu = new OrdersMenu(OrdersService.Instance, "Farm", 4, 4);
            item.drawInMenu(batch, Vector2.Zero, 1, 1, 1, StackDrawType.Hide, Color.White, false);
            Check(Draws.Count == 2, "real menu/inventory icon remains visible even during cursor scope");
        }
        finally { end.Invoke(null, state); Game1.activeClickableMenu = null; }
        Draws.Clear(); item.drawInMenu(batch, Vector2.Zero, 1, 1, 1, StackDrawType.Hide, Color.White, false);
        Check(Draws.Count == 1, "cursor cleanup leaves no stale suppression on hotbar/inventory icons");
        var info = Harmony.GetPatchInfo(typeof(Game1).GetMethod(nameof(Game1.drawMouseCursor), Any)!);
        Check(info!.Finalizers.Any(p => p.PatchMethod.Name == "EndCursor"), "cursor scope cleanup is a Harmony finalizer and survives native rendering exceptions");
        Draws.Clear(); var placed = Place(); placed.draw(batch, 4, 4, 1);
        Check(Draws.Count == 1 && Math.Abs(Draws[0].Depth - (TerminalEntity.VisualBounds(new(4, 4)).Bottom - 1) / 10000f) < .00001,
            "world draw depth retains the existing artwork bottom; collision stays independently 1x1");
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace BetterStardewValley;

/// <summary>
/// One-time coat picker shown after a stable is built (or once on older saves). Every coat is previewed
/// with the player's own horse sprite; hovering a coat plays its gallop. Choosing asks for confirmation,
/// because the choice is permanent.
/// </summary>
internal sealed class HorseSkinMenu : IClickableMenu
{
    private readonly IModHelper helper;
    private readonly Func<string, string> text;
    private readonly Action<string> choose;
    private readonly List<(HorseSkins.Skin Skin, Texture2D Texture, ClickableComponent Box)> cells = new();
    private int hovered = -1, cell, columns, labelHeight;
    private const int Pad = 32, Gap = 12;

    internal HorseSkinMenu(IModHelper helper, Func<string, string> text, Action<string> choose)
    {
        this.helper = helper; this.text = text; this.choose = choose;
        foreach (var skin in HorseSkins.All)
        {
            Texture2D texture;
            try { texture = helper.GameContent.Load<Texture2D>(skin.Id == HorseSkins.Original ? "Animals/horse" : HorseSkins.AssetName(skin.Id)); }
            catch { continue; }
            cells.Add((skin, texture, new ClickableComponent(Rectangle.Empty, skin.Id)));
        }
        Layout();
    }

    private void Layout()
    {
        int vw = Game1.uiViewport.Width, vh = Game1.uiViewport.Height;
        columns = 6; labelHeight = (int)Game1.smallFont.MeasureString("Ag").Y + 4;
        int rows = (cells.Count + columns - 1) / columns;
        int header = 112;
        // Largest whole-pixel preview (3x or 2x the 32 px frame) that fits the screen.
        cell = 96;
        while (cell > 64 && (header + rows * (cell + labelHeight + Gap) + 2 * Pad > vh - 32 || columns * (cell + 40 + Gap) + 2 * Pad > vw - 32)) cell -= 32;
        int cellWidth = cell + 40;
        width = columns * (cellWidth + Gap) - Gap + 2 * Pad;
        height = header + rows * (cell + labelHeight + Gap) - Gap + 2 * Pad;
        xPositionOnScreen = (vw - width) / 2; yPositionOnScreen = Math.Max(16, (vh - height) / 2);
        for (int i = 0; i < cells.Count; i++)
        {
            int x = xPositionOnScreen + Pad + i % columns * (cellWidth + Gap), y = yPositionOnScreen + Pad + header + i / columns * (cell + labelHeight + Gap);
            cells[i].Box.bounds = new Rectangle(x, y, cellWidth, cell + labelHeight);
        }
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) { base.gameWindowSizeChanged(oldBounds, newBounds); Layout(); }

    public override void performHoverAction(int x, int y) => hovered = cells.FindIndex(c => c.Box.bounds.Contains(x, y));

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        int i = cells.FindIndex(c => c.Box.bounds.Contains(x, y));
        if (i < 0) return;
        Game1.playSound("smallSelect");
        var skin = cells[i].Skin;
        string name = text("horse.skin.name." + skin.Id);
        var picker = this;
        Game1.activeClickableMenu = new ConfirmationDialog(text("horse.skin.confirm").Replace("{{name}}", name),
            _ => { Game1.exitActiveMenu(); choose(skin.Id); },
            _ => { Game1.activeClickableMenu = picker; });
    }

    public override void draw(SpriteBatch b)
    {
        if (!Game1.options.showClearBackgrounds) b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * .6f);
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);
        string title = text("horse.skin.title"), subtitle = text("horse.skin.subtitle");
        var titleSize = Game1.dialogueFont.MeasureString(title);
        float titleScale = Math.Min(1f, (width - 2 * Pad) / titleSize.X);
        Utility.drawTextWithShadow(b, title, Game1.dialogueFont, new Vector2(xPositionOnScreen + (width - titleSize.X * titleScale) / 2, yPositionOnScreen + Pad), Game1.textColor, titleScale);
        var subSize = Game1.smallFont.MeasureString(subtitle);
        b.DrawString(Game1.smallFont, subtitle, new Vector2(xPositionOnScreen + (width - subSize.X) / 2, yPositionOnScreen + Pad + 56), new Color(163, 45, 45));
        int frame = (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 90 % 7);
        for (int i = 0; i < cells.Count; i++)
        {
            var (skin, texture, box) = cells[i];
            var r = box.bounds;
            var preview = new Rectangle(r.X + (r.Width - cell) / 2, r.Y, cell, cell);
            b.Draw(Game1.staminaRect, preview, i == hovered ? new Color(186, 214, 140) : new Color(150, 186, 110));
            var edgeColor = i == hovered ? new Color(255, 214, 90) : new Color(122, 74, 20);
            int t = i == hovered ? 4 : 2;
            b.Draw(Game1.staminaRect, new Rectangle(preview.X - t, preview.Y - t, preview.Width + 2 * t, t), edgeColor);
            b.Draw(Game1.staminaRect, new Rectangle(preview.X - t, preview.Bottom, preview.Width + 2 * t, t), edgeColor);
            b.Draw(Game1.staminaRect, new Rectangle(preview.X - t, preview.Y, t, preview.Height), edgeColor);
            b.Draw(Game1.staminaRect, new Rectangle(preview.Right, preview.Y, t, preview.Height), edgeColor);
            // Side view: idle frame, or the gallop while hovered.
            var source = new Rectangle((i == hovered ? frame : 0) * 32, 32, 32, 32);
            b.Draw(texture, preview, source, Color.White);
            string label = text("horse.skin.name." + skin.Id);
            float scale = Math.Min(1f, r.Width / Math.Max(1f, Game1.smallFont.MeasureString(label).X));
            var size = Game1.smallFont.MeasureString(label) * scale;
            b.DrawString(Game1.smallFont, label, new Vector2(r.X + (r.Width - size.X) / 2, r.Y + cell + 2), Game1.textColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }
        drawMouse(b);
    }
}

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace BetterStardewValley;

/// <summary>
/// Scrolls a native item grid that holds more rows than it shows: the player's backpack after BSV's extra upgrades
/// (48 / 60 slots in the native 3-row grid) and a Lv 2 / Lv 3 fishing machine (105 / 140 slots in its 14 x 5 grid).
/// The grid keeps its native size and components. Scrolling renames each slot to the real inventory index it shows,
/// which is what every native click, hover and add uses, and the draw sees a window of the real list.
/// Mouse wheel over the grid, the scroll bar beside it, or the right stick / shoulder scroll one whole row.
/// </summary>
internal static class InventoryScroll
{
    private sealed class State
    {
        internal int Row;            // first visible row
        internal int Total;          // slots in the real inventory
        internal bool Player;        // the farmer's backpack (total follows MaxItems)
        internal bool Dragging;
    }
    private static readonly ConditionalWeakTable<InventoryMenu, State> States = new();
    private static readonly FieldInfo ActualField = AccessTools.Field(typeof(InventoryMenu), nameof(InventoryMenu.actualInventory));
    private static IMonitor? Monitor;
    private static IModHelper? Helper;
    private static bool Logged;

    internal static void Install(IModHelper helper, IMonitor monitor)
    {
        Monitor = monitor; Helper = helper;
        var h = new Harmony(helper.ModRegistry.ModID + ".InventoryScroll");
        h.Patch(AccessTools.Method(typeof(InventoryMenu), nameof(InventoryMenu.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(int) })
                ?? throw new MissingMethodException(nameof(InventoryMenu), "draw"),
            prefix: new HarmonyMethod(typeof(InventoryScroll), nameof(BeforeDraw)), postfix: new HarmonyMethod(typeof(InventoryScroll), nameof(AfterDraw)),
            finalizer: new HarmonyMethod(typeof(InventoryScroll), nameof(DrawFinalizer)));
        // Every menu that handles the mouse wheel itself (shops, crafting pages, ...): over a scrolling grid, the grid wins.
        var seen = new HashSet<MethodBase>();
        foreach (var type in typeof(IClickableMenu).Assembly.GetTypes().Where(t => typeof(IClickableMenu).IsAssignableFrom(t)))
        {
            var method = type.GetMethod(nameof(IClickableMenu.receiveScrollWheelAction), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, new[] { typeof(int) }, null);
            if (method == null || method.IsAbstract || !seen.Add(method)) continue;
            h.Patch(method, prefix: new HarmonyMethod(typeof(InventoryScroll), nameof(BeforeWheel)));
        }
        helper.Events.Input.ButtonPressed += OnButton;
        helper.Events.Input.ButtonReleased += (_, e) => { if (e.Button == SButton.MouseLeft) foreach (var m in Grids()) if (States.TryGetValue(m, out var s)) s.Dragging = false; };
        helper.Events.Input.CursorMoved += (_, _) => Drag();
    }

    /// <summary>Lets a grid show <paramref name="total"/> slots by scrolling (used by the fishing machine).</summary>
    internal static void Register(InventoryMenu grid, int total)
    {
        var s = States.GetValue(grid, _ => new State());
        s.Total = total; s.Player = false;
        Apply(grid, s);
    }

    // ---------------------------------------------------------------- state
    private static int Columns(InventoryMenu m) => Math.Max(1, m.capacity / Math.Max(1, m.rows));
    private static State? For(InventoryMenu m)
    {
        if (States.TryGetValue(m, out var s))
        {
            if (s.Player) s.Total = Game1.player.MaxItems;
            return s.Total > m.capacity ? s : null;
        }
        // The backpack: any native grid showing the farmer's own items with fewer slots than they own.
        if (m.playerInventory && ReferenceEquals(ActualField.GetValue(m), Game1.player.Items) && Game1.player.MaxItems > m.capacity && m.capacity > 0)
        {
            s = States.GetValue(m, _ => new State { Player = true });
            s.Total = Game1.player.MaxItems;
            return s;
        }
        return null;
    }
    private static int MaxRow(InventoryMenu m, State s) => Math.Max(0, (s.Total + Columns(m) - 1) / Columns(m) - m.rows);
    private static void Apply(InventoryMenu m, State s)
    {
        s.Row = Math.Clamp(s.Row, 0, MaxRow(m, s));
        int start = s.Row * Columns(m);
        for (int i = 0; i < m.inventory.Count; i++) m.inventory[i].name = (start + i).ToString();
    }
    private static bool Scroll(InventoryMenu m, int rows)
    {
        if (For(m) is not { } s) return false;
        int before = s.Row; s.Row += rows; Apply(m, s);
        if (s.Row != before) Game1.playSound("shiny4");
        return true;
    }

    // ---------------------------------------------------------------- finding grids in the open menu
    private static IEnumerable<InventoryMenu> Grids()
    {
        var root = Game1.activeClickableMenu;
        if (root == null) yield break;
        var menus = new List<IClickableMenu> { root };
        if (root is GameMenu game && game.GetCurrentPage() is { } page) menus.Add(page);
        if (root.GetChildMenu() is { } child) menus.Add(child);
        foreach (var menu in menus)
            foreach (var field in menu.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (typeof(InventoryMenu).IsAssignableFrom(field.FieldType) && field.GetValue(menu) is InventoryMenu grid) yield return grid;
    }
    private static Rectangle Area(InventoryMenu m) => new(m.xPositionOnScreen - 16, m.yPositionOnScreen - 16, m.width + 64, m.height + 32);
    private static Rectangle Track(InventoryMenu m)
    {
        int top = m.inventory.Count > 0 ? m.inventory.Min(c => c.bounds.Top) : m.yPositionOnScreen;
        int bottom = m.inventory.Count > 0 ? m.inventory.Max(c => c.bounds.Bottom) : m.yPositionOnScreen + m.height;
        int right = m.inventory.Count > 0 ? m.inventory.Max(c => c.bounds.Right) : m.xPositionOnScreen + m.width;
        return new Rectangle(right + 12, top, 24, bottom - top);
    }
    private static InventoryMenu? Under(int x, int y) => Grids().FirstOrDefault(m => For(m) != null && (Area(m).Contains(x, y) || Track(m).Contains(x, y)));

    // ---------------------------------------------------------------- input
    private static bool BeforeWheel(int direction)
    {
        try
        {
            if (Under(Game1.getMouseX(), Game1.getMouseY()) is not { } grid) return true;
            Scroll(grid, direction > 0 ? -1 : 1);
            return false;
        }
        catch (Exception ex) { Warn(ex); return true; }
    }
    private static void OnButton(object? sender, ButtonPressedEventArgs e)
    {
        try
        {
            if (Game1.activeClickableMenu == null) return;
            if (e.Button == SButton.MouseLeft)
            {
                int x = Game1.getMouseX(), y = Game1.getMouseY();
                var grid = Grids().FirstOrDefault(m => For(m) != null && Track(m).Contains(x, y));
                if (grid == null) return;
                // The bar sits just outside the grid: the menu must not treat this click as "outside" (dropping a held item).
                Helper?.Input.Suppress(e.Button);
                States.TryGetValue(grid, out var s); s!.Dragging = true; JumpTo(grid, y);
                return;
            }
            int step = e.Button switch { SButton.RightThumbstickDown => 1, SButton.RightThumbstickUp => -1, _ => 0 };
            if (step == 0) return;
            // Controller: the grid holding the snapped slot, else the first scrolling grid.
            var snapped = Game1.activeClickableMenu.currentlySnappedComponent;
            var target = Grids().FirstOrDefault(m => For(m) != null && snapped != null && m.inventory.Contains(snapped)) ?? Grids().FirstOrDefault(m => For(m) != null);
            if (target != null) Scroll(target, step);
        }
        catch (Exception ex) { Warn(ex); }
    }
    private static void Drag()
    {
        try
        {
            foreach (var m in Grids())
                if (States.TryGetValue(m, out var s) && s.Dragging && For(m) != null) JumpTo(m, Game1.getMouseY());
        }
        catch (Exception ex) { Warn(ex); }
    }
    private static void JumpTo(InventoryMenu m, int y)
    {
        if (For(m) is not { } s) return;
        var track = Track(m); int max = MaxRow(m, s);
        int row = (int)Math.Round(Math.Clamp((y - track.Y) / (float)Math.Max(1, track.Height), 0, 1) * max);
        if (row != s.Row) { s.Row = row; Apply(m, s); Game1.playSound("shiny4"); }
    }

    // ---------------------------------------------------------------- drawing
    /// <summary>Read-only view of the real list from the first visible slot on; the native draw indexes it 0..capacity.</summary>
    private sealed class Window : IList<Item>
    {
        private readonly IList<Item> Real; private readonly int Start;
        internal Window(IList<Item> real, int start) { Real = real; Start = start; }
        public Item this[int index] { get => Start + index < Real.Count ? Real[Start + index] : null!; set => throw new NotSupportedException(); }
        public int Count => Math.Max(0, Real.Count - Start);
        public bool IsReadOnly => true;
        public void Add(Item item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(Item item) => IndexOf(item) >= 0;
        public void CopyTo(Item[] array, int arrayIndex) { for (int i = 0; i < Count; i++) array[arrayIndex + i] = this[i]; }
        public IEnumerator<Item> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int IndexOf(Item item) { for (int i = 0; i < Count; i++) if (ReferenceEquals(this[i], item)) return i; return -1; }
        public void Insert(int index, Item item) => throw new NotSupportedException();
        public bool Remove(Item item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }
    private static void BeforeDraw(InventoryMenu __instance, out IList<Item>? __state)
    {
        __state = null;
        try
        {
            if (For(__instance) is not { } s) return;
            Apply(__instance, s); // keeps names right after the menu rebuilt or the backpack grew
            if (s.Row == 0) return;
            __state = __instance.actualInventory;
            ActualField.SetValue(__instance, new Window(__state, s.Row * Columns(__instance)));
        }
        catch (Exception ex) { Warn(ex); }
    }
    private static void AfterDraw(InventoryMenu __instance, SpriteBatch __0, IList<Item>? __state)
    {
        Restore(__instance, __state);
        try
        {
            if (For(__instance) is not { } s) return;
            var track = Track(__instance); int max = MaxRow(__instance, s);
            IClickableMenu.drawTextureBox(__0, Game1.mouseCursors, new Rectangle(403, 383, 6, 6), track.X, track.Y, track.Width, track.Height, Color.White, 4f, false);
            // The native scroll thumb (6 x 10 at 4x), moving down the native runner.
            int thumbY = track.Y + (max == 0 ? 0 : (track.Height - 40) * s.Row / max);
            __0.Draw(Game1.mouseCursors, new Vector2(track.X, thumbY), new Rectangle(435, 463, 6, 10), Color.White, 0, Vector2.Zero, 4f, SpriteEffects.None, 1f);
        }
        catch (Exception ex) { Warn(ex); }
    }
    private static Exception? DrawFinalizer(InventoryMenu __instance, IList<Item>? __state, Exception? __exception)
    {
        Restore(__instance, __state);
        return __exception;
    }
    private static void Restore(InventoryMenu m, IList<Item>? real)
    {
        if (real != null && m.actualInventory is Window) ActualField.SetValue(m, real);
    }
    private static void Warn(Exception ex)
    {
        if (Logged) return; Logged = true;
        Monitor?.Log("BSV inventory scrolling hit a problem; grids keep working without it.\n" + ex, LogLevel.Warn);
    }
}

using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;

namespace Eddie.FishingMachine;

/// <summary>
/// Chests Anywhere: fishing machines and crab catchers appear in its chest list. Opening one there shows the full
/// machine window (bait, catches, status, level) instead of a plain chest, with the same rules as standing next to
/// it; closing a crab catcher remotely starts it the same way.
/// The machine keeps its inventory lock during remote use: a player announces the remote session in their farmer
/// data, and for that machine only the host keeps the lock for any online player instead of only players standing in
/// the machine's location (the game's rule for chests). Everything else (in-person use, abandoned requests) keeps the
/// game's own rule.
/// </summary>
internal sealed class ChestsAnywhereSupport
{
    internal const string IgnoredKey = "Pathoschild.ChestsAnywhere/IsIgnored";
    internal const string ShownKey = ModEntry.Tag + "ChestsAnywhereShown";
    internal const string SessionKey = ModEntry.Tag + "RemoteMachine";
    private static ModEntry Mod = null!;
    private static readonly Dictionary<NetMutex, string> MachineIds = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<string> RemoteIds = new(StringComparer.Ordinal);
    private static readonly StardewModdingAPI.Utilities.PerScreen<(Chest? Chest, int Tick)> OpenedAt = new(() => (null, 0));
    /// <summary>Whether a machine can be opened from another location (the lock patch is in place).</summary>
    internal static bool Supported { get; private set; }

    internal ChestsAnywhereSupport(ModEntry mod) => Mod = mod;

    internal void Install(IModHelper helper)
    {
        helper.Events.Display.MenuChanged += OnMenuChanged;
        helper.Events.GameLoop.UpdateTicked += (_, _) => { try { Tick(); } catch (Exception ex) { Mod.Monitor.LogOnce("BSV machine remote access: " + ex.Message, LogLevel.Warn); } };
        helper.Events.GameLoop.SaveLoaded += (_, _) => Game1.player.modData.Remove(SessionKey);   // never carried over from a saved session
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { MachineIds.Clear(); RemoteIds.Clear(); };
        try
        {
            // NetMutex.Update(GameLocation) only forwards location.farmers to Update(FarmerCollection); the larger one is patched
            // (a tiny forwarding method can be inlined into its callers, which would skip a patch on it).
            var online = AccessTools.Method(typeof(Game1), nameof(Game1.getOnlineFarmers))?.ReturnType;
            var update = typeof(NetMutex).GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(m => m.Name == nameof(NetMutex.Update)
                && m.GetParameters() is { Length: 1 } p && p[0].ParameterType != typeof(GameLocation) && online != null && p[0].ParameterType.IsAssignableFrom(online));
            if (update == null || update.GetParameters()[0].ParameterType != typeof(FarmerCollection)) throw new MissingMethodException("NetMutex.Update(FarmerCollection) not found.");
            new Harmony(ModEntry.Tag + "ChestsAnywhere").Patch(update, prefix: new HarmonyMethod(typeof(ChestsAnywhereSupport), nameof(BeforeMutexUpdate)));
            Supported = true;
        }
        catch (Exception ex)
        {
            Supported = false;
            Mod.Monitor.Log("BSV machines can only be opened from Chests Anywhere while standing next to them in this game build: " + ex.Message, LogLevel.Warn);
        }
    }

    /// <summary>Machines used to be hidden from Chests Anywhere; show each one once. A machine the player hides later stays hidden.</summary>
    internal static void Show(Chest c)
    {
        if (c.modData.ContainsKey(ShownKey)) return;
        if (c.modData.TryGetValue(IgnoredKey, out var v) && v == "true") c.modData.Remove(IgnoredKey);
        c.modData[ShownKey] = "T";
    }

    internal static void Register(Chest c)
    {
        if (c.modData.TryGetValue(ModEntry.Tag + "MachineId", out var id)) Game1.player.modData[SessionKey] = id;
    }
    /// <summary>Ends the announced remote session, unless a remote open is still waiting for its lock.</summary>
    internal static void Unregister()
    {
        if (Mod.World?.Opening != null) return;
        if (Context.IsWorldReady && Game1.player.modData.ContainsKey(SessionKey)) Game1.player.modData.Remove(SessionKey);
    }

    /// <summary>Chests Anywhere (or any other mod) opened a machine as a plain chest: open the machine window instead.</summary>
    private static void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (!Mod.Ready || !Context.IsWorldReady || e.NewMenu is not ItemGrabMenu menu || menu.context is not Chest c
            || !ModEntry.IsAnchor(c) || ReferenceEquals(c, Mod.OpenChest.Value)) return;
        if (ReferenceEquals(Game1.activeClickableMenu, menu)) Game1.exitActiveMenu();
        // Switching from another machine's window (Chests Anywhere's chest list): that machine's lock is given back first.
        if (Mod.OpenChest.Value != null) Mod.ReleaseChest();
        if (c.Location == Game1.currentLocation && Mod.World.InReach(Game1.player, c)) Mod.World.Open(c);
        else Mod.World.OpenRemote(c);
    }

    /// <summary>The machine is opened from somewhere else (another location, or out of reach).</summary>
    internal static bool Remote(Chest c) => c.Location != Game1.currentLocation || !Mod.World.InReach(Game1.player, c);

    private static object? CaMod;
    private static bool CaLookedUp, CaFailed;
    /// <summary>Chests Anywhere's chest and category lists sit above the machine window. When either would leave the
    /// screen (short window, big UI scale), both move to the left of the machine window, one under the other.</summary>
    private static void KeepListsOnScreen()
    {
        if (CaFailed || Mod.Production.FullMenu is not { } menu || !ReferenceEquals(Game1.activeClickableMenu, menu) || Mod.Production.FullGeometry is not { } g) return;
        try
        {
            if (!CaLookedUp)
            {
                CaLookedUp = true;
                var info = Mod.Helper.ModRegistry.Get("Pathoschild.ChestsAnywhere");
                CaMod = info?.GetType().GetProperty("Mod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(info);
            }
            if (CaMod == null) return;
            var perScreen = AccessTools.Field(CaMod.GetType(), "CurrentOverlay")?.GetValue(CaMod);
            var overlay = perScreen?.GetType().GetProperty("Value")?.GetValue(perScreen);
            if (overlay == null || !ReferenceEquals(AccessTools.Field(overlay.GetType(), "Menu")?.GetValue(overlay), menu)) return;
            var chest = AccessTools.Field(overlay.GetType(), "ChestDropdown")?.GetValue(overlay) as ClickableComponent;
            var category = AccessTools.Field(overlay.GetType(), "CategoryDropdown")?.GetValue(overlay) as ClickableComponent;
            var edit = AccessTools.Field(overlay.GetType(), "EditButton")?.GetValue(overlay) as ClickableComponent;
            if (chest == null) return;
            var screen = new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);
            bool Off(ClickableComponent? c) => c != null && (c.bounds.Top < 0 || c.bounds.Left < 0 || c.bounds.Right > screen.Right || c.bounds.Bottom > screen.Bottom);
            if (!Off(chest) && !Off(category) && !Off(edit)) return;
            // Left of the window: category list on top, chest list (with its edit button) under it. Both lists open
            // downwards, so the chest list - the one used most - never opens under the other box.
            int editWidth = edit?.bounds.Width ?? 0, gap = 8;
            int width = Math.Max(chest.bounds.Width + editWidth, category?.bounds.Width ?? 0);
            int x = Math.Max(4, g.Window.X - 12 - width), y = Math.Max(4, g.Window.Y);
            void Move(ClickableComponent c, int nx, int ny)
            {
                c.bounds = new Rectangle(nx, ny, c.bounds.Width, c.bounds.Height);
                c.GetType().GetMethod("ReinitializeComponents", BindingFlags.Instance | BindingFlags.Public)?.Invoke(c, null);
            }
            int chestY = y;
            if (category != null) { Move(category, x, y); chestY = y + category.bounds.Height + gap; }
            Move(chest, x, chestY);
            if (edit != null) edit.bounds = new Rectangle(x + chest.bounds.Width, chestY, edit.bounds.Width, edit.bounds.Height);
        }
        catch (Exception ex) { CaFailed = true; Mod.Monitor.Log("BSV could not move Chests Anywhere's lists next to the machine window: " + ex.Message, LogLevel.Trace); }
    }

    private static void Tick()
    {
        KeepListsOnScreen();
        if (!Context.IsWorldReady || !Supported) return;
        if (Context.IsMainPlayer)
        {
            MachineIds.Clear(); RemoteIds.Clear();
            foreach (var (chest, _) in Mod.Machines.All)
                if (chest.modData.TryGetValue(ModEntry.Tag + "MachineId", out var id)) MachineIds[chest.GetMutex()] = id;
            foreach (Farmer f in Game1.getOnlineFarmers())
                if (f.modData.TryGetValue(SessionKey, out var id)) RemoteIds.Add(id);
            // Lock requests for a machine nobody is standing next to are processed here.
            foreach (var (chest, location) in Mod.Machines.All)
                if (location != Game1.currentLocation) chest.GetMutex().Update(location);
        }
        Mod.World.TickRemote();
        // A remote opener (host or farmhand) sees its own lock being granted or refused.
        foreach (var c in new[] { Mod.World.Opening, Mod.OpenChest.Value })
            if (c?.Location != null && c.Location != Game1.currentLocation) c.GetMutex().Update(c.Location);
        // The lock was lost during a remote session (host left, disconnect...): close the window instead of editing without it.
        var open = Mod.OpenChest.Value;
        if (open != null && Game1.player.modData.ContainsKey(SessionKey) && Game1.activeClickableMenu is ItemGrabMenu m && ReferenceEquals(m.context, open))
        {
            if (OpenedAt.Value.Chest != open) OpenedAt.Value = (open, Game1.ticks);
            else if (Game1.ticks - OpenedAt.Value.Tick > 30 && !open.GetMutex().IsLockHeld()) { Game1.exitActiveMenu(); Mod.Message("access.busy"); }
        }
    }

    /// <summary>For a machine in an announced remote session, its lock follows every online player.</summary>
    /// The farmer list is replaced through a by-ref argument (a change to Harmony's __args array is not passed on).
    private static void BeforeMutexUpdate(NetMutex __instance, ref FarmerCollection __0)
    {
        if (!Context.IsMainPlayer || !MachineIds.TryGetValue(__instance, out var id)) return;
        // Read the announcements live: a farmhand's announcement and lock request can arrive in the same frame.
        if (!RemoteIds.Contains(id) && !Game1.getOnlineFarmers().Any(f => f.modData.TryGetValue(SessionKey, out var v) && v == id)) return;
        __0 = Game1.getOnlineFarmers();
    }
}

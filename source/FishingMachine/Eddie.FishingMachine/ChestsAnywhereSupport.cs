using System.Reflection;
using HarmonyLib;
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
/// Remote access keeps the machine's inventory lock: the host grants and keeps that lock for any online player,
/// not only for players standing in the machine's location (the game's own rule for chests).
/// </summary>
internal sealed class ChestsAnywhereSupport
{
    internal const string IgnoredKey = "Pathoschild.ChestsAnywhere/IsIgnored";
    internal const string ShownKey = ModEntry.Tag + "ChestsAnywhereShown";
    private static ModEntry Mod = null!;
    private static MethodInfo? UpdateFarmers;
    private static readonly HashSet<NetMutex> Mutexes = new(ReferenceEqualityComparer.Instance);
    /// <summary>Whether a machine can be opened from another location (the lock patch is in place).</summary>
    internal static bool Supported { get; private set; }

    internal ChestsAnywhereSupport(ModEntry mod) => Mod = mod;

    internal void Install(IModHelper helper)
    {
        helper.Events.Display.MenuChanged += OnMenuChanged;
        helper.Events.GameLoop.UpdateTicked += (_, _) => { try { Tick(); } catch (Exception ex) { Mod.Monitor.LogOnce("BSV machine remote access: " + ex.Message, LogLevel.Warn); } };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => Mutexes.Clear();
        try
        {
            var online = AccessTools.Method(typeof(Game1), nameof(Game1.getOnlineFarmers));
            var update = AccessTools.Method(typeof(NetMutex), nameof(NetMutex.Update), new[] { typeof(GameLocation) });
            UpdateFarmers = online == null ? null : typeof(NetMutex).GetMethods().FirstOrDefault(m => m.Name == nameof(NetMutex.Update)
                && m.GetParameters() is { Length: 1 } p && p[0].ParameterType != typeof(GameLocation) && p[0].ParameterType.IsAssignableFrom(online.ReturnType));
            if (update == null || UpdateFarmers == null) throw new MissingMethodException("NetMutex.Update(FarmerCollection) not found.");
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

    /// <summary>Chests Anywhere (or any other mod) opened a machine as a plain chest: open the machine window instead.</summary>
    private static void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (!Mod.Ready || !Context.IsWorldReady || e.NewMenu is not ItemGrabMenu menu || menu.context is not Chest c
            || !ModEntry.IsAnchor(c) || ReferenceEquals(c, Mod.OpenChest.Value)) return;
        if (ReferenceEquals(Game1.activeClickableMenu, menu)) Game1.exitActiveMenu();
        if (c.Location == Game1.currentLocation && Mod.World.InReach(Game1.player, c)) Mod.World.Open(c);
        else Mod.World.OpenRemote(c);
    }

    /// <summary>The machine is opened from somewhere else (another location, or out of reach).</summary>
    internal static bool Remote(Chest c) => c.Location != Game1.currentLocation || !Mod.World.InReach(Game1.player, c);

    private static void Tick()
    {
        if (!Context.IsWorldReady || !Supported) return;
        // The host processes lock requests for machines nobody is standing next to.
        if (Context.IsMainPlayer)
        {
            Mutexes.Clear();
            foreach (var (chest, _) in Mod.Machines.All) Mutexes.Add(chest.GetMutex());
            foreach (var (chest, location) in Mod.Machines.All)
                if (location != Game1.currentLocation) chest.GetMutex().Update(location);
        }
        // A remote opener (host or farmhand) sees its own lock being granted.
        foreach (var c in new[] { Mod.World.Opening, Mod.OpenChest.Value })
            if (c?.Location != null && c.Location != Game1.currentLocation) c.GetMutex().Update(c.Location);
    }

    /// <summary>Machine locks follow every online player, not only the ones in the machine's location.</summary>
    private static bool BeforeMutexUpdate(NetMutex __instance)
    {
        if (!Context.IsMainPlayer || UpdateFarmers == null || !Mutexes.Contains(__instance)) return true;
        try { UpdateFarmers.Invoke(__instance, new object[] { Game1.getOnlineFarmers() }); return false; }
        catch { return true; }
    }
}

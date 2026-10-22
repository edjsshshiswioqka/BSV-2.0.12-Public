using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;

namespace BetterStardewValley;

internal sealed class CellarFeature
{
    private readonly IMonitor monitor;
    private readonly string backupRoot;
    private readonly HashSet<string> errors = new();
    private readonly HashSet<string> retainedWarnings = new();
    private readonly Func<bool> enabled;
    private readonly IModHelper helper;
    private string? retainedStamp;

    internal CellarFeature(IModHelper helper, IMonitor monitor, Func<bool>? enabled = null, Func<bool>? appearance = null)
    {
        this.monitor = monitor;
        this.helper = helper;
        this.enabled = enabled ?? (() => true);
        backupRoot = Path.Combine(helper.DirectoryPath, "backups", "cellar-0.9");
        helper.Events.Content.AssetRequested += (_, e) =>
        {
            if ((!this.enabled() && retainedStamp == null) || !e.NameWithoutLocale.IsEquivalentTo("Maps/Cellar")) return;
            e.Edit(a =>
            {
                try
                {
                    CellarMap.Expand(a.AsMap().Data, this.enabled() ? null : retainedStamp);
                    if(appearance?.Invoke()==true)
                    {
                        try
                        {
                            if(!CellarAppearance.Apply(a.AsMap().Data))
                                monitor.Log("BSV cellar visual polish skipped: unfamiliar native wall band; map retained.",LogLevel.Debug);
                        }
                        catch(Exception ex){Error("appearance","BSV cellar appearance edit skipped; expanded geometry and placed contents were not changed by the visual pass.",ex);}
                    }
                }
                catch (MapConflictException ex) when (this.enabled())
                {
                    // Another mod's cellar: left exactly as that mod made it. Expected with map replacement mods.
                    if (errors.Add("foreign")) monitor.Log(BsvI18n.Get(helper,"diag.cellar-foreign")+" ("+ex.Message+")", LogLevel.Warn);
                }
                catch (Exception ex) { Error("map", "BSV cellar expansion skipped; the original map was retained.", ex); }
            }, AssetEditPriority.Late);
        };
        helper.Events.GameLoop.SaveLoaded += (_, _) => CheckCellars();
        helper.Events.GameLoop.DayStarted += (_, _) => CheckCellars();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            errors.Clear(); retainedWarnings.Clear(); retainedStamp = null;
            helper.GameContent.InvalidateCache("Maps/Cellar");
        };
        helper.Events.Player.Warped += (_, e) => { if (e.IsLocalPlayer && e.NewLocation is Cellar) CheckCellars(); };
        helper.ConsoleCommands.Add("bsv_cellar_status", BsvI18n.Get(helper,"cmd.cellar-status"), (_, _) =>
        {
            if (!Context.IsWorldReady) { monitor.Log(BsvI18n.Get(helper,"diag.load-save"), LogLevel.Info); return; }
            Utility.ForEachLocation(location =>
            {
                if (location is Cellar)
                {
                    string stamp = location.Map.Properties.TryGetValue(CellarMap.LayoutKey, out var p) ? p.ToString() : "not expanded";
                    monitor.Log($"{location.NameOrUniqueName}: layout={stamp}; floor tiles={CellarMap.FloorTiles(location.Map).Count}; placed objects={location.objects.Count()}; exits={string.Join(';', location.warps.Select(w => $"{w.X},{w.Y}->{w.TargetName}:{w.TargetX},{w.TargetY}"))}", LogLevel.Info);
                }
                return true;
            });
        });
    }

    private void CheckCellars()
    {
        if (!Context.IsWorldReady) return;
        bool backedUp = false;
        try
        {
            Utility.ForEachLocation(location =>
            {
                if (location is not Cellar) return true;
                if (!enabled() && location.modData.TryGetValue(CellarMap.LayoutKey, out var retained))
                {
                    // Exact layout validation precedes the atomic map commit; casks,
                    // held items, timers and furniture are never moved or reconstructed.
                    retainedStamp = retained;
                    if (!location.Map.Properties.TryGetValue(CellarMap.LayoutKey, out var current) || current.ToString() != retained)
                    {
                        if (errors.Contains(location.NameOrUniqueName)) return true;
                        helper.GameContent.InvalidateCache("Maps/Cellar");
                        location.loadMap(location.mapPath.Value, force_reload: true);
                        location.updateWarps();
                    }
                    if (!location.Map.Properties.TryGetValue(CellarMap.LayoutKey, out current) || current.ToString() != retained)
                    {
                        Error(location.NameOrUniqueName, "Recorded BSV cellar could not be restored on this map. Original items retained; restore the previous map setup before saving.", null);
                        return true;
                    }
                    if (retainedWarnings.Add(location.NameOrUniqueName))
                        monitor.Log(BsvI18n.Get(helper,"diag.map-retained")+"\nCellar: "+location.NameOrUniqueName, LogLevel.Warn);
                    return true;
                }
                if (!enabled()) return true;
                if (!Context.IsMainPlayer) return true;
                if (!location.Map.Properties.TryGetValue(CellarMap.LayoutKey, out var layout))
                {
                    if (location.modData.ContainsKey(CellarMap.LayoutKey))
                        Error(location.NameOrUniqueName, "A saved BSV expanded cellar is using a different map. Restore the BSV cellar map before saving; no items have been deleted.", null);
                    return true;
                }
                string stamp = layout.ToString();
                if (location.modData.TryGetValue(CellarMap.LayoutKey, out var saved))
                {
                    if (saved != stamp) Error(location.NameOrUniqueName, "The cellar base map changed after expansion. Check other map mods before saving; no items have been moved.", null);
                    return true;
                }
                // One save backup per check covers every cellar (farmhouse and each cabin); 2.0.12 copied the
                // whole save once per cellar.
                if (!backedUp)
                {
                    backedUp = true;
                    string? backup = GreenhouseBackup.Create(Constants.CurrentSavePath, backupRoot);
                    if (backup != null) monitor.Log("BSV saved a pre-cellar-expansion backup to: " + backup, LogLevel.Debug);
                }
                // Native floors and entrance coordinates stay in place. Never recreate objects,
                // furniture or casks: existing held items, quality and timers remain untouched.
                location.modData[CellarMap.LayoutKey] = stamp;
                return true;
            });
        }
        catch (Exception ex) { Error("save", "BSV cellar compatibility check failed.", ex); }
    }

    private void Error(string key, string message, Exception? ex)
    {
        if (errors.Add(key)) monitor.Log(BsvI18n.Get(helper,"diag.map-error")+"\n"+message + (ex == null ? "" : "\n" + ex), LogLevel.Error);
    }
}

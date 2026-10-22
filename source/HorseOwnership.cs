using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Menus;

namespace BetterStardewValley;

internal sealed partial class HorseFeature
{
    private const string TractorMarker = "Pathoschild.TractorMod";
    // Official Tractor Mod Data/Buildings ID (ModEntry.GarageBuildingId/IsGarage).
    // Covers construction and the interval before Tractor Mod attaches its horse marker.
    private const string TractorGarageId = "Pathoschild.TractorMod_Stable";
    // Tractor Mod reuses the Horse class. Recognize every known marker (current modData key and any key of
    // that mod, the older "tractor/" name prefix, and garage building types), so BSV never calls, names,
    // claims or speeds up a tractor.
    private static bool IsTractorHorse(Horse? horse) => horse != null && (horse.modData.ContainsKey(TractorMarker)
        || horse.modData.Keys.Any(k => k.StartsWith(TractorMarker, StringComparison.OrdinalIgnoreCase))
        || horse.Name?.StartsWith("tractor/", StringComparison.OrdinalIgnoreCase) == true);
    private static bool IsRealManagedHorse(Horse? horse) => horse != null && !IsTractorHorse(horse);
    internal static bool IsTractor(Horse horse) => IsTractorHorse(horse);
    private static bool IsTractorStable(Stable stable) => stable.buildingType.Value == TractorGarageId
        || stable.buildingType.Value?.Contains("Tractor", StringComparison.OrdinalIgnoreCase) == true
        || stable.modData.Keys.Any(k => k.StartsWith(TractorMarker, StringComparison.OrdinalIgnoreCase))
        || IsTractorHorse(stable.getStableHorse());

    private static List<Stable> Stables()
    {
        var result = new List<Stable>();
        Utility.ForEachLocation(location => { result.AddRange(location.buildings.OfType<Stable>().Where(s => !IsTractorStable(s))); return true; });
        return result;
    }
    private static Stable? StableFor(Guid id) => Stables().FirstOrDefault(s => s.HorseId == id);
    private static List<Horse> OwnedHorses(long actor) => Stables()
        .Where(s => s.owner.Value == actor && s.daysOfConstructionLeft.Value <= 0)
        .Select(s => s.getStableHorse()).OfType<Horse>().Where(h => IsRealManagedHorse(h) && h.ownerId.Value == actor).Distinct().ToList();

    private static void Recover(Stable stable)
    {
        // Run before native grabHorse/updateHorseOwnership can overwrite pre-BSV ownership.
        // A nonzero ID remains an owner even if that farmer is currently offline/unavailable.
        if (IsTractorStable(stable)) return;
        var horse = stable.getStableHorse();
        if (!IsRealManagedHorse(horse)) return;
        long owner = HorseRules.RecoverOwner(horse?.ownerId.Value ?? 0, stable.owner.Value);
        stable.owner.Value = owner;
        if (horse != null) horse.ownerId.Value = owner;
        if (!stable.modData.ContainsKey(NameKey))
        {
            string name = horse?.Name ?? "";
            if (string.IsNullOrWhiteSpace(name) && owner != 0) name = Game1.GetPlayer(owner)?.horseName.Value ?? "";
            if (!string.IsNullOrWhiteSpace(name)) stable.modData[NameKey] = name;
        }
    }
    private static void ApplyName(Stable stable)
    {
        if (IsTractorStable(stable)) return;
        var horse = stable.getStableHorse();
        if (!IsRealManagedHorse(horse)) return;
        horse.ownerId.Value = stable.owner.Value;
        // The coat lives on the stable (permanent) and is mirrored to its horse, which syncs it to every player.
        if (stable.modData.TryGetValue(HorseSkins.SkinKey, out var coat)) horse!.modData[HorseSkins.SkinKey] = coat;
        if (stable.modData.TryGetValue(NameKey, out var name) && name.Length > 0)
        {
            name = NativeName(name);
            stable.modData[NameKey] = name;
            horse.Name = name; horse.displayName = name.Trim();
        }
    }
    private static string NativeName(string name)
    {
        // Match vanilla Horse.nameHorse: don't shadow a villager's internal lookup name.
        Utility.ForEachVillager(npc => { if (npc.Name == name) name += " "; return true; });
        return name;
    }
    private void RecoverAll()
    {
        ClearHost();
        foreach (var stable in Stables())
        {
            Recover(stable); ApplyName(stable);
            stable.getStableHorse()?.modData.Remove(RunKey); // never resume a serialized in-flight controller
        }
        foreach (var farmer in Game1.getAllFarmers()) ValidateSelection(farmer);
    }
    private static void ValidateSelection(Farmer farmer)
    {
        if (!Game1.IsMasterGame || !farmer.modData.TryGetValue(SelectedKey, out var raw)) return;
        var stable = Guid.TryParse(raw, out var id) ? StableFor(id) : null;
        // Keep an existing real stable's choice while its horse is temporarily absent
        // during load/native respawn; removal of the stable invalidates the preference.
        var horse = stable?.getStableHorse();
        if (stable == null || stable.owner.Value != farmer.UniqueMultiplayerID
            || (horse != null && (!IsRealManagedHorse(horse) || horse.ownerId.Value != farmer.UniqueMultiplayerID)))
            farmer.modData.Remove(SelectedKey);
    }
    private static bool BeforeOwnership(Stable __instance)
    {
        if (IsTractorStable(__instance)) return true;
        if (Instance is null) return true;
        if (!Game1.IsMasterGame) return false;
        Recover(__instance);
        return true;
    }
    private static bool BeforeGrab(Stable __instance)
    {
        if (IsTractorStable(__instance)) return true;
        if (!Game1.IsMasterGame) return false;
        if (Instance?.Active.TryGetValue(__instance.HorseId, out var s) == true) Instance.Finish(s, "");
        Recover(__instance);
        return true; // native lifecycle alone may create a new horse for a newly completed stable
    }
    private static void AfterOwnership(Stable __instance) { if (Game1.IsMasterGame) ApplyName(__instance); }
    private static void BeforeDemolition(Stable __instance)
    {
        if (IsTractorStable(__instance) || !Game1.IsMasterGame || Instance is null) return;
        Instance.CleanupStable(__instance);
        var horse = __instance.getStableHorse();
        if (horse?.rider != null) horse.dismount(from_demolish: true);
        if (horse != null) horse.modData.Remove(RunKey);
        // Stable.performActionOnDemolition removes the actual horse; no replacement is spawned.
    }
    private void CleanupStable(Stable stable)
    {
        if (IsTractorStable(stable)) return;
        if (Active.TryGetValue(stable.HorseId, out var state)) Finish(state, "");
        foreach (var farmer in Game1.getAllFarmers())
            if (farmer.modData.TryGetValue(SelectedKey, out var raw) && raw == stable.HorseId.ToString("N")) farmer.modData.Remove(SelectedKey);
    }

    private static bool BeforeAction(Horse __instance, Farmer who, ref bool __result)
    {
        if (!IsRealManagedHorse(__instance)) return true;
        var mod = Instance;
        if (mod is null || !mod.Ready) return true;
        __result = true;
        var stable = StableFor(__instance.HorseId);
        if (stable == null) { if (who.IsLocalPlayer) mod.Message("horse.call.no_horse"); return false; }
        if (HorseRules.CanClaim(stable.owner.Value, __instance.ownerId.Value))
        {
            if (who.IsLocalPlayer && who.CanMove) mod.OpenNaming(__instance, true);
            return false;
        }
        bool owns = HorseRules.Owns(who.UniqueMultiplayerID, stable.owner.Value, __instance.ownerId.Value);
        // Host setting (GMCM): by default only the owner may ride; when off, anyone may ride (never call) it.
        if (!owns && Configuration.SharedPreferences.Effective(mod.Config).OnlyOwnersRide)
        {
            if (who.IsLocalPlayer)
            {
                var owner = Game1.GetPlayer(HorseRules.RecoverOwner(__instance.ownerId.Value, stable.owner.Value));
                mod.Message(string.IsNullOrWhiteSpace(__instance.Name) ? "horse.interaction.not_owner" : "horse.interaction.not_owner_named",
                    new { owner = owner?.Name ?? mod.T("horse.ui.unknown_owner"), horseName = __instance.Name });
            }
            return false;
        }
        // A horse running to or away from its caller can be mounted on the spot: the run simply ends
        // (on a farmhand, the host ends it as soon as it sees the horse being mounted).
        if (Context.IsMainPlayer && mod.Active.TryGetValue(__instance.HorseId, out var running)) mod.Finish(running, "");
        // Avoid vanilla's unnamed-horse branch, which can assign ownership and clear another stable.
        if (string.IsNullOrWhiteSpace(__instance.Name))
        { if (owns && who.IsLocalPlayer && who.CanMove) mod.OpenNaming(__instance, false); return false; } // never let vanilla name/claim someone else's horse
        return true; // native mutex, mounting, dismounting, hats, carrots and rider rendering
    }

    private const string BuilderKey = Tag + "StableBuilder";
    /// <summary>Records who placed a stable at Robin's (host writes it; a farmhand asks the host).</summary>
    private void MarkBuilder(Stable stable)
    {
        if (Context.IsMainPlayer) { if (!stable.modData.ContainsKey(BuilderKey)) stable.modData[BuilderKey] = Game1.player.UniqueMultiplayerID.ToString(); }
        else SendStableAction(stable, "Builder", "");
    }
    /// <summary>Whether the local player is the one who picks this stable's coat (once, permanently).</summary>
    private static bool PicksCoat(Stable stable, long me)
    {
        if (stable.modData.ContainsKey(HorseSkins.SkinKey) || IsTractorStable(stable) || stable.daysOfConstructionLeft.Value > 0) return false;
        if (stable.modData.TryGetValue(BuilderKey, out var builder)) return builder == me.ToString();
        // Older stables: their owner; an unclaimed horse in single player belongs to the only player.
        return stable.owner.Value == me || stable.owner.Value == 0 && (!Game1.IsMultiplayer || Context.IsMainPlayer);
    }
    private void OfferCoat()
    {
        var local = Local.Value; double now = Now;
        if (Game1.activeClickableMenu is CarpenterMenu) local.CarpenterSeen = now;
        if (now < local.NextCoatCheck || !Context.IsPlayerFree || Game1.activeClickableMenu != null || Game1.eventUp) return;
        local.NextCoatCheck = now + 1;
        foreach (var stable in Stables())
        {
            if (local.CoatOffered.Contains(stable.HorseId) || !PicksCoat(stable, Game1.player.UniqueMultiplayerID)) continue;
            if (!IsRealManagedHorse(stable.getStableHorse())) continue;
            local.CoatOffered.Add(stable.HorseId); // closed without choosing: offered again tomorrow
            Game1.activeClickableMenu = new HorseSkinMenu(Helper, key => T(key), id => SendStableAction(stable, "Skin", id));
            return;
        }
    }
    private void SendStableAction(Stable stable, string action, string value)
    {
        if (!Context.IsWorldReady) return;
        var r = new HorseRequest { Id = Guid.NewGuid().ToString("N"), Actor = Game1.player.UniqueMultiplayerID, Horse = stable.HorseId.ToString("N"),
            Location = Game1.currentLocation.NameOrUniqueName, Action = action, Name = value, View = CurrentView() };
        Local.Value.Pending = r.Id; Local.Value.PendingSince = Now;
        if (Context.IsMainPlayer || Context.IsSplitScreen) Incoming[r.Actor] = r;
        else Helper.Multiplayer.SendMessage(r, RequestType, new[] { ModId }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }
    /// <summary>Host: builder record and the permanent coat choice.</summary>
    private void HandleStable(HorseRequest r, Guid id)
    {
        var stable = Stables().FirstOrDefault(s => s.HorseId == id);
        if (stable == null) { Reply(r, "horse.call.no_horse"); return; }
        if (r.Action == "Builder")
        {
            if (!stable.modData.ContainsKey(BuilderKey)) stable.modData[BuilderKey] = r.Actor.ToString(); // first placement report wins
            Reply(r); return;
        }
        if (stable.modData.ContainsKey(HorseSkins.SkinKey)) { Reply(r, "horse.skin.locked"); return; }
        if (HorseSkins.Find(r.Name) is not { } skin || !PicksCoat(stable, r.Actor)) { Reply(r, "horse.skin.unavailable"); return; }
        stable.modData[HorseSkins.SkinKey] = skin.Id;
        if (stable.getStableHorse() is { } horse && IsRealManagedHorse(horse)) horse.modData[HorseSkins.SkinKey] = skin.Id;
        Reply(r, "horse.skin.saved");
    }

    private void OpenNaming(Horse horse, bool claim)
    {
        if (!IsRealManagedHorse(horse)) return;
        Game1.activeClickableMenu = new NamingMenu(value =>
        {
            string name = HorseRules.CleanName(value);
            if (name.Length == 0) return;
            Game1.exitActiveMenu();
            SendAction(horse, claim ? "Claim" : "Name", name);
        }, T(claim ? "horse.naming.claim_prompt" : "horse.naming.prompt"), horse.Name ?? "");
    }

    private Horse? PickOwnedHorse(bool offerChoice, bool forceChoice = false)
    {
        ValidateSelection(Game1.player);
        var owned = OwnedHorses(Game1.player.UniqueMultiplayerID);
        if (owned.Count == 0) { Message("horse.call.no_horse"); return null; }
        if (!forceChoice && Game1.player.modData.TryGetValue(SelectedKey, out var selected))
        {
            var match = owned.FirstOrDefault(h => Guid.TryParse(selected, out var selectedId) && h.HorseId == selectedId);
            if (match != null) return match;
        }
        if (owned.Count == 1) return owned[0];
        if (!offerChoice) { Message("horse.call.choose_first"); return null; }
        // Multiple stables belonging to one player are legal. Explicit choice, never "first horse".
        var choices = owned.Select((h, i) => new Response(h.HorseId.ToString("N"),
            string.IsNullOrWhiteSpace(h.Name) ? T("horse.ui.unnamed") + " " + (i + 1) : h.Name)).ToList();
        choices.Add(new Response("cancel", T("horse.ui.cancel")));
        Game1.currentLocation.createQuestionDialogue(T("horse.call.choose"), choices.ToArray(), (_, answer) =>
        {
            var chosen = owned.FirstOrDefault(h => h.HorseId.ToString("N") == answer);
            if (IsRealManagedHorse(chosen)) SendAction(chosen!, forceChoice ? "Select" : "Call");
        });
        return null;
    }
}

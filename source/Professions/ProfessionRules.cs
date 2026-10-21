using StardewValley;

[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("ProfessionsNativeChecks")]

namespace BetterStardewValley.Professions;

/// <summary>Explicit vanilla mapping, verified against 1.6.15 constants and menu IDs.
/// Native constant names butcher/baitmaster/mariner/burrower are historical names.</summary>
internal static class ProfessionRules
{
    internal sealed record Skill(int Id, string Name, int[] Basic, int[] Advanced)
    { internal IEnumerable<int> All => Basic.Concat(Advanced); }
    internal static readonly Skill[] Skills = {
        new(Farmer.farmingSkill, "Farming", new[]{0,1}, new[]{2,3,4,5}),
        new(Farmer.miningSkill, "Mining", new[]{18,19}, new[]{20,21,22,23}),
        new(Farmer.foragingSkill, "Foraging", new[]{12,13}, new[]{14,15,16,17}),
        new(Farmer.fishingSkill, "Fishing", new[]{6,7}, new[]{8,9,10,11}),
        new(Farmer.combatSkill, "Combat", new[]{24,25}, new[]{26,27,28,29})
    };
    internal static Skill? Find(int id) => Skills.FirstOrDefault(s=>s.Id==id);
    internal static bool Stage(int skill,int level) => Find(skill)!=null && level is 5 or 10;
    internal static IEnumerable<int> Expected(int skill,int level) => Find(skill) is not { } s || level<5
        ? Array.Empty<int>() : level<10 ? s.Basic : s.All;
    internal static int HealthPerk(int id) => id switch {24=>15,27=>25,_=>0};
    internal static bool Pending(Farmer who,int skill) => who.newLevels.Any(p=>p.X==skill && p.Y is >=1 and <=10);
}

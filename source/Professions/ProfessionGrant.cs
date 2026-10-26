using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace BetterStardewValley.Professions;

/// <summary>Only the owning local player's game thread writes professions/health.
/// Native NetIntHashSet is the idempotency record; no parallel host grants, snapshots or XP edits.</summary>
internal static class ProfessionGrant
{
    internal static string SettledKey(int skill,int level)=>$"Eddie.BetterStardewValley/AllProfessions/Settled/{skill}/{level}";
    internal static bool Owns(Farmer? who) => who!=null && ReferenceEquals(who,Game1.player) && who.IsLocalPlayer;
    internal static int Missing(Farmer who,int skill,int level)
    {
        var ids=ProfessionRules.Expected(skill,level).Where(id=>!who.professions.Contains(id)).ToArray();
        // Preflight the only arithmetic which could fail before mutating the set.
        int health=checked(who.maxHealth+ids.Sum(ProfessionRules.HealthPerk));
        foreach(int id in ids)who.professions.Add(id);
        who.maxHealth=health;
        return ids.Length;
    }
    internal static int Migrate(Farmer who)
    {
        if(!Owns(who))return 0;
        int added=0,health=who.health;float energy=who.Stamina;
        // Native validation is a lower-bound repair, never a fixed overwrite. It accounts for
        // queued combat levels and qiCave. Preserve current HP: migration is not a healing action.
        try { LevelUpMenu.RevalidateHealth(who); }
        finally { who.health=health;who.Stamina=energy; }
        foreach(var skill in ProfessionRules.Skills)
            if(ProfessionService.SkillActive(skill.Name)&&!ProfessionRules.Pending(who,skill.Id))added+=Missing(who,skill.Id,who.GetUnmodifiedSkillLevel(skill.Id));
        added+=MigrateCustom(who);
        who.health=health;who.Stamina=energy;
        return added;
    }
    /// <summary>SpaceCore skills (e.g. YACS cooking): every profession of each reached stage, like SpaceCore's own All Professions support.
    /// No perk calls and no health changes, the same as SpaceCore.</summary>
    internal static int MigrateCustom(Farmer who)
    {
        if(!Owns(who))return 0;
        int added=0;
        foreach(var row in SkillRows.Custom)
        {
            if(!ProfessionService.SkillActive(row.Key))continue;
            foreach(int id in row.Expected(row.Level(who)))
                if(!who.professions.Contains(id)){who.professions.Add(id);added++;}
        }
        return added;
    }
    internal static bool CanConfirm(Farmer who,int skill,int level) => Owns(who)
        && ProfessionRules.Stage(skill,level) && who.GetUnmodifiedSkillLevel(skill)>=level
        && who.newLevels.Contains(new Point(skill,level));
    internal static int Confirm(Farmer who,int skill,int level)
    {
        if(!CanConfirm(who,skill,level))return -1;
        string key=SettledKey(skill,level);bool settled=who.modData.ContainsKey(key);
        int added=Missing(who,skill,level);
        who.modData[key]="1";
        // Same recovery as native getImmediateProfessionPerk, ONCE per pending stage.
        // Do not call getLevelPerk: it would give an extra +5 HP at combat 5 and 10.
        if(!settled){who.health=who.maxHealth;who.Stamina=who.MaxStamina;}
        return added;
    }
}

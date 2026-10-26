using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace BetterStardewValley.Professions;

internal sealed class ProfessionService
{
    internal const string AuditedMvid="aa1f513a-94f0-4ef6-a35f-2d5b4a3721bf";
    internal static bool Enabled {get;private set;}
    internal static bool Active=>Enabled && (!Configuration.SharedPreferences.HasLocal||Configuration.SharedPreferences.Current.EnableAllProfessions);
    /// <summary>All Professions is on and this skill ("Farming"... or a SpaceCore skill id) is not switched back to the native choice.</summary>
    internal static bool SkillActive(string key)=>Active&&!(Configuration.SharedPreferences.Current.AllProfessionsExcludedSkills??Array.Empty<string>()).Contains(key);
    internal static bool SkillActive(int vanillaSkill)=>ProfessionRules.Find(vanillaSkill) is {} s&&SkillActive(s.Name);
    private readonly StardewModdingAPI.Utilities.PerScreen<bool> Waiting=new(()=>true);
    private readonly StardewModdingAPI.Utilities.PerScreen<string> WasActive=new(()=>"");
    private bool WaitingForPolicy {get=>Waiting.Value;set=>Waiting.Value=value;}
    private static ProfessionService? Instance;
    private readonly IModHelper Helper;private readonly IMonitor Monitor;private readonly Harmony Patches;
    private string Reason="not initialized";
    private MethodBase[] Critical=Array.Empty<MethodBase>();
    internal ProfessionService(IModHelper helper,IMonitor monitor,string id,Action<bool> startup)
    {
        Helper=helper;Monitor=monitor;Patches=new Harmony(id+".AllProfessions");Instance=this;
        helper.Events.GameLoop.GameLaunched+=(_,_)=>{Install();startup(Enabled);};
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>{SkillRows.Reset();WaitingForPolicy=true;MigrateLocal();};
        helper.Events.GameLoop.UpdateTicked+=(_,e)=>
        {
            if(!e.IsMultipleOf(30)||!Context.IsWorldReady||!Configuration.SharedPreferences.Ready)return;
            bool active=Active;
            // Turning a single skill back on in the settings also repairs its professions right away.
            string state=active?"on:"+string.Join(",",Configuration.SharedPreferences.Current.AllProfessionsExcludedSkills??Array.Empty<string>()):"off";
            if(state!=WasActive.Value){WasActive.Value=state;WaitingForPolicy=active;}
            if(WaitingForPolicy)MigrateLocal();
            if(active&&Game1.activeClickableMenu is LevelUpMenu page)ProfessionMenus.Convert(page);
        };
        helper.Events.Player.LevelChanged+=(_,e)=>{if(e.IsLocalPlayer)WaitingForPolicy=true;};
        helper.Events.GameLoop.DayStarted+=(_,_)=>MigrateLocal();
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{ProfessionMenus.Clear();Waiting.ResetAllScreens();WasActive.ResetAllScreens();};
        helper.ConsoleCommands.Add("bsv_professions_status",Text("professions.command"),(_,_)=>Status());
    }
    internal static string Text(string key,object? tokens=null)=>Instance==null?key:BsvI18n.Get(Instance.Helper,key,tokens);
    internal static void Trace(string message)=>Instance?.Monitor.Log(message,LogLevel.Trace);
    internal bool Install()
    {
        try
        {
            if(typeof(Farmer).Module.ModuleVersionId.ToString()!=AuditedMvid)
                Monitor.Log("BSV professions: game build differs from the audited 1.6.15 reference; the exact API targets were found and are used.",LogLevel.Info);
            MethodInfo M(Type type,string name,Type result,params Type[] args)=>DrawPatchTargets.Require(type,name,result,args);
            var constructors=new[]{typeof(LevelUpMenu).GetConstructor(Type.EmptyTypes)!,typeof(LevelUpMenu).GetConstructor(new[]{typeof(int),typeof(int)})!};
            var update=M(typeof(LevelUpMenu),"update",typeof(void),typeof(GameTime));
            var perk=M(typeof(LevelUpMenu),"getLevelPerk",typeof(void),typeof(int),typeof(int));
            var ok=M(typeof(LevelUpMenu),"okButtonClicked",typeof(void));
            var remove=M(typeof(LevelUpMenu),"RemoveLevelFromLevelList",typeof(void));
            Critical=constructors.Concat(new MethodBase[]{update,perk,ok,remove,
                M(typeof(LevelUpMenu),"getImmediateProfessionPerk",typeof(void),typeof(int)),
                M(typeof(LevelUpMenu),"removeImmediateProfessionPerk",typeof(void),typeof(int)),
                M(typeof(LevelUpMenu),"AddMissedProfessionChoices",typeof(void),typeof(Farmer))}).ToArray();
            if(Critical.Any(m=>m==null))throw new MissingMethodException("All Professions menu target missing.");
            // Other mods patching the level-up menu (SpaceCore, skill mods...) no longer switch this feature off;
            // both keep running and the overlap is only reported once in the log.
            string? conflict=Conflicts();if(conflict!=null)WarnOverlap(conflict);
            HarmonyMethod Menu(string name)=>new(typeof(ProfessionMenus),name);
            foreach(var ctor in constructors)Patches.Patch(ctor,postfix:Menu(nameof(ProfessionMenus.Convert)));
            // Parameterless menu creates a page inside update. Prefix covers an already-open
            // chooser, postfix covers that late-created page before drawing or the next input.
            Patches.Patch(update,prefix:Menu(nameof(ProfessionMenus.Convert)),postfix:Menu(nameof(ProfessionMenus.Convert)));
            Patches.Patch(ok,prefix:Menu(nameof(ProfessionMenus.BeforeOk)));
            Patches.Patch(perk,prefix:Menu(nameof(ProfessionMenus.Perk)));
            Patches.Patch(remove,prefix:Menu(nameof(ProfessionMenus.BeforeRemove)));
            Patches.Patch(M(typeof(SkillsPage),"performHoverAction",typeof(void),typeof(int),typeof(int)),postfix:Menu(nameof(ProfessionMenus.SkillHover)));
            Patches.Patch(M(typeof(GameLocation),"answerDialogueAction",typeof(bool),typeof(string),typeof(string[])),
                prefix:new HarmonyMethod(typeof(ProfessionService),nameof(Statue)));
            try { ProfessionSkillsPage.Install(Patches); } catch(Exception ui) { Monitor.Log("BSV skill page could not initialize; native display retained.\n"+ui,LogLevel.Error); }
            // SpaceCore skills (e.g. YACS cooking): its level-up chooser becomes a plain page and every profession of the stage is granted.
            try
            {
                var custom=AccessTools.TypeByName("SpaceCore.Interface.SkillLevelUpMenu");
                var customUpdate=custom==null?null:AccessTools.Method(custom,"update",new[]{typeof(GameTime)});
                if(customUpdate!=null)Patches.Patch(customUpdate,postfix:Menu(nameof(ProfessionMenus.CustomLevelUp)));
                var customPerk=custom==null?null:AccessTools.Method(custom,"getLevelPerk",new[]{typeof(string),typeof(int)});
                if(customPerk!=null)Patches.Patch(customPerk,prefix:Menu(nameof(ProfessionMenus.CustomPerk)));
            }
            catch(Exception sc){Monitor.Log("BSV could not attach to SpaceCore's level-up page; custom skills keep their own choice.\n"+sc,LogLevel.Warn);}
            Enabled=true;Reason="active";Monitor.Log("All Professions initialized (vanilla five skills; owner-local grants).",LogLevel.Debug);return true;
        }
        catch(Exception ex)
        {
            Enabled=false;Reason=ex.Message;Patches.UnpatchAll(Patches.Id);ProfessionSkillsPage.Remove();
            Monitor.Log("BSV All Professions could not initialize; native profession selection retained.\n"+ex,LogLevel.Error);return false;
        }
    }
    private string? WarnedOverlap;
    private void WarnOverlap(string conflict)
    {
        if(WarnedOverlap==conflict)return;WarnedOverlap=conflict;
        Monitor.Log("BSV All Professions keeps running alongside another mod's level-up patch; if profession choices look wrong, report it with this log. "+conflict,LogLevel.Info);
    }
    private string? Conflicts()
    {
        foreach(var method in Critical)
        {
            var info=Harmony.GetPatchInfo(method);if(info==null)continue;
            var other=info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                .Where(p=>p.owner!=Patches.Id).Select(p=>p.owner).Distinct().ToArray();
            if(other.Length>0)return $"Unverified overlapping profession/level-up patch on {method.DeclaringType?.FullName}.{method.Name}: {string.Join(", ",other)}. No competing grants installed.";
        }
        return null;
    }
    private void MigrateLocal()
    {
        if(!Context.IsWorldReady || !ProfessionGrant.Owns(Game1.player))return;
        if(Configuration.SharedPreferences.HasLocal&&!Configuration.SharedPreferences.Ready)return;
        WaitingForPolicy=false;
        if(!Active)return;
        string? conflict=Conflicts();
        if(conflict!=null)WarnOverlap(conflict);
        try
        {
            int added=ProfessionGrant.Migrate(Game1.player);
            WaitingForPolicy=Game1.player.newLevels.Count>0;
            if(added>0)
            {
                Game1.addHUDMessage(new HUDMessage(Text("professions.migrated",new{count=added}),HUDMessage.newQuest_type));
                Trace($"Player {Game1.player.UniqueMultiplayerID}: repaired {added} missing professions; queued skills deferred.");
            }
        }
        catch(Exception ex){Monitor.Log("BSV profession migration failed; pending upgrades retained.\n"+ex,LogLevel.Error);}
    }
    internal static bool IsVanillaRespec(string? response)=>RespecSkill(response)!=null;
    private static string? RespecSkill(string? response)=>response switch{"professionForget_farming"=>"Farming","professionForget_mining"=>"Mining",
        "professionForget_foraging"=>"Foraging","professionForget_fishing"=>"Fishing","professionForget_combat"=>"Combat",_=>null};
    private static bool Statue(ref string questionAndAnswer,ref bool __result)
    {
        // SpaceCore skills: SpaceCore takes the 10,000g in a postfix, so the answer itself is neutralised as well.
        if(questionAndAnswer!=null&&questionAndAnswer.StartsWith("professionForget_")&&RespecSkill(questionAndAnswer)==null)
        {
            string id=questionAndAnswer.Split('_',2)[1];
            if(SkillRows.Custom.Any(r=>r.Key==id)&&SkillActive(id)&&ProfessionGrant.Owns(Game1.player))
            {
                Game1.drawObjectDialogue(Text("professions.statue"));
                questionAndAnswer="BSV_professionForget_blocked";__result=true;return false;
            }
        }
        // Do not intercept the whole dog statue or custom response IDs: third-party menu
        // entries remain reachable. These five native answers precede the 10,000g debit.
        if(RespecSkill(questionAndAnswer) is not {} key || !SkillActive(key) || !ProfessionGrant.Owns(Game1.player))return true;
        Game1.drawObjectDialogue(Text("professions.statue"));__result=true;return false;
    }
    private void Status()
    {
        Monitor.Log($"All Professions: enabled={Enabled}; reason={Reason}; game MVID={typeof(Farmer).Module.ModuleVersionId}; authority=owning local player; no mastery/custom-skill grants.",LogLevel.Info);
        if(!Context.IsWorldReady)return;
        foreach(var skill in ProfessionRules.Skills)
            Monitor.Log($"{skill.Name}: permanent={Game1.player.GetUnmodifiedSkillLevel(skill.Id)}, pending={ProfessionRules.Pending(Game1.player,skill.Id)}, owned=[{string.Join(",",skill.All.Where(Game1.player.professions.Contains))}]",LogLevel.Info);
    }
}

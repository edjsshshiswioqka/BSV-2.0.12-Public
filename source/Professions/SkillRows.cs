using System.Collections;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace BetterStardewValley.Professions;

/// <summary>
/// One row of the Skills page: the five vanilla skills, then skills added through SpaceCore (e.g. YACS cooking).
/// Profession ids are always ordered like vanilla: [0,1] level 5, [2,3] level 10 under [0], [4,5] level 10 under [1].
/// </summary>
internal sealed class SkillRow
{
    internal string Key="";                 // "Farming"... for vanilla, the SpaceCore skill id for custom skills
    internal int VanillaId=-1;              // Farmer.farmingSkill... ; -1 for a custom skill
    internal int[] Ids=Array.Empty<int>();
    internal Func<string> Name=()=>"";
    internal Func<Farmer,int> Level=_=>0;
    internal Func<int,string> Title=_=>"";
    internal Func<int,string> Description=_=>"";
    internal Func<int,(Texture2D Texture,Rectangle Source)?> Icon=_=>null;
    internal bool Custom=>VanillaId<0;
    internal IEnumerable<int> Expected(int level)=>level<5?Array.Empty<int>():level<10?Ids.Take(2):Ids;
    internal int Branch(int id){int i=Array.IndexOf(Ids,id);return i<2?i:(i-2)/2;}
    internal int StageOf(int id)=>Array.IndexOf(Ids,id)<2?5:10;
}

internal static class SkillRows
{
    private static List<SkillRow>? cache;
    internal static void Reset()=>cache=null;
    /// <summary>Vanilla rows first, then every SpaceCore skill that has the standard 5/10 profession tree.</summary>
    internal static IReadOnlyList<SkillRow> All
    {
        get
        {
            if(cache!=null)return cache;
            var rows=ProfessionRules.Skills.Select(Vanilla).ToList();
            try{rows.AddRange(SpaceCoreSkills.Rows());}
            catch(Exception ex){ProfessionService.Trace("BSV could not read SpaceCore skills; only the five vanilla skills are shown: "+ex.Message);}
            return cache=rows;
        }
    }
    internal static IEnumerable<SkillRow> Custom=>All.Where(r=>r.Custom);
    internal static SkillRow? ForProfession(int id)=>All.FirstOrDefault(r=>r.Ids.Contains(id));
    private static SkillRow Vanilla(ProfessionRules.Skill s)=>new()
    {
        Key=s.Name,VanillaId=s.Id,Ids=s.All.ToArray(),
        Name=()=>Farmer.getSkillDisplayNameFromIndex(s.Id),
        Level=f=>f.GetUnmodifiedSkillLevel(s.Id),
        Title=LevelUpMenu.getProfessionTitleFromNumber,
        Description=id=>string.Join("\n",LevelUpMenu.getProfessionDescription(id).Skip(1)),
        Icon=id=>(Game1.mouseCursors,ProfessionSkillsPage.ProfessionIcon(id))
    };
}

/// <summary>Reads SpaceCore's custom skills by reflection, so BSV keeps working without SpaceCore installed.</summary>
internal static class SpaceCoreSkills
{
    private static Type? SkillsType;
    internal static bool Loaded=>(SkillsType??=AccessTools.TypeByName("SpaceCore.Skills"))!=null;
    private static object? Get(object o,string name)=>AccessTools.Property(o.GetType(),name)?.GetValue(o);
    private static string Call(object o,string name)=>AccessTools.Method(o.GetType(),name,Type.EmptyTypes)?.Invoke(o,null) as string??"";
    internal static IEnumerable<SkillRow> Rows()
    {
        if(!Loaded)yield break;
        var list=AccessTools.Method(SkillsType,"GetSkillList")?.Invoke(null,null) as string[]??Array.Empty<string>();
        var getSkill=AccessTools.Method(SkillsType,"GetSkill",new[]{typeof(string)});
        var level=AccessTools.TypeByName("SpaceCore.SkillExtensions")?.GetMethod("GetCustomSkillLevel",new[]{typeof(Farmer),typeof(string)});
        if(getSkill==null||level==null)yield break;
        foreach(string id in list)
        {
            var skill=getSkill.Invoke(null,new object[]{id});
            if(skill==null||Get(skill,"ShouldShowOnSkillsPage") is false)continue;
            SkillRow? row=null;
            try{row=Build(id,skill,level);}catch(Exception ex){ProfessionService.Trace($"SpaceCore skill '{id}' could not be read: "+ex.Message);}
            if(row==null){ProfessionService.Trace($"SpaceCore skill '{id}' does not use the standard 5/10 profession tree; BSV leaves it to SpaceCore.");continue;}
            yield return row;
        }
    }
    private static SkillRow? Build(string id,object skill,MethodInfo level)
    {
        var pairs=(Get(skill,"ProfessionsForLevels") as IEnumerable)?.Cast<object>().ToList();
        if(pairs==null||pairs.Count!=3)return null;
        object? Prof(object pair,string side)=>Get(pair,side);
        int Lv(object pair)=>(Get(pair,"Level") as int?)??0;
        var five=pairs.Where(p=>Lv(p)==5&&Get(p,"Requires")==null).ToList();
        if(five.Count!=1)return null;
        var a=Prof(five[0],"First");var b=Prof(five[0],"Second");
        if(a==null||b==null)return null;
        var underA=pairs.FirstOrDefault(p=>Lv(p)==10&&ReferenceEquals(Get(p,"Requires"),a));
        var underB=pairs.FirstOrDefault(p=>Lv(p)==10&&ReferenceEquals(Get(p,"Requires"),b));
        if(underA==null||underB==null)return null;
        var profs=new[]{a,b,Prof(underA,"First"),Prof(underA,"Second"),Prof(underB,"First"),Prof(underB,"Second")};
        if(profs.Any(p=>p==null))return null;
        int Vid(object p)=>(int)AccessTools.Method(p.GetType(),"GetVanillaId")!.Invoke(p,null)!;
        if(profs.Select(p=>Vid(p!)).Distinct().Count()!=6)return null;
        var byId=profs.ToDictionary(p=>Vid(p!),p=>p!);
        return new SkillRow
        {
            Key=id,Ids=profs.Select(p=>Vid(p!)).ToArray(),
            Name=()=>Call(skill,"GetName"),
            Level=f=>(int)level.Invoke(null,new object[]{f,id})!,
            Title=pid=>byId.TryGetValue(pid,out var p)?Call(p,"GetName"):"",
            Description=pid=>byId.TryGetValue(pid,out var p)?Call(p,"GetDescription"):"",
            Icon=pid=>byId.TryGetValue(pid,out var p)&&Get(p,"Icon") is Texture2D t?(t,new Rectangle(0,0,16,16)):null
        };
    }
}

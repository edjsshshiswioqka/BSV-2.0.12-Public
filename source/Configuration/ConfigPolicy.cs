using Newtonsoft.Json;

namespace BetterStardewValley.Configuration;

internal static class ConfigPolicy
{
    internal static T Copy<T>(T source) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source))!;
    internal static void Normalize(ModConfig c)
    {
        c.AfkFishing ??=new(); c.HarvestBonus ??=new();
        c.AllProfessionsExcludedSkills=(c.AllProfessionsExcludedSkills??Array.Empty<string>()).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        c.SeedChance=HarvestRules.NormalizeChance(c.SeedChance);
        c.HorseSpeedBonus=double.IsFinite(c.HorseSpeedBonus)?Math.Clamp(c.HorseSpeedBonus,0,1):.20;
        c.AfkFishing.ReturnStaminaThreshold=double.IsFinite(c.AfkFishing.ReturnStaminaThreshold)?Math.Clamp(c.AfkFishing.ReturnStaminaThreshold,.01,.50):.05;
        var h=c.HarvestBonus;
        (h.DoubleChance,h.TripleChance)=Pair(h.DoubleChance,h.TripleChance,.20,.05);
        (h.BasicDoubleChance,h.BasicTripleChance)=Pair(h.BasicDoubleChance,h.BasicTripleChance,.25,.05);
        (h.QualityDoubleChance,h.QualityTripleChance)=Pair(h.QualityDoubleChance,h.QualityTripleChance,.35,.08);
        (h.DeluxeDoubleChance,h.DeluxeTripleChance)=Pair(h.DeluxeDoubleChance,h.DeluxeTripleChance,.45,.10);
    }
    private static (double,double) Pair(double twice,double triple,double defaultTwice,double defaultTriple)
    { return double.IsFinite(twice)&&double.IsFinite(triple)&&twice>=0&&triple>=0&&twice+triple<=1
        ?(twice,triple):(defaultTwice,defaultTriple); }
    // Preserve every existing service's config object, including nested option references.
    internal static void Apply(object source, object target)
    {
        foreach(var p in source.GetType().GetProperties().Where(p=>p.CanRead&&p.CanWrite))
        {
            var value=p.GetValue(source); var old=p.GetValue(target);
            if(value!=null&&old!=null&&p.PropertyType.IsClass&&p.PropertyType!=typeof(string)&&!p.PropertyType.IsArray)
                Apply(value,old);
            else p.SetValue(target,value);
        }
    }
}

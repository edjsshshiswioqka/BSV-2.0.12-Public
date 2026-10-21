using System.Text.Json;
using System.Text.RegularExpressions;
using BetterStardewValley;
using StardewModdingAPI;
using StardewValley;
int checks=0;
void Check(bool b,string message){checks++;if(!b)throw new Exception(message);}
foreach(var (level,twice,thrice) in new[]{(0,2000,500),(1,2500,500),(2,3500,800),(3,4500,1000)})
{
 var counts=new int[4];for(int n=0;n<10000;n++)counts[HarvestRules.YieldMultiplier(level,(n+.5)/10000)]++;
 Check(counts[2]==twice&&counts[3]==thrice&&counts[1]==10000-twice-thrice,"exclusive exact yield probability "+level);
 var p=HarvestRules.YieldChances(level);Check(HarvestRules.YieldMultiplier(level,p.Triple)==2,"triple upper edge is double");
 Check(HarvestRules.YieldMultiplier(level,Math.BitDecrement(p.Triple))==3,"triple lower edge inclusive");
 if(level<3)Check(HarvestRules.YieldMultiplier(level,p.Triple+p.Double)==1,"double upper edge is ordinary");
}
Check(HarvestRules.YieldMultiplier(-1,.1)==2&&HarvestRules.YieldMultiplier(4,.99)==1,"unrecognized low fertilizer is base; higher boost capped to deluxe");
ModEntry.CheckBoundary(Check);
var helper=new IModHelper();
foreach(string lang in new[]{"default","zh"})helper.Translation.Locales[lang]=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(args[0],lang+".json")))!;
var en=helper.Translation.Locales["default"];var zh=helper.Translation.Locales["zh"];
Check(en.Keys.ToHashSet().SetEquals(zh.Keys),"identical Chinese and English coverage");
foreach(string locale in new[]{"ja","ko","de","ru","es","fr","pt","it","tr","hu","vi","custom-a","custom-b"})helper.Translation.Locales[locale]=en.ToDictionary(p=>p.Key,p=>locale+":"+p.Value);
// Real shipped translations replace routing-marker fixtures when present.
// Missing files remain synthetic routing tests, not claimed translation coverage.
var shipped=new List<string>();
foreach(string path in Directory.GetFiles(args[0],"*.json")){
 string locale=Path.GetFileNameWithoutExtension(path);shipped.Add(locale);
 helper.Translation.Locales[locale]=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))!;
 Check(helper.Translation.Locales[locale].Keys.ToHashSet().SetEquals(en.Keys),"shipped key coverage "+locale);
}
foreach(string locale in helper.Translation.Locales.Keys.Append("unknown").Append("en")){
 helper.Translation.Locale=locale;
 foreach(string key in en.Keys){Check(BsvI18n.Get(helper,key)==(helper.Translation.Locales.TryGetValue(locale,out var texts)?texts[key]:en[key]),"SMAPI current locale including independent custom languages: "+locale+" "+key);}
}
foreach(string key in en.Keys){Check(!string.IsNullOrWhiteSpace(en[key])&&!string.IsNullOrWhiteSpace(zh[key]),"both texts exist");Check(Regex.Matches(en[key],@"\{\{(.*?)\}\}").Select(m=>m.Value).OrderBy(v=>v).SequenceEqual(Regex.Matches(zh[key],@"\{\{(.*?)\}\}").Select(m=>m.Value).OrderBy(v=>v)),"matching token schema "+key);}
LocalizedContentManager.CurrentLanguageCode=LocalizedContentManager.LanguageCode.zh;
helper.Translation.Locale="zh";
var t=BsvI18n.Get(helper,"status.working",new{hours=4,minutes=20});Check(t.Contains("4")&&t.Contains("20")&&!t.Contains("{{"),"dynamic tokens applied");
Check(BsvI18n.Get(helper,"status.working",new{hours=9,minutes=1})!=t,"cached tokens don't leak");
Check(BsvI18n.Get(helper,"missing.key")=="Text unavailable","unknown key cannot expose raw key or fall back to a third language");
helper.Translation.Locales["zh"]["rescue.give-up"]="  ";
Check(BsvI18n.Get(helper,"rescue.give-up")==en["rescue.give-up"],"blank localized emergency button uses English fallback");
helper.Translation.Locales["default"]["rescue.give-up"]="TODO";
Check(BsvI18n.Get(helper,"rescue.give-up")=="Give Up","both broken emergency translations retain safe text");
helper.Translation.Broken=true;
Check(BsvI18n.Get(helper,"rescue.give-up")=="Give Up","translation exception does not block emergency input layout");
helper.Translation.Broken=false;
float Measure(string text)=>System.Globalization.StringInfo.ParseCombiningCharacters(text).Length*14;
foreach(string text in new[]{"Give Up","放弃等待","Aufgeben","Abandonner l’attente","等待救援の終了","Chấm dứt chờ cứu hộ","A várakozás feladása","Đa\u0301nh dấu 😀"}.Concat(Directory.GetFiles(args[0],"*.json").Select(path=>JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))!["rescue.give-up"])))
foreach(var screen in new[]{(640,360),(800,600),(1280,720),(320,360)})
{
 var layout=DisplayText.Emergency(text,screen.Item1,screen.Item2,24,Measure);
 Check(layout.X>=0&&layout.Y>=0&&layout.X+layout.Width<=screen.Item1&&layout.Y+layout.Height<=screen.Item2,"emergency layout stays in viewport");
 Check(layout.Text.Replace("\n","").Replace(" ","")==text.Replace(" ",""),"emergency wrap never truncates full label");
 Check(layout.Text.Split('\n').All(s=>Measure(s)<=layout.Width-24),"emergency label fits measured width without microscopic scaling");
 Check(layout==DisplayText.Emergency(text,screen.Item1,screen.Item2,24,Measure),"input and draw pure layout match without prior draw");
}
Check(DisplayText.Ellipsis("a\u0301😀bc",42,Measure)=="a\u0301😀…","ellipsis preserves combining text elements and surrogate pairs");
Check(DisplayText.Wrap("没有空格的中文长句",42,Measure).Split('\n').All(s=>Measure(s)<=42),"CJK text wraps without requiring spaces");
foreach(string corrupt in new[]{new string('界',10000),new string('A',500),"  "}){
 var layout=DisplayText.Emergency(corrupt,320,180,24,Measure);
 Check(layout.Text=="Give Up"&&layout.Y+layout.Height<=180,"unrenderably long or blank emergency label uses readable independent fallback");
}
var originalCulture=System.Globalization.CultureInfo.CurrentCulture;
foreach(var (locale,expected) in new[]{("de","1.234,5"),("pt","1.234,5"),("ja","1,234.5"),("missing-custom","1,234.5")}){
 helper.Translation.Locale=locale;
 Check(BsvI18n.Number(helper,1234.5,"N1")==expected,"display-only numeric culture "+locale);
 Check(ReferenceEquals(System.Globalization.CultureInfo.CurrentCulture,originalCulture),"numeric display does not modify serialization culture");
}
BsvI18n.Register(helper);helper.Events.Content.Change();Check(helper.GameContent.Invalidated.Count==6&&CraftingRecipe.craftingRecipes.ContainsKey("fresh")&&CraftingRecipe.cookingRecipes.ContainsKey("fresh"),"language changes refresh item, machine, recipe, event and mail data");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions=checks,shipped_locales=shipped,missing_required_locales=new[]{"default","zh","ja","ko","de","ru","es","fr","pt","it","tr","hu","vi"}.Except(shipped).ToArray(),synthetic_custom_locale_fixtures=new[]{"custom-a","custom-b"},scope="Actual harvest output helper, fertilizer rules and BSV locale selector against headless API boundaries; no live Harmony installation."}));

using Mono.Cecil;
using Mono.Cecil.Cil;
using StardewValley;
using System.Globalization;
static class MetadataChecks
{
 internal static void Run(List<string> checks)
 {
  using var asm=AssemblyDefinition.ReadAssembly(typeof(Farmer).Assembly.Location);
  void Check(bool b,string s){if(!b)throw new Exception(s);checks.Add(s);}
  MethodDefinition Method(string type,string name)
  {
   var matches=asm.MainModule.GetType(type).Methods.Where(m=>m.Name==name).ToArray();
   if(matches.Length>1)matches=matches.Where(m=>m.HasBody&&m.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="professions")).ToArray();
   if(matches.Length!=1)throw new Exception($"Audit target {type}.{name}: {matches.Length} profession-reading overloads");
   return matches[0];
  }
  var farmer=asm.MainModule.GetType("StardewValley.Farmer");
  var constants=new Dictionary<string,int>{{"farmingSkill",0},{"fishingSkill",1},{"foragingSkill",2},{"miningSkill",3},{"combatSkill",4},{"fighter",24},{"defender",27},{"scout",25},{"brute",26},{"acrobat",28},{"desperado",29}};
  foreach(var row in constants)Check(Convert.ToInt32(farmer.Fields.Single(f=>f.Name==row.Key).Constant)==row.Value,"native constant "+row.Key+"="+row.Value);
  var professionNames=new[]{"rancher","tiller","butcher","shepherd","artisan","agriculturist","fisher","trapper","angler","pirate","baitmaster","mariner","forester","gatherer","lumberjack","tapper","botanist","tracker","miner","geologist","blacksmith","burrower","excavator","gemologist","fighter","scout","brute","defender","acrobat","desperado"};
  for(int id=0;id<30;id++)Check(Convert.ToInt32(farmer.Fields.Single(f=>f.Name==professionNames[id]).Constant)==id,"verified native profession ID "+id+"="+professionNames[id]);
  var add=Method("StardewValley.Menus.LevelUpMenu","getImmediateProfessionPerk");
  Check(add.Body.Instructions.Count(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.Name=="maxHealth")==2,"only Fighter/Defender have native immediate permanent HP writes");
  Check(add.Body.Instructions.Any(i=>Number(i)==15)&&add.Body.Instructions.Any(i=>Number(i)==25),"native permanent bonuses +15/+25");
  var health=Method("StardewValley.Menus.LevelUpMenu","RevalidateHealth");
  Check(health.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="GetUnmodifiedSkillLevel")&&health.Body.Instructions.Any(i=>i.OpCode==OpCodes.Bge_S),"native health validator uses unmodified combat and only raises a minimum");
  var damage=Method("StardewValley.GameLocation","damageMonster");
  foreach(int id in new[]{24,25,26,29})Check(Has(damage,id),"native damage method independently reads profession "+id);
  var il=damage.Body.Instructions;
  int fighter=Index(damage,24),brute=Index(damage,26),desperado=Index(damage,29);
  Check(((Instruction)il[fighter+3].Operand).Offset==il[brute-2].Offset || ((Instruction)il[fighter+3].Operand).Offset<il[brute].Offset,"Fighter false branch still reaches Brute check");
  Check(il.Skip(fighter).Take(brute-fighter).Any(i=>Number(i)==1.1)&&il.Skip(brute).Take(desperado-brute).Any(i=>Number(i)==1.15),"native sequential Fighter 1.1 and Brute 1.15 with ceiling each");
  Check(il.Skip(Index(damage,25)).Take(12).Any(i=>Number(i)==.5)&&il.Skip(desperado).Take(12).Any(i=>Number(i)==2),"Scout +50% of crit chance; Desperado x2 only existing critical branch");
  var weapon=Method("StardewValley.Tools.MeleeWeapon","doAnimateSpecialMove");
  Check(weapon.Body.Instructions.Count(i=>Number(i)==28)==3&&weapon.Body.Instructions.Count(i=>i.OpCode==OpCodes.Div)>=3,"all three native special moves halve cooldown for Acrobat");
  var price=Method("StardewValley.Object","getPriceAfterMultipliers");
  Check(Has(price,6)&&Has(price,8)&&price.Body.Instructions.Any(i=>Number(i)==1.25)&&price.Body.Instructions.Any(i=>Number(i)==1.5),"native Fisher/Angler selection retained");
  var needs=Method("StardewValley.Objects.CrabPot","NeedsBait");var pot=Method("StardewValley.Objects.CrabPot","DayUpdate");
  Check(Has(needs,11)&&Has(pot,10),"native ID11=Luremaster bait exemption, ID10=Mariner trash exemption (legacy constant names differ)");
  foreach(var row in new[]{("StardewValley.TerrainFeatures.HoeDirt","applySpeedIncreases",5),("StardewValley.GameLocation","checkAction",13),("StardewValley.GameLocation","GetHarvestSpawnedObjectQuality",16),("StardewValley.Game1","drawHUD",17),("StardewValley.GameLocation","breakStone",18),("StardewValley.GameLocation","breakStone",19),("StardewValley.GameLocation","breakStone",21),("StardewValley.Locations.MineShaft","checkStoneForItems",22),("StardewValley.Tools.FishingRod","startMinigameEndFunction",9),("StardewValley.TerrainFeatures.Tree","tickUpdate",12),("StardewValley.TerrainFeatures.Tree","tickUpdate",14)})
   Check(Has(Method(row.Item1,row.Item2),row.Item3),"native effect read "+row.Item3+" "+row.Item1+"."+row.Item2);
  foreach(string field in new[]{"ProfessionForHappinessBoost","ProfessionForFasterProduce","ProfessionForQualityBoost"})
   Check(asm.MainModule.GetType("StardewValley.FarmAnimal").Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is FieldReference f&&f.Name==field),"animal effects use loaded data field "+field);
  // Boundary audit of production module: it has no gainExperience or mastery setter call.
  using var bsv=AssemblyDefinition.ReadAssembly(typeof(BetterStardewValley.ModEntry).Assembly.Location);
  var code=bsv.MainModule.Types.Where(t=>t.Namespace=="BetterStardewValley.Professions").SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).ToArray();
  Check(!code.Any(i=>i.Operand is MethodReference m&&(m.Name=="gainExperience"||m.Name=="set_experiencePoints"||m.Name.Contains("Mastery"))),"All Professions does not award XP or mastery");
 }
 static double? Number(Instruction i)=>i.OpCode.Code switch{Code.Ldc_I4_0=>0,Code.Ldc_I4_1=>1,Code.Ldc_I4_2=>2,Code.Ldc_I4_3=>3,Code.Ldc_I4_4=>4,Code.Ldc_I4_5=>5,Code.Ldc_I4_6=>6,Code.Ldc_I4_7=>7,Code.Ldc_I4_8=>8,Code.Ldc_I4_S or Code.Ldc_I4 or Code.Ldc_R4 or Code.Ldc_R8=>Math.Round(Convert.ToDouble(i.Operand,CultureInfo.InvariantCulture),6),_=>null};
 static int Index(MethodDefinition m,int id){var a=m.Body.Instructions;for(int i=0;i<a.Count-2;i++)if(a[i].Operand is FieldReference f&&f.Name=="professions"&&Number(a[i+1])==id&&a[i+2].Operand is MethodReference r&&r.Name=="Contains")return i;return -1;}
 static bool Has(MethodDefinition m,int id)=>Index(m,id)>=0;
}

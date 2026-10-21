using System.Reflection;
using BetterStardewValley;
using BetterStardewValley.Farming;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Buildings;
using StardewValley.Objects;
using StardewValley.GameData.Crops;
using StardewValley.TerrainFeatures;
using Mod=BetterStardewValley.ModEntry;

partial class Program
{
 static Random HarvestRandom(double a,double b,double c,double d,double e)=>new(47);
 static IEnumerable<CodeInstruction> FixedRandom(IEnumerable<CodeInstruction> code)
 {
  var target=AccessTools.Method(typeof(Utility),"CreateRandom",new[]{typeof(double),typeof(double),typeof(double),typeof(double),typeof(double)});
  foreach(var op in code){if(op.Calls(target))op.operand=AccessTools.Method(typeof(Program),nameof(HarvestRandom));yield return op;}
 }
 static readonly List<Item> Dropped=new();
 static bool Drop(Item __0,ref Debris __result){Dropped.Add(__0);__result=null!;return false;}
 static void NativeHarvests()
 {
  var h=new Harmony("BSV.Farming.NativeHarvestFixture");
  h.Patch(AccessTools.Method(typeof(Crop),"harvest"),transpiler:new(typeof(Program),nameof(FixedRandom)));
  foreach(var method in typeof(Game1).GetMethods(Any).Where(m=>m.Name=="createItemDebris"&&m.GetParameters().FirstOrDefault()?.ParameterType==typeof(Item)))h.Patch(method,prefix:new(typeof(Program),nameof(Drop)));
  foreach(var pair in new[]{(typeof(Farmer),"animateOnce"),(typeof(Farmer),"gainExperience"),(typeof(GameLocation),"playSound"),(typeof(DelayedAction),"playSoundAfterDelay"),(typeof(Game1),"showRedMessage")})
   foreach(var method in pair.Item1.GetMethods(Any).Where(m=>m.Name==pair.Item2))h.Patch(method,prefix:new(typeof(Program),nameof(Skip)));
  var main=new Mod();typeof(Mod).GetField("Instance",Any)!.SetValue(null,main);typeof(Mod).GetField("Config",Any)!.SetValue(main,Config);
  typeof(StardewModdingAPI.Mod).GetProperty("Monitor",Any)!.SetValue(main,Recorder.Make(typeof(IMonitor)));
  h.Patch(AccessTools.Method(typeof(Crop),"harvest"),prefix:new(typeof(Mod),"BeforeHarvest"){priority=Priority.First},postfix:new(typeof(Mod),"AfterHarvest"){priority=Priority.Last},transpiler:new(typeof(Mod),"CaptureHarvestOutputs"),finalizer:new(typeof(Mod),"FinishHarvestScope"));
  Game1.year=1;Game1.season=Season.Spring;Game1.dayOfMonth=1;Game1.timeOfDay=600;Host.stats=new();
  IsHost=true;Player(Host);Config.HarvestBonus.Enabled=true;Config.HarvestBonus.EnableFertilizerYieldBonus=false;Config.HarvestBonus.DoubleChance=1;Config.HarvestBonus.TripleChance=0;Config.EnableHarvestSeeds=false;
  var data=Game1.cropData["472"];data.HarvestMinStack=data.HarvestMaxStack=3;data.ExtraHarvestChance=0;
  foreach(bool scythe in new[]{false,true})foreach(int original in new[]{1,3,4,7})
  {
   data.RegrowDays=original;Config.EnableBsvSpeedGro=true;Host.Items.Clear();for(int n=0;n<36;n++)Host.Items.Add(null);Dropped.Clear();
   var soil=Dirt();var crop=PlantCrop(soil);FertilizerService.Write(soil,FertilizerRules.Add(FertilizerRules.Native("(O)919"),"(O)918",0));crop.currentPhase.Value=crop.phaseDays.Count-1;
   for(int round=0;round<5;round++)
   {
    crop.dayOfCurrentPhase.Value=0;int before=Host.Items.Where(i=>i?.QualifiedItemId=="(O)24").Sum(i=>i.Stack)+Dropped.Where(i=>i.QualifiedItemId=="(O)24").Sum(i=>i.Stack);
    bool result=crop.harvest(3,3,soil,null,scythe);
    int after=Host.Items.Where(i=>i?.QualifiedItemId=="(O)24").Sum(i=>i.Stack)+Dropped.Where(i=>i.QualifiedItemId=="(O)24").Sum(i=>i.Stack);
    if(after-before!=6)throw new Exception($"native harvest output {scythe}/{original}/{round}: {after-before}; logs: "+string.Join("\n",Recorder.Logs));
    Check(!result&&after-before==6,"H04 native manual/scythe three products receive exactly three extras "+scythe+"/"+original+"/"+round);
    Check(crop.dayOfCurrentPhase.Value==FertilizerRules.Regrow(original,3,true)&&data.RegrowDays==original,"S04 native reset uses unchanged runtime D five times "+scythe+"/"+original+"/"+round);
    int timer=crop.dayOfCurrentPhase.Value;Config.EnableBsvSpeedGro=false;Check(crop.dayOfCurrentPhase.Value==timer,"S05 disabling preserves active native timer");Config.EnableBsvSpeedGro=true;
    crop.harvest(3,3,soil,null,scythe);int unchanged=Host.Items.Where(i=>i?.QualifiedItemId=="(O)24").Sum(i=>i.Stack)+Dropped.Where(i=>i.QualifiedItemId=="(O)24").Sum(i=>i.Stack);
    Check(unchanged==after,"H05 unready duplicate native harvest grants nothing");
   }
   crop.dayOfCurrentPhase.Value=0;Config.EnableBsvSpeedGro=false;crop.harvest(3,3,soil,null,scythe);Check(crop.dayOfCurrentPhase.Value==original,"S05 next native reset respects OFF");
  }
  // Preserve native inventory-full failure; no success, bonus, or regrowth reset.
  data.RegrowDays=7;Host.Items.Clear();for(int n=0;n<36;n++)Host.Items.Add(ItemRegistry.Create("(O)472",999));Dropped.Clear();
  var fullSoil=Dirt();var fullCrop=PlantCrop(fullSoil);fullCrop.currentPhase.Value=fullCrop.phaseDays.Count-1;
  fullCrop.harvest(3,3,fullSoil,null,false);
  Check(!fullCrop.fullyGrown.Value&&Dropped.Count==0&&!Host.Items.Any(i=>i?.QualifiedItemId=="(O)24"),"H05 native full backpack refuses manual harvest without extra reward");
  h.Patch(AccessTools.Method(typeof(Building),"resetTexture"),prefix:new(typeof(Program),nameof(Skip)));
  foreach(var method in typeof(Utility).GetMethods(Any).Where(m=>m.Name=="isOnScreen"))h.Patch(method,prefix:new(typeof(Program),nameof(No)));
  Game1.bigCraftableData=new Dictionary<string,StardewValley.GameData.BigCraftables.BigCraftableData>{["130"]=new(){Name="Chest",DisplayName="Chest"}};
  ItemRegistry.AddTypeDefinition(new StardewValley.ItemTypeDefinitions.BigCraftableDataDefinition());
  Game1.buildingData=new Dictionary<string,StardewValley.GameData.Buildings.BuildingData>();var hut=new JunimoHut();var chest=new Chest(true){Name="Output"};hut.buildingChests.Add(chest);Farm.buildings.Add(hut);
  var junimo=new JunimoHarvester{currentLocation=Farm,home=hut,Position=new(192,192)};
  foreach(bool full in new[]{false,true})
  {
   chest.Items.Clear();if(full)for(int n=0;n<chest.GetActualCapacity();n++)chest.Items.Add(ItemRegistry.Create("(O)472",999));Dropped.Clear();
   var soil=Dirt();var crop=PlantCrop(soil);crop.currentPhase.Value=crop.phaseDays.Count-1;FertilizerService.Write(soil,FertilizerRules.Add(FertilizerRules.Native("(O)919"),"(O)918",0));Config.EnableBsvSpeedGro=true;
   crop.harvest(3,3,soil,junimo,false);
   int total=chest.Items.Where(i=>i?.QualifiedItemId=="(O)24").Sum(i=>i.Stack)+Dropped.Where(i=>i.QualifiedItemId=="(O)24").Sum(i=>i.Stack);
   if(total!=6||crop.dayOfCurrentPhase.Value!=5)throw new Exception($"Junimo {full}: total={total}, days={crop.dayOfCurrentPhase.Value}, hut={hut.id.Value}, home={junimo.HomeId}, pending="+string.Join(";",Host.modData.Keys.Where(k=>k.StartsWith(HarvestNetwork.Outbox)))+" logs="+string.Join("\n",Recorder.Logs));
   Check(total==6&&crop.dayOfCurrentPhase.Value==5,"H04/H05 actual Junimo harvest, native chest delivery/overflow, one bonus and reset "+full);
  }
  var pendingId=Guid.NewGuid().ToString("N");var pendingCrop=Guid.NewGuid().ToString("N");
  var pending=new HarvestRequest(1,pendingId,Host.UniqueMultiplayerID,pendingCrop,0,Farm.NameOrUniqueName,3,3,"472",0,HarvestNetwork.Pack(new[]{ItemRegistry.Create("(O)24",3)}),hut.id.Value.ToString("N"),false);
  string pendingKey=HarvestNetwork.Outbox+pendingId;Host.modData[pendingKey]=System.Text.Json.JsonSerializer.Serialize(pending);Service.Harvests.Process(Host,pendingKey);Farm.buildings.Remove(hut);Dropped.Clear();Service.Harvests.Receive(Host,pendingKey);
  Check(Dropped.Sum(i=>i.Stack)==3&&!Host.modData.ContainsKey(pendingKey),"H05 hut removed after settlement returns exact owed extras at original crop tile");
  data.RegrowDays=7;data.HarvestMinStack=data.HarvestMaxStack=1;Config.HarvestBonus=new();
 }
}

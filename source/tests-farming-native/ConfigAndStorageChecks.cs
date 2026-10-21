using System.Globalization;
using System.Xml.Serialization;
using BetterStardewValley;
using BetterStardewValley.Configuration;
using BetterStardewValley.Farming;
using Eddie.FishingMachine.Core;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

partial class Program
{
 static void NativeToolSafety()
 {
  Host.Items.Clear();for(int n=0;n<36;n++)Host.Items.Add(null);Host.CurrentToolIndex=35;
  var rods=Enumerable.Range(0,6).Select(_=>new StardewValley.Tools.FishingRod(2){ItemId="FiberglassRod"}).ToArray();
  for(int n=0;n<6;n++)Host.Items[n]=rods[n];
  Check(Eddie.FishingMachine.CraftingSupport.Slots().Count(s=>s.SafeTool)==6,"M08 six distinct native fiberglass tools counted individually");
  Host.CurrentToolIndex=0;Check(!Eddie.FishingMachine.CraftingSupport.Slots()[0].SafeTool,"M08 equipped native rod rejected");Host.CurrentToolIndex=35;
  rods[0].attachments[0]=ItemRegistry.Create<StardewValley.Object>("(O)368",1);Check(!Eddie.FishingMachine.ProductionService.SafeRod(rods[0])&&rods[0].attachments[0]!=null,"M08 attached item protected without mutation");rods[0].attachments[0]=null;
  rods[0].modData["OtherMod/Important"]="keep";Check(!Eddie.FishingMachine.ProductionService.SafeRod(rods[0]),"M08 extra tool metadata protected");rods[0].modData.Clear();
  rods[0].UpgradeLevel=3;Check(!Eddie.FishingMachine.ProductionService.SafeRod(rods[0]),"M08 real tool level must match item ID");rods[0].UpgradeLevel=2;
  Host.Items[1]=rods[0];Check(!Eddie.FishingMachine.CraftingSupport.Slots()[0].SafeTool&&!Eddie.FishingMachine.CraftingSupport.Slots()[1].SafeTool,"M08 repeated physical tool reference cannot count twice");
  var iridium=new StardewValley.Tools.FishingRod(3){ItemId="IridiumRod"};Check(Eddie.FishingMachine.ProductionService.SafeRod(iridium),"M06 ordinary native iridium tool legal without advanced rod or mastery");
 }
 static void ConfigFilesChecks()
 {
  string dir=Path.Combine(Path.GetTempPath(),"bsv-r13-config-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string? previous=Recorder.ModuleDirectory;Recorder.ModuleDirectory=dir;
  try
  {
   var helper=(IModHelper)Recorder.Make(typeof(IModHelper));var monitor=(IMonitor)Recorder.Make(typeof(IMonitor));
   string path=Path.Combine(dir,"fishing-machine.json"),raw="{\"CycleGameMinutes\":2880,\"Foreign\":{\"Keep\":17},\"RulesVersion\":0,\"LevelThreeTripleChance\":0.15}";File.WriteAllText(path,raw);
   BalanceConfig Load()=>MachineBalanceMigration.Load<BalanceConfig>(helper,monitor,"fishing-machine.json",false,c=>{c.Migrate();c.Validate();});
   var first=Load();Check(first.MinutesForLevel(2)==4320&&first.MinutesForLevel(3)==5760&&File.ReadAllText(path+".pre-r15.bak")==raw,"M10 real filesystem migration writes exact original backup before new settings");
   var saved=JObject.Parse(File.ReadAllText(path));Check(saved["Foreign"]!["Keep"]!.Value<int>()==17&&saved.Value<int>("RulesVersion")==1&&saved.Value<double>("LevelThreeTripleChance")==.24&&saved.Value<int>("FishingBalanceVersion")==1&&first.BaitPerCycle==200&&first.LevelTwoBaitPerCycle==300&&first.LevelThreeBaitPerCycle==400,"M10/R15 unknown fields preserved, approved budget and probability migration applied");
   saved["LevelTwoCycleGameMinutes"]=6000;File.WriteAllText(path,saved.ToString());string changed=File.ReadAllText(path);Load();Check(File.ReadAllText(path)==changed&&File.ReadAllText(path+".pre-r15.bak")==raw,"M10 subsequent manual level edit survives reload; original backup unchanged");
   saved["CycleGameMinutes"]=-1;File.WriteAllText(path,saved.ToString());changed=File.ReadAllText(path);bool threw=false;try{Load();}catch(ArgumentException){threw=true;}Check(threw&&File.ReadAllText(path)==changed,"M10 invalid input is rejected before any replacement");
   File.WriteAllText(Path.Combine(dir,"config.json"),"{\"ConfigSchemaVersion\":2,\"HarvestBonus\":{\"EnableFertilizerYieldBonus\":false}}");var old=ConfigFiles.Load(helper,monitor);
   Check(!old.HarvestBonus.EnableFertilizerYieldBonus&&old.EnableBsvSpeedGro,"D07 existing explicit F OFF preserved; absent new S defaults ON without GMCM");
   // Execute the real private GMCM callbacks, with only its helper/visual API boundary faked.
   var machine=new Eddie.FishingMachine.ModEntry(helper,monitor,(IManifest)Recorder.Make(typeof(IManifest))){Ready=true};
   var oldMachine=Eddie.FishingMachine.ModEntry.Instance;Eddie.FishingMachine.ModEntry.Instance=machine;
   try
   {
    Config.HarvestBonus.EnableFertilizerYieldBonus=false;Config.EnableBsvSpeedGro=false;
    var menu=typeof(ConfigMenu).GetConstructors(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Single().Invoke(new object[]{helper,monitor,(IManifest)Recorder.Make(typeof(IManifest)),Config});
    var soil=Dirt();var crop=PlantCrop(soil);crop.fullyGrown.Value=true;crop.dayOfCurrentPhase.Value=5;
    typeof(ConfigMenu).GetMethod("Reset",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(menu,null);
    var draft=(ModConfig)typeof(ConfigMenu).GetField("draft",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(menu)!;
    Check(draft.EnableBsvSpeedGro&&draft.HarvestBonus.EnableFertilizerYieldBonus&&crop.dayOfCurrentPhase.Value==5,"M10/D07 actual GMCM Reset restores F/S ON without changing crop timer");
    typeof(ConfigMenu).GetMethod("Save",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(menu,null);
    var disk=JObject.Parse(File.ReadAllText(Path.Combine(dir,"config.json")));
    Check(Config.EnableBsvSpeedGro&&disk.Value<bool>("EnableBsvSpeedGro")&&disk["HarvestBonus"]!.Value<bool>("EnableFertilizerYieldBonus"),"M10/D07 actual GMCM Save applies both switches to disk and runtime");
    Check(machine.Config.MinutesForLevel(2)==4320&&machine.Config.MinutesForLevel(3)==5760&&machine.CrabConfig.MinutesForLevel(2)==5760&&crop.dayOfCurrentPhase.Value==5,"M10 real Reset/Save level defaults and active timer conservation");
   }
   finally{Eddie.FishingMachine.ModEntry.Instance=oldMachine;}
   foreach(bool f in new[]{false,true})foreach(bool speed in new[]{false,true})
   {
    Config.HarvestBonus.EnableFertilizerYieldBonus=f;Config.EnableBsvSpeedGro=speed;IsHost=true;Player(Host);SharedPreferences.Publish(Config);IsHost=false;Player(Guest);
    Check(SharedPreferences.Ready&&SharedPreferences.Current.HarvestBonus.EnableFertilizerYieldBonus==f&&SharedPreferences.Current.EnableBsvSpeedGro==speed&&SharedPreferences.CycleMinutes(false,2)==4320&&SharedPreferences.CycleMinutes(true,2)==5760,"M10/D07 host snapshot transmits two switches and level durations "+f+speed);
   }
  }
  finally{IsHost=true;Player(Host);Recorder.ModuleDirectory=previous;Directory.Delete(dir,true);}
 }
 static void StorageChecks()
 {
  var original=Farm;
  foreach(string name in new[]{"Greenhouse","IslandWest","CustomRoom_A","CustomRoom_B","Farm"})
  {
   Farm=name=="Farm"?original:new GameLocation();Farm.name.Value=name;if(Farm!=original)Game1.game1._locations.Add(Farm);Host.currentLocation=Guest.currentLocation=Farm;
   var soil=Dirt();Apply(soil,"(O)919");Apply(soil,"(O)918");var crop=PlantCrop(soil);crop.dead.Value=true;
   Check(FertilizerService.Read(soil).Dual,"D04 death alone does not destroy retained native fertilizer "+name);
   soil.destroyCrop(false);Check(FertilizerService.Read(soil).Dual==!string.IsNullOrEmpty(soil.fertilizer.Value),"D04 actual native removal retains/clears extension with primary "+name);
   var fresh=new HoeDirt();fresh.Location=Farm;fresh.Tile=soil.Tile;Farm.terrainFeatures[soil.Tile]=fresh;Check(FertilizerService.Read(fresh).Primary=="","D03/D04 replacement at same coordinates has no ghost fertilizer "+name);
  }
  Farm=original;Host.currentLocation=Guest.currentLocation=Farm;
  foreach(bool f in new[]{false,true})foreach(bool speed in new[]{false,true})foreach(bool reverse in new[]{false,true})
  {
   Config.HarvestBonus.EnableFertilizerYieldBonus=f;Config.EnableBsvSpeedGro=speed;
   var soil=Dirt();Apply(soil,reverse?"(O)465":"(O)368");Apply(soil,reverse?"(O)368":"(O)465");
   Check(soil.GetFertilizerQualityBoostLevel()==1&&soil.GetFertilizerSpeedBoost()>.09f,"D07 new dual application works in all F/S states, both orders "+f+speed+reverse);
  }
  var potSoil=Dirt();Farm.terrainFeatures.Remove(potSoil.Tile);var pot=new IndoorPot();pot.Location=Farm;pot.TileLocation=potSoil.Tile;pot.hoeDirt.Value=potSoil;Farm.objects[potSoil.Tile]=pot;
  Check(ReferenceEquals(Farm.GetHoeDirtAtTile(potSoil.Tile),potSoil),"D03 actual native flowerpot lookup resolves its own soil");
  Apply(potSoil,"(O)919");Apply(potSoil,"(O)918");Check(FertilizerService.Read(potSoil).Dual,"D03 approved transaction supports native flowerpot soil");Farm.objects.Remove(potSoil.Tile);
 }
}

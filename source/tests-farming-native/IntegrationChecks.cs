using System.Globalization;
using System.Text.Json;
using System.Xml.Serialization;
using BetterStardewValley;
using BetterStardewValley.Farming;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

partial class Program
{
 static Crop PlantCrop(HoeDirt soil)
 {
  var crop=new Crop();crop.netSeedIndex.Value="472";crop.indexOfHarvest.Value="24";soil.crop=crop;crop.Dirt=soil;
  crop.ResetPhaseDays();FertilizerService.EnsureCrop(crop);return crop;
 }
 static void NativeGrowth()
 {
  foreach(string speed in new[]{"(O)465","(O)466","(O)918"})foreach(bool agriculturist in new[]{false,true})
  {
   Host.professions.Clear();if(agriculturist)Host.professions.Add(5);
   var single=Dirt();var crop=PlantCrop(single);single.fertilizer.Value=speed;single.applySpeedIncreases(Host);int[] expected=crop.phaseDays.ToArray();
   var dual=Dirt();var second=PlantCrop(dual);FertilizerService.Write(dual,FertilizerRules.Add(FertilizerRules.Native("(O)919"),speed,0));dual.applySpeedIncreases(Host);
   Check(second.phaseDays.SequenceEqual(expected),"S03 actual native rounding and Agriculturist parity "+speed+agriculturist);
   var order=Dirt();var third=PlantCrop(order);Apply(order,speed);int[] before=third.phaseDays.ToArray();Apply(order,"(O)919");
   Check(third.phaseDays.SequenceEqual(before),"S03 adding quality after speed does not reapply native growth "+speed+agriculturist);
   third.fullyGrown.Value=true;third.dayOfCurrentPhase.Value=5;Config.EnableBsvSpeedGro=false;Config.EnableBsvSpeedGro=true;
   Check(third.dayOfCurrentPhase.Value==5,"S05 toggles do not change current countdown");
  }
  Host.professions.Clear();
  var soil=Dirt();Apply(soil,"(O)368");Apply(soil,"(O)465");
  var serializer=new XmlSerializer(typeof(HoeDirt));using var writer=new StringWriter(CultureInfo.InvariantCulture);serializer.Serialize(writer,soil);
  using var reader=new StringReader(writer.ToString());var loaded=(HoeDirt)serializer.Deserialize(reader)!;
  Check(FertilizerService.Read(loaded).Dual,"D03 actual native XML roundtrip preserves both slots");
 }
 static void Pump()
 {
  for(int i=0;i<6;i++){IsHost=true;Player(Host);Service.Network.Tick();IsHost=false;Player(Guest);Service.Network.Tick();}
  IsHost=true;Player(Host);
 }
 static void Transactions()
 {
  foreach(bool same in new[]{true,false})
  {
   var soil=Dirt();IsHost=false;Player(Guest);Guest.CurrentToolIndex=0;Guest.Items[0]=ItemRegistry.Create(same?"(O)368":"(O)465",5);
   soil.plant(Guest.ActiveObject.QualifiedItemId,Guest,true);
   Check(Guest.Items[0].Stack==5,"D08 client request does not consume before host approval");
   IsHost=true;Player(Host);Host.CurrentToolIndex=0;Host.Items[0]=ItemRegistry.Create("(O)368",5);soil.plant("(O)368",Host,true);
   Pump();
   Check(Host.Items[0].Stack==4&&Guest.Items[0].Stack==(same?5:4)&&FertilizerService.Read(soil).Dual==!same,"D08 ordered host reservation concurrent "+same);
   Check(!Guest.modData.ContainsKey(FertilizerNetwork.Pending)&&!soil.modData.ContainsKey(FertilizerNetwork.Reservation),"D08 completed/rejected leases retire");
  }
  {
   var soil=Dirt();IsHost=false;Player(Guest);Guest.CurrentToolIndex=0;Guest.Items[0]=ItemRegistry.Create("(O)465",5);soil.plant("(O)465",Guest,true);
   IsHost=true;Player(Host);Service.Network.Tick();Guest.Items[0]=null;IsHost=false;Player(Guest);Service.Network.Tick();Pump();
   Check(FertilizerService.Read(soil).Primary==""&&!Guest.modData.ContainsKey(FertilizerNetwork.Pending),"D08 material changed after approval cancels without soil mutation");
  }
  {
   var soil=Dirt();Apply(soil,"(O)368");IsHost=false;Player(Guest);Guest.CurrentToolIndex=0;Guest.Items[0]=ItemRegistry.Create("(O)465",5);soil.plant("(O)465",Guest,true);
   IsHost=true;Player(Host);Service.Network.Tick();IsHost=false;Player(Guest);Service.Network.Tick();Check(Guest.Items[0].Stack==4,"D08 approved remote escrow paid");
   // A real world replacement invalidates the reserved object after payment.
   IsHost=true;Player(Host);var replacement=Dirt();Pump();
   Check(Guest.Items[0].Stack==5&&!FertilizerService.Read(replacement).Dual&&!Guest.modData.ContainsKey(FertilizerNetwork.Pending),"D08 object replacement returns the paid item exactly once");
  }
  {
   var soil=Dirt();IsHost=false;Player(Guest);Guest.CurrentToolIndex=0;Guest.Items[0]=ItemRegistry.Create("(O)465",5);soil.plant("(O)465",Guest,true);
   IsHost=true;Player(Host);Service.Network.Tick();IsHost=false;Player(Guest);Service.Network.Tick();Check(Guest.Items[0].Stack==4,"D08 reconnect scenario escrows only after approval");
   IsHost=true;Player(Host);Recorder.PeerVersion=null;Service.Network.Tick();Recorder.PeerVersion=typeof(FertilizerService).Assembly.GetName().Version!.ToString(3);Pump();
   Check(Guest.Items[0].Stack==5&&FertilizerService.Read(soil).Primary=="","D08 disconnect after escrow rejects and refunds once on reconnect");Pump();Check(Guest.Items[0].Stack==5,"D08 repeated reconnect does not duplicate refund");
  }
 }
 static void Harvests()
 {
  IsHost=true;Player(Host);Config.EnableHarvestSeeds=false;Config.HarvestBonus.Enabled=true;Config.HarvestBonus.EnableFertilizerYieldBonus=false;
  Config.HarvestBonus.DoubleChance=1;Config.HarvestBonus.TripleChance=0;
  var soil=Dirt();var crop=PlantCrop(soil);var item=ItemRegistry.Create("(O)24",3,2);item.modData["OtherMod/LegitimateMetadata"]="retained";
  foreach(var who in new[]{Host,Guest}){who.Items.Clear();for(int i=0;i<36;i++)who.Items.Add(null);}
  string id=crop.modData[FertilizerService.CropKey];
  Service.Harvests.Submit(crop,soil,Host,new[]{item},id+"/0","472",false,0,7,null,soil.Tile*64,false);
  Check(Host.Items.Where(i=>i!=null&&i.QualifiedItemId=="(O)24").Sum(i=>i.Stack)==3,"H04 only extra portion of native multi-output stack delivered");
  var reward=Host.Items.First(i=>i!=null);Check(reward.Quality==2&&reward.modData["OtherMod/LegitimateMetadata"]=="retained","H04 legal quality and metadata preserved through host receipt");
  string seedBefore=Host.Items.Count(i=>i?.QualifiedItemId=="(O)472").ToString();
  IsHost=false;Player(Guest);Service.Harvests.Submit(crop,soil,Guest,new[]{item},id+"/0","472",false,0,7,null,soil.Tile*64,false);
  IsHost=true;Player(Host);Service.Harvests.Tick();IsHost=false;Player(Guest);Service.Harvests.Tick();
  Check(!Guest.Items.Any(i=>i!=null),"H05 simultaneous second peer receives no duplicate crop reward");
  IsHost=true;Player(Host);Service.Harvests.Submit(crop,soil,Host,new[]{item},id+"/1","472",false,0,7,null,soil.Tile*64,false);
  Check(Host.Items.Where(i=>i?.QualifiedItemId=="(O)24").Sum(i=>i.Stack)==6,"H05 next generation at same tile earns its own bonus");
  Config.EnableHarvestSeeds=true;Config.SeedChance=1;Config.HarvestBonus.Enabled=false;
  Service.Harvests.Submit(crop,soil,Host,new[]{item},id+"/2","472",true,1,7,null,soil.Tile*64,false);
  Check(Host.Items.Where(i=>i?.QualifiedItemId=="(O)24").Sum(i=>i.Stack)==6&&Host.Items.Where(i=>i?.QualifiedItemId=="(O)472").Sum(i=>i.Stack)==1,"H03/H04 Extra OFF leaves one independent seed, never multiplies it");
  Config.EnableHarvestSeeds=false;Config.HarvestBonus=new();
 }
}

using BetterStardewValley;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Machines;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using System.Reflection.Emit;
using O = StardewValley.Object;
using R = BetterStardewValley.TeaAppleRules;

class Program
{
 static int assertions; static readonly List<string> scenarios=new();static readonly Fixture f=new();
 static void Check(bool test,string message){assertions++;if(!test)throw new Exception(message);}
 static void Run(string name,Action test){Reset();test();scenarios.Add(name);}
 static void Reset(){Context.IsMainPlayer=true;Context.IsWorldReady=true;Context.IsMultiplayer=false;Game1.season=Season.Spring;Game1.Date.TotalDays=100;Game1.stats.DaysPlayed=100;Game1.timeOfDay=600;Game1.currentGameTime=new(TimeSpan.FromMilliseconds(1000),TimeSpan.Zero);Game1.PassTime=true;Game1.eventUp=false;Game1.Messages.Clear();Game1.Drops.Clear();Game1.Players.Clear();Utility.Locations.Clear();Game1.player=Player(1);Game1.currentLocation=Game1.player.currentLocation;Utility.Locations.Add(Game1.currentLocation);}
 static Farmer Player(long id){var who=new Farmer{UniqueMultiplayerID=id};var home=new FarmHouse{owner=who,upgradeLevel=1};who.Home=home;who.currentLocation=home;Game1.Players[id]=who;return who;}
 static Bush Plant(GameLocation? location=null){var b=new Bush{Location=location??Game1.currentLocation,Tile=new Vector2(2,2)};b.datePlanted.Value=(int)Game1.stats.DaysPlayed;TeaFeature.TestNew(b);b.Location.terrainFeatures[b.Tile]=b;return b;}
 static O Apple(int stack=5)=>new(){QualifiedItemId=R.GoldenApple,Stack=stack};
 static void Eat(GoldenAppleFeature feature,O apple){Check(GoldenAppleFeature.TestEat(Game1.player,apple),"first accepted");GoldenAppleFeature.TestDone(Game1.player);Check(Game1.player.hasBuff(R.Tenacity),"HUD buff applied");}
 static void Main(string[] args)
 {
  Run("original object identities, original green tea and leaf fields retained",()=>{
   var leaves=new ObjectData{Name="Tea Leaves",Price=50,Category=-75,Edibility=-300};var green=new ObjectData{Name="Green Tea",Price=100,Edibility=13,Category=-26};
   var data=new Dictionary<string,ObjectData>{{"815",leaves},{"251",new()},{"614",green}};string old=JsonConvert.SerializeObject(green),leaf=JsonConvert.SerializeObject(leaves);
   TeaAppleData.Objects(data,k=>k);Check(JsonConvert.SerializeObject(green)==old,"green tea byte-equivalent data");Check(leaves.Price==80,"leaf price80");leaves.Price=50;Check(JsonConvert.SerializeObject(leaves)==leaf,"all other leaf fields preserved");
   Check(data[R.CupId].Category==-7&&data[R.CupId].IsDrink==true,"cup separate drink");Check(data[R.CupId].Buffs==null||data[R.CupId].Buffs.Count==0,"no stat buffs");Check(data[R.CakeId].Price==750&&data[R.CakeId].Category==-26,"cake artisan");Check((int)(data[R.CakeId].Price*1.4)==1050,"artisan sale");Check(data[R.AppleId].ExcludeFromRandomSale,"not random sale");
  });
  Run("Pierre only, 400g, unlimited stock, all spring/summer, player unlock",()=>{
   var other=new ShopData{Items=new(){new(){ItemId="251",Price=777}}};var shops=new Dictionary<string,ShopData>{{"SeedShop",new(){Items=new(){new(){ItemId="251"},new(){ItemId="301"}}}},{"Other",other}};
   TeaAppleData.Shops(shops);TeaAppleData.Shops(shops);var item=shops["SeedShop"].Items.Single(x=>x.ItemId==R.Sapling);
   Check(item.Price==400&&item.AvailableStock==-1,"400 unlimited");Check(item.Condition=="SEASON spring summer, "+R.SeasonQuery+", PLAYER_HAS_MAIL Current "+R.TeaUnlock,"full season personal condition");Check(item.IgnoreShopPriceModifiers==true&&item.ApplyProfitMargins==false&&item.UseObjectDataPrice==false,"fixed price");Check(other.Items[0].Price==777,"other shops unchanged");Check(shops["SeedShop"].Items.Count==2,"idempotent, other listing retained");
  });
  Run("Pierre Missing Stocklist cannot bypass BSV seasonal availability",()=>{
   _=new TeaFeature(f,f);for(int season=0;season<4;season++){Game1.season=(Season)season;Check(GameStateQuery.Queries[R.SeasonQuery](Array.Empty<string>(),new object())==(season<2),"custom non-SEASON guard");}
  });
  Run("Caroline native event retained with one localized final line",()=>{
   string key=R.CarolineEvent+"/f Caroline 500/t 900 1700/w sunny";var events=new Dictionary<string,string>{{key,"music/actors/speak Caroline \"Original\"/end"},{"other","unchanged"}};
   TeaAppleData.Events(events,k=>"Pierre sells tea saplings.");TeaAppleData.Events(events,k=>"Pierre sells tea saplings.");Check(events[key].Split("Pierre sells").Length==2,"one added line");Check(events[key].EndsWith("/end")&&events[key].Contains("Original"),"native script retained");Check(events.ContainsKey(key)&&events["other"]=="unchanged","native conditions and other event unchanged");
   {var native=new Dictionary<string,string>{{"CarolineTea","Native %item craftingRecipe Tea_Sapling %%[#]Caroline's Tea Note"}};TeaAppleData.Mail(native,k=>k);
    Check(!native["CarolineTea"].Contains("craftingRecipe")&&native["CarolineTea"].EndsWith("[#]Caroline's Tea Note"),"game's own CarolineTea key is rewritten");}
   foreach(string reward in new[]{"Tea Sapling","Tea_Sapling"}){
    string foreign="Foreign NPC story %item craftingRecipe "+reward+" %%[#]Foreign title";
    var mail=new Dictionary<string,string>{{"carolineTea","Original %item craftingRecipe "+reward+" %% %item money 100 100 %%[#]Original title"},{"other",foreign},{"carolineTeaUnderscore",foreign}};
    TeaAppleData.Mail(mail,k=>k);var once=mail["carolineTea"];TeaAppleData.Mail(mail,k=>k);
    Check(!once.Contains("craftingRecipe")&&once.Contains("Original")&&once.Contains("money 100 100")&&once.EndsWith("[#]Original title"),"only native tea attachment changes; patched story/title/other rewards retained");
    Check(mail["other"]==foreign&&mail["carolineTeaUnderscore"]==foreign,"foreign tea-recipe letters remain byte-for-byte intact");
    Check(mail["carolineTea"]==once,"native tea letter change is idempotent");
   }
  });
  Run("old saves and skipped completed event unlock once per player",()=>{
   var tea=new TeaFeature(f,f);Game1.player.craftingRecipes["Tea Sapling"]=10;tea.TestUnlock();Check(!Game1.player.craftingRecipes.ContainsKey("Tea Sapling"),"legacy crafting removed");Check(!Game1.player.mailReceived.Contains(R.TeaUnlock),"two hearts alone don't bypass native event");Game1.player.eventsSeen.Add(R.CarolineEvent);Game1.eventUp=true;tea.TestUnlock();Check(!Game1.player.mailReceived.Contains(R.TeaUnlock),"wait for end");Game1.eventUp=false;tea.TestUnlock();tea.TestUnlock();Check(Game1.player.craftingRecipes.ContainsKey(R.CupId)&&Game1.player.mailReceived.Contains(R.TeaUnlock),"unlock");Check(Game1.Messages.Count==1,"one notice");Game1.player=Player(2);tea.TestUnlock();Check(!Game1.player.mailReceived.Contains(R.TeaUnlock),"no cross-player unlock");
  });
  Run("recipes use separate crafting/cooking tables and exact ingredients",()=>{
   var craft=new Dictionary<string,string>{{"Tea Sapling","old"},{"Native","unchanged"}};var cook=new Dictionary<string,string>();TeaAppleData.Crafting(craft,k=>k);TeaAppleData.Cooking(cook,k=>k);
   Check(!craft.ContainsKey("Tea Sapling")&&!craft.ContainsKey(R.AppleId),"no inventory apple or sapling");Check(craft[R.CupId].StartsWith("815 1/"),"one leaf cup");Check(cook[R.AppleId].StartsWith("613 1 336 3/"),"apple3gold");Check(craft["Native"]=="unchanged","native recipe retained");
  });
  Run("dehydrator 5 leaves two days one normal cake, other machine rules unchanged",()=>{
   var native=new MachineOutputRule{Id="Fruit"};var jar=new MachineData{OutputRules=new(){new(){Id="Vegetables"}}};var keg=new MachineData{OutputRules=new(){new(){Id="GreenTea"}}};var m=new Dictionary<string,MachineData>{{R.Dehydrator,new(){OutputRules=new(){native}}},{"(BC)15",jar},{"(BC)12",keg}};
   string before=JsonConvert.SerializeObject(new[]{jar,keg});TeaAppleData.Machines(m);TeaAppleData.Machines(m);var rule=m[R.Dehydrator].OutputRules[0];Check(rule.DaysUntilReady==2&&rule.MinutesUntilReady==-1,"native day timer");Check(rule.Triggers.Single().RequiredCount==5&&rule.Triggers[0].RequiredItemId==R.Leaves,"5 original leaves");Check(rule.Triggers[0].Condition.Contains(R.TeaUnlock),"knowledge gate");var o=rule.OutputItem.Single();Check(o.ItemId==R.Cake&&o.Quality==0&&!o.CopyQuality&&o.MinStack==1&&o.MaxStack==1,"one no-star cake");Check(m[R.Dehydrator].OutputRules.Count==2&&ReferenceEquals(m[R.Dehydrator].OutputRules[1],native),"original dehydrator rules");Check(JsonConvert.SerializeObject(new[]{jar,keg})==before,"jar/keg unchanged");
  });
  Run("15 watered days, dry days stall, two watered days per leaf",()=>{
   var p=new R.Plant(0,0,false,false,0);
   for(int day=1;day<=15;day++){p=R.Advance(p,day,0,day-1);Check(p.Growth==day&&p.Ready==(day==15),"growth boundary");}
   p=R.Harvest(p);p=R.Advance(p,16,0,14);Check(!p.Ready&&p.Regrow==2,"dry mature plant stalled");p=R.Advance(p,17,0,16);Check(!p.Ready&&p.Regrow==1,"first watered day");p=R.Advance(p,18,0,17);Check(p.Ready,"second watered day");
   var before=p;p=R.Advance(p,18,0,17);Check(p==before,"same day can't advance twice");Check(R.Advance(new(0,0,false,false,0),1,0,1).Growth==0,"today water cannot grow yesterday");
  });
  Run("full-season regrowth and all global season transitions",()=>{
   var p=new R.Plant(15,0,true,false,0);int leaves=0;
   for(int day=1;day<=56;day++){if(p.Ready){p=R.Harvest(p);leaves++;}p=R.Advance(p,day,day<29?0:1,day-1);}
   Check(leaves==28,"28 mature harvests over56days");Check(R.Advance(new(15,0,true,false,28),29,1,28).Ready,"ready leaf survives spring28summer1");p=R.Advance(p,57,2,56);Check(p.Dead&&!p.Ready,"fall permanent death");for(int season=0;season<4;season++)Check(R.Advance(p,113+season,season,112+season).Dead,"no seasonal resurrection");
  });
  Run("host advances shared plant once; manual water and native sprinklers",()=>{
   _=new TeaFeature(f,f);var b=Plant();TeaFeature.TestTool(b,new WateringCan());Game1.Date.TotalDays++;Context.IsMainPlayer=false;TeaFeature.TestDay(b);Check(TeaFeature.Read(b).Growth==0,"farmhand doesn't advance");Context.IsMainPlayer=true;TeaFeature.TestDay(b);Check(TeaFeature.Read(b).Growth==1,"host advances");Game1.player=Player(2);TeaFeature.TestDay(b);Check(TeaFeature.Read(b).Growth==1,"visitor cannot doublecount");var sprinkler=new O{Location=b.Location};TeaFeature.TestSprinkler(sprinkler,b.Tile);Check(TeaFeature.Get(b,R.WaterKey)==101,"sprinkler marked water");Game1.Date.TotalDays++;TeaFeature.TestDay(b);Check(TeaFeature.Read(b).Growth==2,"sprinkler grows");b.Location.NoSprinklers=true;TeaFeature.TestSprinkler(sprinkler,b.Tile);Check(TeaFeature.Get(b,R.WaterKey)==101,"native NoSprinklers respected");
  });
  Run("rain-before-pot-update preserves yesterday, not today's rain",()=>{
   _=new TeaFeature(f,f);var b=Plant();var pot=new IndoorPot{Location=b.Location};pot.bush.Value=b;Game1.Date.TotalDays=101;TeaFeature.TestPotWater(pot);TeaFeature.TestDay(b);Check(TeaFeature.Read(b).Growth==0,"dry yesterday no extra growth");Game1.Date.TotalDays=102;TeaFeature.TestPotWater(pot);TeaFeature.TestDay(b);Check(TeaFeature.Read(b).Growth==1,"yesterday rain remembered");TeaFeature.TestPotWater(pot);Check(TeaFeature.Get(b,R.PreviousWaterKey)==101,"duplicate water preserves previous date");
  });
  Run("native bushes and pots migrate in place, Sunroom protected",()=>{
   var tea=new TeaFeature(f,f);var old=new Bush{Location=Game1.currentLocation};old.datePlanted.Value=80;old.tileSheetOffset.Value=1;Game1.currentLocation.terrainFeatures[Vector2.Zero]=old;var room=new GameLocation{Name="Sunroom"};var npc=new Bush{Location=room};room.terrainFeatures[Vector2.Zero]=npc;Utility.Locations.Add(room);tea.TestWorld();Check(TeaFeature.Read(old).Growth==15&&TeaFeature.Read(old).Ready,"old mature ready leaf retained");Check(!npc.modData.ContainsKey(R.VersionKey),"NPC event bush untouched");Check(ReferenceEquals(Game1.currentLocation.terrainFeatures[Vector2.Zero],old),"same entity");
   for(int age=0;age<=25;age++)Check(R.MigratedGrowth(age)==Math.Min(20,age)*15/20,"legacy growth fraction");
   Game1.season=Season.Fall;tea.TestWorld();Check(TeaFeature.Read(old).Dead&&!TeaFeature.Read(old).Ready,"indoor fall kills");Check(TeaFeature.TestDeadAxe(old),"dead bush clears without native sapling refund");
  });
  Run("spring/summer planting only, pot probe has no notification",()=>{
   _=new TeaFeature(f,f);var seed=new O{QualifiedItemId=R.Sapling};for(int season=0;season<4;season++){Game1.season=(Season)season;Check(TeaFeature.TestPlant(seed)==(season<2),"season placement");int notices=Game1.Messages.Count;Check(TeaFeature.TestPotPlant(seed,true)==(season<2),"pot season");Check(notices==Game1.Messages.Count,"probe silent");}Check(TeaFeature.TestPlant(new O{QualifiedItemId="(O)301"}),"unrelated seed unaffected");
  });
  Run("planting preserves pre-watered soil and garden pots",()=>{
   _=new TeaFeature(f,f);var seed=new O{QualifiedItemId=R.Sapling};var l=Game1.currentLocation;l.terrainFeatures[Vector2.Zero]=new HoeDirt{Location=l,Watered=true};TeaFeature.TestPrewateredOutside(seed,l);var b=(Bush)l.terrainFeatures[Vector2.Zero];Check(TeaFeature.Get(b,R.WaterKey)==100,"wet soil kept");var pot=new IndoorPot{Location=l};pot.bush.Value=Plant(l);pot.hoeDirt.Value.Watered=true;TeaFeature.TestPrewateredPot(pot,seed);Check(TeaFeature.Get(pot.bush.Value,R.WaterKey)==100,"wet pot kept");
  });
  Run("network lock rechecks ready flag and produces one original normal leaf",()=>{
   _=new TeaFeature(f,f);var b=Plant();TeaFeature.Write(b,new(15,0,true,false,100));b.uniqueSpawnMutex.Deferred=true;TeaFeature.TestHarvest(b);Game1.player=Player(2);TeaFeature.TestHarvest(b);Check(Game1.Drops.Count==0,"wait for mutex");b.uniqueSpawnMutex.Flush();Check(Game1.Drops.Count==1&&Game1.Drops[0].Item1==R.Leaves,"one original leaf");Check(Game1.Drops[0].Item2==1,"original picker captured");Check(TeaFeature.Read(b).Regrow==2&&!TeaFeature.Read(b).Ready,"one reset");Check(b.uniqueSpawnMutex.Released==2,"all locks released");TeaFeature.TestTick(b);Check(b.NeedsUpdate&&b.uniqueSpawnMutex.Updates==1,"mutex keeps ticking");
  });
  Run("season reset cannot erase ready leaves and dead sprite stays dead",()=>{
   _=new TeaFeature(f,f);var b=Plant();TeaFeature.Write(b,new(15,0,true,false,100));Game1.season=Season.Summer;TeaFeature.TestSeason(b);Check(TeaFeature.Read(b).Ready,"summer ready");Game1.season=Season.Fall;TeaFeature.TestSeason(b);Check(TeaFeature.Read(b).Dead,"fall dead");Check(TeaFeature.TestDeadSprite(b)==new Rectangle(96,288,16,32),"native winter sprite");Game1.season=Season.Spring;TeaFeature.TestSeason(b);Check(TeaFeature.Read(b).Dead,"permanent");
  });
  Run("cup32/14 apple38/18 recovery; new crafts always normal",()=>{
   _=new GoldenAppleFeature(f,f);Check(GoldenAppleFeature.TestRecovery(new O{QualifiedItemId=R.Cup,Quality=4})==(32,14),"cup exact");Check(GoldenAppleFeature.TestRecovery(Apple())==(38,18),"apple exact");Check(GoldenAppleFeature.TestRecovery(new O{QualifiedItemId="(O)614"})==(77,88),"green tea untouched");foreach(string id in new[]{R.Cup,R.GoldenApple}){var o=new O{QualifiedItemId=id,Quality=4};GoldenAppleFeature.TestQuality(o);Check(o.Quality==0,"no-star new craft");}var native=new O{QualifiedItemId="(O)613",Quality=4};GoldenAppleFeature.TestQuality(native);Check(native.Quality==4,"native quality unchanged");
  });
  Run("only own upgraded kitchen fridge context, not Cookout Kit or inventory",()=>{
   var appleFeature=new GoldenAppleFeature(f,f);var who=Game1.player;appleFeature.TestReconcile();Check(who.cookingRecipes.ContainsKey(R.AppleId),"upgraded house unlock");var page=new CraftingPage{cooking=true,_materialContainers=new(){who.Home.Fridge.Items}};var c=new ClickableTextureComponent();page.pagesOfCraftingRecipes.Add(new(){{c,new(){name=R.AppleId}}});Check(GoldenAppleFeature.TestCraft(page,c),"own kitchen");page._materialContainers=null;Check(!GoldenAppleFeature.TestCraft(page,c),"cookout blocked even inside house");page._materialContainers=new(){who.Home.Fridge.Items};page.cooking=false;Check(!GoldenAppleFeature.TestCraft(page,c),"inventory blocked");page.cooking=true;who.Home.upgradeLevel=0;Check(!GoldenAppleFeature.TestCraft(page,c),"unupgraded blocked");who.Home.upgradeLevel=1;who.currentLocation=new FarmHouse();Check(!GoldenAppleFeature.TestCraft(page,c),"other house blocked");Check(GoldenAppleFeature.SeasoningQuality(Apple())==-1,"apple bypasses seasoning");Check(GoldenAppleFeature.SeasoningQuality(new O{Quality=0})==0,"native food still seasons");Check(Game1.Messages.Contains("apple.kitchen-required"),"clear notice");
  });
  Run("first consumption reserves day immediately; second consumes nothing",()=>{
   var feature=new GoldenAppleFeature(f,f);var a=Apple();Check(GoldenAppleFeature.TestEat(Game1.player,a),"first accepted");Check(a.Stack==4,"one consumed");Check(!GoldenAppleFeature.TestEat(Game1.player,a)&&a.Stack==4,"second during animation blocked");feature.TestReconcile();Check(Game1.player.hasBuff(R.Tenacity),"only one buff");Check(Game1.player.buffs.AppliedBuffs.Count==1,"not stacked");Check(Game1.Messages.Contains("apple.already-eaten"),"daily notice");
  });
  Run("lethal1HP 1500ms invincibility removes icon and sends trigger notice",()=>{
   var feature=new GoldenAppleFeature(f,f);Eat(feature,Apple());Game1.player.health=30;GoldenAppleFeature.TestDamage(Game1.player,0);Check(Game1.player.health==1,"one HP");Check(Game1.player.temporarilyInvincible&&Game1.player.currentTemporaryInvincibilityDuration==1500,"1500ms");Check(!Game1.player.hasBuff(R.Tenacity)&&!GoldenAppleFeature.Armed(Game1.player),"spent immediately");Check(Game1.Messages.Contains("tenacity.triggered"),"clear HUD");Check(GoldenAppleFeature.TestTint(Game1.player)!=Color.White&&GoldenAppleFeature.TestDraw(Game1.player),"gold and no flashing");Check(!GoldenAppleFeature.TestEat(Game1.player,Apple()),"spent still daily-limited");Check(!GoldenAppleFeature.TestDamageable(Game1.player),"protected window");
   feature.TestTick();for(int i=1;i<15;i++){Game1.currentGameTime=new(TimeSpan.FromMilliseconds(1000+i*100),TimeSpan.FromMilliseconds(100));feature.TestTick();feature.TestTick();Check(GoldenAppleFeature.TestTint(Game1.player)!=Color.White,"full tint and duplicate screen tick safe");}
   Game1.PassTime=false;Game1.currentGameTime=new(TimeSpan.FromMilliseconds(2500),TimeSpan.FromMilliseconds(100));feature.TestTick();Check(GoldenAppleFeature.Active(Game1.player),"pause doesn't spend window");Game1.PassTime=true;Game1.currentGameTime=new(TimeSpan.FromMilliseconds(2600),TimeSpan.FromMilliseconds(100));feature.TestTick();Check(!GoldenAppleFeature.Active(Game1.player)&&GoldenAppleFeature.TestTint(Game1.player)==Color.White,"expires at1500 active ms");Check(!Game1.player.temporarilyInvincible,"native invincibility ends even if vanilla timer stalled");GoldenAppleFeature.TestDamage(Game1.player,0);Check(Game1.player.health==0,"no second save");
  });
  Run("nonlethal damage preserves buff; defense boundary zero is lethal",()=>{
   var feature=new GoldenAppleFeature(f,f);Eat(feature,Apple());GoldenAppleFeature.TestDamage(Game1.player,1);Check(Game1.player.health==1&&GoldenAppleFeature.Armed(Game1.player),"surviving hit retains buff");GoldenAppleFeature.TestDamage(Game1.player,-20);Check(Game1.player.health==1&&!GoldenAppleFeature.Armed(Game1.player),"negative lethal intercepted");Check(!R.Lethal(0,-1,true),"never resurrect dead player");
  });
  Run("2AM expiry and next-day reset, regardless of trigger",()=>{
   var feature=new GoldenAppleFeature(f,f);var a=Apple();Eat(feature,a);Game1.timeOfDay=2550;feature.TestReconcile();Check(GoldenAppleFeature.Armed(Game1.player),"active before2AM");Game1.timeOfDay=2600;feature.TestReconcile();Check(!Game1.player.hasBuff(R.Tenacity)&&!GoldenAppleFeature.Armed(Game1.player),"expires unused");Check(!GoldenAppleFeature.TestEat(Game1.player,a)&&a.Stack==4,"2AM second blocked no refund");Game1.Date.TotalDays++;Game1.timeOfDay=600;feature.TestReconcile();Check(GoldenAppleFeature.TestEat(Game1.player,a)&&a.Stack==3,"new day allows one");
  });
  Run("save/reconnect retains daily allowance and armed or spent state",()=>{
   var feature=new GoldenAppleFeature(f,f);Eat(feature,Apple());var ledger=JsonConvert.SerializeObject(Game1.player.modData);Game1.player.modData=JsonConvert.DeserializeObject<ModData>(ledger)!;Game1.player.buffs=new();feature=new GoldenAppleFeature(f,f);feature.TestReconcile();Check(Game1.player.hasBuff(R.Tenacity),"armed restored");Check(!GoldenAppleFeature.TestEat(Game1.player,Apple()),"reconnect can't re-eat");GoldenAppleFeature.TestDamage(Game1.player,0);ledger=JsonConvert.SerializeObject(Game1.player.modData);Game1.player.modData=JsonConvert.DeserializeObject<ModData>(ledger)!;feature=new GoldenAppleFeature(f,f);feature.TestReconcile();Check(!GoldenAppleFeature.Armed(Game1.player)&&!GoldenAppleFeature.TestEat(Game1.player,Apple()),"spent state persists");
  });
  Run("multiplayer ledgers and effects isolated; visual sender validated",()=>{
   var feature=new GoldenAppleFeature(f,f);Farmer a=Game1.player;Eat(feature,Apple());Farmer b=Player(2);Game1.player=b;Eat(feature,Apple());GoldenAppleFeature.TestDamage(b,0);Check(GoldenAppleFeature.Armed(a)&&!GoldenAppleFeature.Armed(b),"independent buffs");Check(!GoldenAppleFeature.Active(a)&&GoldenAppleFeature.Active(b),"independent windows");Farmer remote=Player(3);remote.IsLocalPlayer=false;Context.IsMultiplayer=true;
   var n=new GoldenAppleFeature.TriggerNotice{Farmer=3,Token=Guid.NewGuid().ToString("N")};var e=new ModMessageReceivedEventArgs{FromModID=R.Prefix,Type=R.TriggerMessage,FromPlayerID=2,Body=n};feature.TestNotice(e);Check(!GoldenAppleFeature.Active(remote),"forged sender rejected");e.FromPlayerID=3;feature.TestNotice(e);Check(GoldenAppleFeature.Active(remote)&&remote.health==100&&remote.modData.Count==0,"remote message only visual");Check(GoldenAppleFeature.TestDamageable(remote),"remote visual packet cannot grant damage protection");
  });
  Run("scoped transpilers preserve native operations and fail on unknown shapes",()=>{
   var field=AccessTools.Field(typeof(Farmer),"health");var max=typeof(Math).GetMethod("Max",new[]{typeof(int),typeof(int)})!;
   var code=new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Sub),new CodeInstruction(OpCodes.Call,max),new CodeInstruction(OpCodes.Stfld,field),new CodeInstruction(OpCodes.Stfld,field)};
   var result=GoldenAppleFeature.DamageTranspiler(code).ToArray();Check(result[3].opcode==OpCodes.Call&&result[4].opcode==OpCodes.Stfld,"only damage not healing");bool failed=false;try{GoldenAppleFeature.DamageTranspiler(Array.Empty<CodeInstruction>()).ToArray();}catch(InvalidOperationException){failed=true;}Check(failed,"unknown damage fails closed");
   var quality=new[]{new CodeInstruction(OpCodes.Callvirt,AccessTools.PropertyGetter(typeof(Item),"Quality"))};Check(GoldenAppleFeature.SeasoningTranspiler(quality).Single().opcode==OpCodes.Call,"scoped quality guard");
  });
  Console.WriteLine(JsonConvert.SerializeObject(new{suite="Tea and Golden Apple production adapters + real GameData + rules",passed=true,assertions,scenarios,live_game_tested=false},Formatting.Indented));
 }
}

#pragma warning disable CS0067
class Fixture:IModHelper,IEvents,IContentEvents,IGameEvents,IMultiplayerEvents,ITranslationHelper,IGameContentHelper,ICommandHelper,IMultiplayerHelper,IMonitor
{
 public IEvents Events=>this;public IContentEvents Content=>this;public IGameEvents GameLoop=>this;IMultiplayerEvents IEvents.Multiplayer=>this;
 public ITranslationHelper Translation=>this;public IGameContentHelper GameContent=>this;public ICommandHelper ConsoleCommands=>this;public IMultiplayerHelper Multiplayer=>this;
 public event EventHandler<AssetRequestedEventArgs>? AssetRequested;public event EventHandler<EventArgs>? LocaleChanged,SaveLoaded,DayStarted,OneSecondUpdateTicked,TimeChanged,ReturnedToTitle;public event EventHandler<UpdateTickedEventArgs>? UpdateTicked;public event EventHandler<ModMessageReceivedEventArgs>? ModMessageReceived;
 public Translation Get(string key)=>new(){Text=key};public void InvalidateCache(string s){}public T Load<T>(string s)=>default!;
 public void Add(string s,string help,Action<string,string[]> action){}public void SendMessage<T>(T value,string type,string[] mods){}public void Log(string s,LogLevel l){}
}

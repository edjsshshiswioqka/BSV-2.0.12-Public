using BetterStardewValley;
using BetterStardewValley.GiantLobster;
using BetterStardewValley.MutantLobsterKing;
using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Buildings;
using StardewValley.GameData.Machines;
using StardewValley.Inventories;
using xTile;
using xTile.Layers;
using xTile.Tiles;
using xTile.Dimensions;
using SObject=StardewValley.Object;
using MachineMod=Eddie.FishingMachine.ModEntry;

internal static class MutantChecks
{
    private static bool ErrorName(ref string __result){__result="Headless error-item label";return false;}
    private sealed class Fixed:Random {internal int Calls;readonly double Value;internal Fixed(double v)=>Value=v;public override double NextDouble(){Calls++;return Value;}}
    internal static Sewer Fixture()
    {
        var l=new Sewer();l.name.Value="Sewer";var map=new Map("SewerFixture");
        var sheet=new TileSheet("fixture",map,"not-rendered",new Size(2,2),new Size(16,16));map.AddTileSheet(sheet);
        foreach(string layer in new[]{"Back","Buildings","Front"})map.AddLayer(new Layer(layer,map,new Size(12,12),new Size(64,64)));
        var back=map.GetLayer("Back");
        for(int x=0;x<12;x++)for(int y=0;y<12;y++)
        {back.Tiles[x,y]=new StaticTile(back,sheet,BlendMode.Alpha,0);if(x>=3&&x<=8&&y>=3&&y<=8)back.Tiles[x,y].Properties["Water"]="T";}
        l.map=map;return l;
    }
    internal static object Run(Farmer player,Dictionary<string,string> fish,Action<bool,string> check)
    {
        Console.Error.WriteLine("MUTANT data/usage/placement");
        if(Game1.getLocationFromName("Farm") is not Farm){var farm=new Farm();farm.name.Value="Farm";Game1.game1._locations.Add(farm);Game1._locationLookup.Clear();}
        Game1.getFarm().modData[MutantWildQuota.Key]=System.Text.Json.JsonSerializer.Serialize(new MutantAnnualLedger());
        // Generic category misses resolve an error-item label; graphics/content pack isn't installed here.
        new Harmony("BSV.Mutant.ErrorLabelFixture").Patch(DrawPatchTargets.Require(typeof(ItemRegistry),"GetErrorItemName",typeof(string),typeof(string)),prefix:new(typeof(MutantChecks),nameof(ErrorName)));
        Game1.objectData.TryAdd("715",new(){Name="Lobster",DisplayName="Lobster",Description="Fixture",Type="Fish",Category=-4,Edibility=5});
        Game1.objectData.TryAdd("716",new(){Name="Crayfish",DisplayName="Crayfish",Description="Fixture",Type="Fish",Category=-4,Edibility=5});
        Game1.objectData.TryAdd("227",new(){Name="Sashimi",DisplayName="Sashimi",Description="Fixture",Type="Cooking",Category=-7,Edibility=30});
        Game1.objectData.TryAdd("142",new(){Name="Carp",DisplayName="Carp",Type="Fish",Category=-4});
        fish.TryAdd("142","Carp/15/mixed/1/20/600 2600/spring summer fall winter/both/0/0/1/1/0");
        foreach(string id in new[]{"710","168","169","170","171","172","685","774","DeluxeBait"})
            Game1.objectData.TryAdd(id,new(){Name=id,DisplayName=id,Description="Fixture",Type="Basic",Category=id is "685" or "774" or "DeluxeBait"?-21:0});
        fish.TryAdd("716","Crayfish/trap/.35/1/freshwater/1/20");
        CraftingRecipe.cookingRecipes??=new();
        MutantData.Objects(Game1.objectData,k=>k);MutantData.Objects(Game1.objectData,k=>k);
        var item=ItemRegistry.Create<SObject>(MutantLobsterRules.Item,4);item.modData["fixture/preserve"]="yes";
        player.professions.Clear();check(item.Price==50000&&item.sellToStorePrice(player.UniqueMultiplayerID)==50000&&item.Type=="Basic"&&item.Category==0,"collectible 50000 base, Basic category0");
        check(item.canBeShipped()&&item.Edibility==-300&&item.staminaRecoveredOnConsumption()<=0&&item.healthRecoveredOnConsumption()<=0,"shippable but inedible (native negative sentinel, not zero-edibility food)");
        foreach(int profession in Enumerable.Range(0,30))player.professions.Add(profession);
        check(item.sellToStorePrice(player.UniqueMultiplayerID)==50000,"no unsolicited fish/artisan profession multiplier");
        check((long)item.sellToStorePrice(player.UniqueMultiplayerID)*999==49950000,"stack value fits existing integer shipping amounts");
        var copy=(SObject)item.getOne();check(copy.modData["fixture/preserve"]=="yes"&&copy.canStackWith(item),"copy/stack preserves metadata");
        check(FishPond.GetRawData(MutantLobsterRules.Item)==null,"actual native fish_pond_ignore excludes even before admission guard");
        var table=new Furniture{Location=Fixture()};table.furniture_type.Value=11;
        check(table.performObjectDropInAction(item,true,player,false)&&item.Stack==4&&table.heldObject.Value==null,"native tabletop placement probe accepts collectible without consuming it (render/retrieval need live test)");
        check(!(bool)DrawPatchTargets.Require(typeof(FishPond),"isLegalFishForPonds",typeof(bool),typeof(string)).Invoke(new FishPond(),new object[]{MutantLobsterRules.Id})!,"exact pond admission rejected");
        check(!fish.ContainsKey(MutantLobsterRules.Id),"no trap/rod Data Fish registration");
        var injected=new Dictionary<string,string>(fish){[MutantLobsterRules.Id]="Injected/trap/1/1/freshwater ocean/1/10"};
        check(!GiantLobsterRules.OrdinaryTrapData(injected).ContainsKey(MutantLobsterRules.Id)&&CrabRules.NativePool(injected,new HashSet<string>{"freshwater"}).All(p=>!MutantLobsterRules.IsItem(p.Id)),"exact exclusions survive malicious ordinary pool row");
        var machine=new SObject{Location=Fixture()};var md=new MachineData();int oldStack=item.Stack;
        foreach(bool probe in new[]{true,false})check(!machine.PlaceInMachine(md,item,probe,player,false,false)&&item.Stack==oldStack&&machine.heldObject.Value==null&&machine.MinutesUntilReady==0,"actual machine probe/commit rejects without consuming "+probe);
        check(!MachineDataUtility.CanApplyOutput(machine,new MachineOutputRule(),MachineOutputTrigger.ItemPlacedInMachine,item,player,machine.Location,out _,out _),"output selection used by automatic inputs rejects");
        var saveItems=player.Items.ToArray();player.Items.Clear();player.Items.Add(item);
        CraftingRecipe.cookingRecipes["MutantFixtureSashimi"]="-4 1/Home/227 1/none/Sashimi";
        var recipe=new CraftingRecipe("MutantFixtureSashimi",true);
        check(!recipe.doesFarmerHaveIngredientsInInventory(null)&&recipe.getCraftableCount((IList<Item>?)null)==0,"inventory generic fish availability/count excludes collectible");
        Console.Error.WriteLine("MUTANT fridge");var fridge=new Chest();fridge.Items.Add(item);player.Items.Clear();
        check(!recipe.doesFarmerHaveIngredientsInInventory(fridge.Items)&&recipe.getCraftableCount(new List<Chest>{fridge})==0,"fridge generic fish availability/count excludes collectible");
        Console.Error.WriteLine("MUTANT ordinary lobster");fridge.Items.Add(ItemRegistry.Create("(O)715",2));
        check(recipe.doesFarmerHaveIngredientsInInventory(fridge.Items)&&recipe.getCraftableCount(new List<Chest>{fridge})==2,"ordinary fish remains usable alongside collectible");
        recipe.consumeIngredients(new List<IInventory>{fridge.Items});
        check(item.Stack==4&&fridge.Items.Where(i=>i?.QualifiedItemId=="(O)715").Sum(i=>i.Stack)==1,"actual fridge ingredient deduction consumes ordinary fish only");
        CraftingRecipe.cookingRecipes["MutantFixtureExplicit"]=MutantLobsterRules.Id+" 1/Home/227 1/none/Forbidden fixture";
        var explicitRecipe=new CraftingRecipe("MutantFixtureExplicit",true);
        check(!explicitRecipe.doesFarmerHaveIngredientsInInventory(fridge.Items)&&explicitRecipe.getCraftableCount(new List<Chest>{fridge})==0,"explicit injected ingredient has consistent unavailable/count zero, not a free recipe");
        check(!CraftingRecipe.DoesFarmerHaveAdditionalIngredientsInInventory(new(){new(MutantLobsterRules.Id,1)},fridge.Items),"additional ingredient availability rejects exact collectible");
        check(UsagePolicy.ContainerCount(player,fridge.Items,MutantLobsterRules.Id)==0&&UsagePolicy.ContainerCount(player,fridge.Items,"715")==1,"recipe UI shared count helper excludes collectible and retains ordinary fish");
        check(player.getItemCountInList(fridge.Items,MutantLobsterRules.Id)==4,"non-recipe inventory queries unaffected");
        explicitRecipe.consumeIngredients(new List<IInventory>{fridge.Items});check(item.Stack==4,"explicit consumption guard preserves collectible");
        player.Items.Clear();foreach(var old in saveItems)player.Items.Add(old);
        Console.Error.WriteLine("MUTANT map");var l=Fixture();Game1.locationData["Sewer"]=new();player.currentLocation=l;
        player.Position=new Vector2(2*64,4*64);var world=new WorldService(null!); // geometric checks do not invoke the service host
        check(MutantService.IsSewer(l)&&MutantService.Eligible(l,new(4,4)),"actual Sewer type/name legal water qualifies");
        var foreign=new GameLocation();foreign.name.Value="Sewer";foreign.map=l.map;
        check(!MutantService.IsSewer(foreign),"name-only foreign map not original Sewer");
        check(world.CanPlace(l,new(4,4),false)&&world.CanPlace(l,new(4,4),true),"both actual placement methods accept synthetic complete water footprint");
        check(!world.CanPlace(l,new(11,11),true)&&!world.CanPlace(l,new(0,0),false),"boundary/dry placement rejected");
        l.objects[new(4,4)]=new SObject();check(!world.CanPlace(l,new(4,4),true),"occupied tile rejects entire footprint");l.objects.Clear();
        l.map.GetLayer("Back").Tiles[4,4].Properties["NoFishing"]="T";
        check(!MutantService.Eligible(l,new(4,4))&&!world.CanPlace(l,new(4,4),true),"NoFishing blocks capture and crab footprint");l.map.GetLayer("Back").Tiles[4,4].Properties.Remove("NoFishing");
        var habitats=l.GetCrabPotFishForTile(new(4,4));check(habitats.Contains("freshwater"),"actual native Sewer habitat falls back to freshwater with no area override");
        Game1.locationData["Sewer"].Fish=new(){new(){Id="fixtureCarp",ItemId="(O)142",Chance=1},new(){Id="fixtureForbidden",ItemId=MutantLobsterRules.Item,Chance=1}};
        check(FishPool.Get(l,new(4,4),Facing.Right,player).Select(p=>p.Id).SequenceEqual(new[]{"(O)142"}),"read-only actual fishing adapter uses loaded Sewer fixture Carp and excludes injected Mutant row");
        var pot=new CrabPot{Location=l,TileLocation=new(4,4)};
        foreach(string name in new[]{"Beach","Town","Mountain","Mine","MutantBugLair","Sewer"})
        {
            foreign.name.Value=name;pot.Location=foreign;pot.heldObject.Value=null;
            var r=new Fixed(0);LobsterCrabPots.SelectOrJunk(r,.2,pot,1,0);
            check(pot.heldObject.Value==null,"non-original Sewer identity cannot hit Mutant even at zero roll: "+name);
        }
        pot.Location=l;
        var priorSeason=Game1.season;
        foreach(var season in new[]{Season.Spring,Season.Summer,Season.Fall,Season.Winter})
        {
            Game1.season=season;pot.heldObject.Value=null;Game1.getFarm().modData[MutantWildQuota.Key]=System.Text.Json.JsonSerializer.Serialize(new MutantAnnualLedger());
            LobsterCrabPots.SelectOrJunk(new Fixed(0),.2,pot,9,0);
            check(pot.heldObject.Value?.QualifiedItemId==MutantLobsterRules.Item&&pot.heldObject.Value.Stack==1,
                "actual native Sewer selector allows one item in season "+season);
            var cfg=new CrabBalanceConfig();var bait=new[]{new CrabBait(0,"(O)685",150,false)};var pool=new[]{new CrabAquatic("(O)716",1)};
            var available=CrabRules.Begin(new(){Kind=MachineKinds.Crab},bait,pool,35,false,true,1000,146000,cfg,kingEligible:true,sewer:true);
            var used=CrabRules.Begin(new(){Kind=MachineKinds.Crab},bait,pool,35,false,true,1000,146000,cfg,kingEligible:false,sewer:true);
            check(available.Pending.SequenceEqual(used.Pending),"actual Sewer machine output independent of ordinary quota availability in "+season);
        }
        Game1.season=priorSeason;pot.heldObject.Value=null;
        player.stats.Set("Book_Crabbing",1);pot.heldObject.Value=item;
        check(PotCollection.Book(player.stats,"Book_Crabbing",pot)==0,"native harvest book guard disables doubling only protected output");
        pot.heldObject.Value=ItemRegistry.Create<SObject>("(O)715");check(PotCollection.Book(player.stats,"Book_Crabbing",pot)==1,"ordinary pot book effect preserved");
        var automatePath=System.IO.Path.Combine(AppContext.BaseDirectory,"Automate.dll");
        if(System.IO.File.Exists(automatePath))
        {
            var assembly=System.Reflection.Assembly.LoadFrom(automatePath);
            var type=assembly.GetType("Pathoschild.Stardew.Automate.Framework.Machines.Objects.CrabPotMachine",true)!;
            PotCollection.InstallAutomate(new Harmony("BSV.Mutant.AutomateFixture"),type);
            pot.heldObject.Value=ItemRegistry.Create<SObject>(MutantLobsterRules.Item,1,2);
            var wrapper=Activator.CreateInstance(type,pot,l,pot.TileLocation,(StardewModdingAPI.IMonitor)Recorder.Make(typeof(StardewModdingAPI.IMonitor)))!;
            var tracked=type.GetMethod("GetOutput")!.Invoke(wrapper,null)!;
            int count=(int)tracked.GetType().GetProperty("Count")!.GetValue(tracked)!;
            check(count==1&&pot.heldObject.Value.Stack==1,"actual Automate 2.6.1 GetOutput with crabbing book does not double Mutant");
        }
        pot.heldObject.Value=null;
        foreach(double u in new[]{0d,Math.BitDecrement(.0005),.0005,Math.BitIncrement(.0005),Math.BitDecrement(1d)})
        {
            Game1.getFarm().modData[MutantWildQuota.Key]=System.Text.Json.JsonSerializer.Serialize(new MutantAnnualLedger());
            var r=new Fixed(u);LobsterCrabPots.SelectOrJunk(r,0,pot,99,2);
            check((pot.heldObject.Value?.QualifiedItemId==MutantLobsterRules.Item)==(u<.0005),"actual pot selector boundary "+u.ToString("R"));
            if(u<.0005)check(pot.heldObject.Value!.Stack==1&&pot.heldObject.Value.Quality==2&&r.Calls==1,"single hit suppresses wild-bait quantity, retains quality, no second draw");
            pot.heldObject.Value=null;
        }
        NativeDayUpdate(pot,player,check);
        var native=Native(pot,player,check);
        Console.Error.WriteLine("MUTANT machine 10M+");var batches=Machines(check);
        foreach(var route in new[]{"fish","other","jelly","target"})
        {
            bool blocked=false;try{var result=Production.Begin(new BalanceConfig{BaitPerCycle=1},new[]{new BaitSpend(0,1,route=="target"?BaitKind.Targeted:BaitKind.Normal,route=="target"?MutantLobsterRules.Item:null)},new[]{new FishChoice(route=="fish"?MutantLobsterRules.Item:"(O)128",1)},new[]{route=="other"?MutantLobsterRules.Item:"(O)168"},1000,3,jelly:route=="jelly"?MutantLobsterRules.Item:null);blocked=result.Pending.All(p=>!MutantLobsterRules.IsItem(p.Id));}catch(ArgumentException){blocked=true;}
            check(blocked,"actual fishing kernel prevents collectible output in route: "+route);
        }
        return new{native,batches,fixture="real methods + synthetic map/content; not real Sewer map",live_game=false,multiplayer=false};
    }
    private static void NativeDayUpdate(CrabPot pot,Farmer player,Action<bool,string> check)
    {
        var h=new Harmony("BSV.Mutant.NativeDateFixture");
        h.Patch(DrawPatchTargets.Require(typeof(CrabPot),"DayUpdate",typeof(void)),transpiler:new HarmonyMethod(typeof(LobsterChecks).GetMethod("InjectDayRandom",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!));
        try
        {
            pot.owner.Value=player.UniqueMultiplayerID;player.professions.Clear();pot.bait.Value=null;pot.heldObject.Value=null;
            pot.DayUpdate();check(pot.heldObject.Value==null,"actual native no-bait gate prevents new output");
            var held=ItemRegistry.Create<SObject>(MutantLobsterRules.Item,1);pot.heldObject.Value=held;pot.bait.Value=ItemRegistry.Create<SObject>("(O)774");
            pot.DayUpdate();check(ReferenceEquals(pot.heldObject.Value,held)&&held.Stack==1,"actual native existing output not replaced or rerolled");
            int hits=0;
            foreach(string bait in new[]{"685","774","DeluxeBait"})
            {
                for(int i=0;i<10000;i++)
                {
                    pot.heldObject.Value=null;Game1.getFarm().modData[MutantWildQuota.Key]=System.Text.Json.JsonSerializer.Serialize(new MutantAnnualLedger());pot.bait.Value=ItemRegistry.Create<SObject>("(O)"+bait);pot.directionOffset.Value=new Vector2(i,0);pot.DayUpdate();
                    if(pot.heldObject.Value?.QualifiedItemId==MutantLobsterRules.Item)
                    {hits++;if(pot.heldObject.Value.Stack!=1||pot.heldObject.Value.Quality!=(bait=="DeluxeBait"?1:0))throw new Exception("Native bait quantity/quality regression");}
                }
            }
            check(hits>0,"30000 actual native DayUpdate fixtures hit new output with wild/deluxe/normal bait; output quantity one");
        }
        finally{h.UnpatchAll(h.Id);pot.heldObject.Value=null;pot.directionOffset.Value=Vector2.Zero;}
    }
    private static object Native(CrabPot pot,Farmer player,Action<bool,string> check)
    {
        Game1.getFarm().modData[MutantWildQuota.Key]=System.Text.Json.JsonSerializer.Serialize(new MutantAnnualLedger());
        const int n=250000;const int seed=146000;var results=new List<object>();
        foreach(string prof in new[]{"none","mariner","luremaster","all"})
        {
            Console.Error.WriteLine("MUTANT actual pot selector 250k independently available opportunities: "+prof);player.professions.Clear();
            if(prof=="mariner")player.professions.Add(10);if(prof=="luremaster")player.professions.Add(11);if(prof=="all")foreach(int x in Enumerable.Range(0,30))player.professions.Add(x);
            var r=new Random(seed+results.Count);int hits=0;
            for(int i=0;i<n;i++)
            {
                LobsterCrabPots.SelectOrJunk(r,prof is "mariner" or "all"?0:.2,pot,2,1);
                if(pot.heldObject.Value==null)continue;
                if(pot.heldObject.Value.QualifiedItemId!=MutantLobsterRules.Item||pot.heldObject.Value.Stack!=1)throw new Exception("Unexpected rare output");
                hits++;pot.heldObject.Value=null;Game1.getFarm().modData[MutantWildQuota.Key]=System.Text.Json.JsonSerializer.Serialize(new MutantAnnualLedger());
            }
            Rate(hits,n,check,"pot "+prof);results.Add(new{prof,seed=seed+results.Count,n,hits,rate=(double)hits/n});
        }
        return new{adapter="actual LobsterCrabPots.SelectOrJunk; original DayUpdate preconditions separately checked",total=4*n,results};
    }
    private static object Machines(Action<bool,string> check)
    {
        var pool=new[]{new CrabAquatic("(O)716",1)};var results=new List<object>();
        foreach(int level in new[]{1,2})foreach(bool deluxe in new[]{false,true})foreach(bool lure in new[]{false,true})foreach(bool book in new[]{false,true})
        {
            var config=new CrabBalanceConfig();int cost=config.Required(level,lure),rolls=cost*(lure?2:1),cycles=(640000+rolls-1)/rolls;
            var bait=new[]{new CrabBait(0,deluxe?"(O)DeluxeBait":"(O)685",cost,deluxe)};int hits=0,multi=0,seedStart=146000+results.Count*100000;
            for(int i=0;i<cycles;i++)
            {
                var s=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},bait,pool,35,lure,book,1000,seedStart+i,config,sewer:true);
                int selected=s.Pending.Where(p=>MutantLobsterRules.IsItem(p.Id)).Sum(p=>p.Count);hits+=selected;if(selected>1)multi++;
                if(!CrabRules.Valid(s)||s.Pending.Sum(p=>p.Count)!=s.Crab!.Rolls+s.Crab.MachineCrits+s.Crab.BookBonus)throw new Exception("Invalid batch or quantity statistics");
                if(i<3)check(ProductionState.Load(s.Save()).Pending.SequenceEqual(s.Pending),"stored Sewer batch never rerolls");
            }
            int n=cycles*rolls;Rate(hits,n,check,$"machine L{level}/{deluxe}/{lure}/{book}");check(multi>0,"independent multiple successes allowed per batch");
            results.Add(new{level,deluxe,lure,book,seedStart,cycles,n,hits,rate=(double)hits/n,multi});
        }
        var all=new CrabBalanceConfig{LevelOneCriticalChance=1,LevelTwoCriticalChance=1};bool two=false;
        for(int seed=0;seed<2000&&!two;seed++)
        {
            var r=MutantLobsterRules.RandomFor(seed);int expected=Enumerable.Range(0,150).Count(_=>MutantLobsterRules.Select(true,r.NextDouble()));if(expected<2)continue;
            var s=CrabRules.Begin(new(){Kind=MachineKinds.Crab},new[]{new CrabBait(0,"(O)685",150,false)},pool,35,false,true,1000,seed,all,sewer:true);
            check(s.Pending.Where(p=>MutantLobsterRules.IsItem(p.Id)).Sum(p=>p.Count)==expected,"all bonuses still exactly one per success, aggregated multiple successes retained");two=true;
        }
        check(two,"bounded deterministic multi-success seed located");
        return results;
    }
    private static void Rate(int hits,int n,Action<bool,string> check,string label)
    {double sd=Math.Sqrt(n*.0005*.9995);check(Math.Abs(hits-n*.0005)<6*sd,label+": E="+n+", hits="+hits+", prespecified 6-sigma binomial band");}
}

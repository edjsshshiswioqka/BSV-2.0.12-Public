using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using BetterStardewValley;
using BetterStardewValley.GiantLobster;
using BetterStardewValley.SpiritFish;
using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.GameData.Objects;
using StardewValley.Objects;
using StardewValley.Tools;
using SObject=StardewValley.Object;

internal static class LobsterChecks
{
    private const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    internal static object Run(Farmer player,Dictionary<string,string> fish,Action<bool,string> check)
    {
        var objects=Game1.objectData;
        foreach(var pair in new[]{("715","Lobster"),("716","Crayfish"),("717","Crab"),("718","Cockle"),("719","Mussel"),("720","Shrimp"),("721","Snail"),("722","Periwinkle"),("723","Oyster"),("372","Clam"),("128","Pufferfish")})
            objects[pair.Item1]=new(){Name=pair.Item2,DisplayName=pair.Item2,Type="Fish",Category=-4,Edibility=5};
        foreach(string id in new[]{"710","168","169","170","171","172","685","774","DeluxeBait","SpecificBait","ChallengeBait","256","266","192","250"})
            objects[id]=new(){Name=id,DisplayName=id,Type="Basic",Category=id is "685" or "774" or "DeluxeBait" or "SpecificBait" or "ChallengeBait"?-21:0};
        // Explicit ordered fixtures; never presented as the shipped Content XNB.
        fish["715"]="Lobster/trap/.05/1/ocean/2/20";fish["717"]="Crab/trap/.1/1/ocean/1/20";
        fish["723"]="Oyster/trap/.15/1/ocean/1/20";fish["716"]="Crayfish/trap/.35/1/freshwater/1/20";
        fish["128"]="Pufferfish/80/floater/1/20/600 2600/spring summer fall winter/both/0/0/1/1/0";
        LobsterData.Objects(objects,k=>k);LobsterData.Fish(fish);LobsterData.Fish(fish);
        var bait=ItemRegistry.Create<SObject>(GiantLobsterRules.Bait,7);
        check(bait.Category==-21&&ProductionService.Bait(bait,3)?.Kind==BaitKind.GiantLobster,"actual ItemRegistry bait category and machine whitelist accept exact new ID");
        check(ProductionService.Bait(ItemRegistry.Create("(O)685"),0)?.Kind==BaitKind.Normal,"ordinary bait retains old type");
        check((int)BaitKind.Normal==0&&(int)BaitKind.Deluxe==1&&(int)BaitKind.Targeted==2&&(int)BaitKind.Challenge==3&&(int)BaitKind.GiantLobster==4,"new bait appended; old serialized enum values unchanged");
        var craft=new Dictionary<string,string>();var cook=new Dictionary<string,string>();LobsterData.Crafting(craft,k=>k);LobsterData.Cooking(cook,k=>k);
        check(craft[GiantLobsterRules.BaitId].Split('/')[0]==GiantLobsterRules.Id+" 1"&&craft[GiantLobsterRules.BaitId].Split('/')[2]==GiantLobsterRules.BaitId+" 5","craft exactly one Giant Lobster into five bait");
        check(CraftingRecipe.TryParseLevelRequirement(GiantLobsterRules.BaitId,craft[GiantLobsterRules.BaitId],false,out int skill,out int level,true)&&skill==1&&level==7,"actual native crafting parser recognizes Fishing 7");
        check(CraftingRecipe.TryParseLevelRequirement(GiantLobsterRules.DinnerId,cook[GiantLobsterRules.DinnerId],true,out skill,out level,true)&&skill==1&&level==7,"actual native cooking parser recognizes Fishing 7");
        foreach(int permanent in new[]{6,7,10})
        {
            player.craftingRecipes.Remove(GiantLobsterRules.BaitId);player.cookingRecipes.Remove(GiantLobsterRules.DinnerId);player.fishingLevel.Value=permanent;
            LobsterData.Learn(player);LobsterData.Learn(player);
            check(player.craftingRecipes.ContainsKey(GiantLobsterRules.BaitId)==(permanent>=7)&&player.cookingRecipes.ContainsKey(GiantLobsterRules.DinnerId)==(permanent>=7),"idempotent permanent-level recipe completion "+permanent);
        }
        var food=ItemRegistry.Create<SObject>("(O)"+GiantLobsterRules.DinnerId);
        var buffs=food.GetFoodOrDrinkBuffs().ToArray();
        check(buffs.Length==1&&buffs[0].effects.FishingLevel.Value==3&&buffs[0].effects.LuckLevel.Value==0,"actual native food buff has Fishing +3 and no luck");
        check(buffs[0].millisecondsDuration==420000,"native food buff is exactly 420 seconds at base multiplier");
        check(food.Price==700&&food.staminaRecoveredOnConsumption()==160&&food.healthRecoveredOnConsumption()==72,"native dinner price and recovery are 700/160/72");
        check(ItemRegistry.Create<SObject>("(O)"+GiantLobsterRules.Id).Price==450&&bait.Price==60,"actual ItemRegistry fish/bait base prices 450/60");
        check(cook[GiantLobsterRules.DinnerId].Split('/')[0]==GiantLobsterRules.Id+" 1 256 1 192 1 250 1"&&cook[GiantLobsterRules.DinnerId].Split('/')[2]==GiantLobsterRules.DinnerId+" 1","formal dinner ingredients and output: giant/tomato/potato/kale each one, output one");
        foreach(var entry in new[]{(GiantLobsterRules.Id,450),(GiantLobsterRules.BaitId,60),(GiantLobsterRules.DinnerId,700)})
        {
            var old=ItemRegistry.Create<SObject>("(O)"+entry.Item1,9,quality:2);old.Price=0;if(entry.Item1==GiantLobsterRules.DinnerId)old.Edibility=0;
            old.modData["test/legacy"]="keep";LobsterMigration.RepairPreview(old);LobsterMigration.RepairPreview(old);
            check(old.Price==entry.Item2&&old.Stack==9&&old.Quality==2&&old.modData["test/legacy"]=="keep","idempotent preview economy migration preserves exact item/count/quality/metadata "+entry.Item1);
            if(entry.Item1==GiantLobsterRules.DinnerId)check(old.Edibility==64,"old preview dinner no longer has zero recovery");
            old.Price=1234;LobsterMigration.RepairPreview(old);check(old.Price==1234,"non-placeholder custom price preserved "+entry.Item1);
        }
        var foreign=ItemRegistry.Create<SObject>("(O)715");foreign.Price=0;LobsterMigration.RepairPreview(foreign);check(foreign.Price==0,"preview repair never changes another fish");
        var nativePot=DrawPatchTargets.Require(typeof(CrabPot),"DayUpdate",typeof(void));
        check(Harmony.GetPatchInfo(nativePot)!.Owners.Contains("BSV.Lobster.Tests.GiantLobster"),"real native crab pot transpiler installed exactly once");
        var ocean=new HashSet<string>{"ocean"};var fresh=new HashSet<string>{"freshwater"};
        var edited=GiantLobsterRules.OrdinaryTrapData(fish);var pool=CrabRules.NativePool(fish,ocean);
        check(!edited.ContainsKey(GiantLobsterRules.Id)&&!pool.Any(p=>p.Id=="(O)"+GiantLobsterRules.Id),"fixed rare roll is sole entry; ordinary pot and machine pools exclude Giant");
        check(edited.SequenceEqual(GiantLobsterRules.OrdinaryTrapData(edited)),"ordinary data preparation is idempotent and retains order");
        check(edited.SequenceEqual(fish.Where(x=>x.Key!=GiantLobsterRules.Id)),"all foreign/ordinary fish rows retained byte-for-byte and in order");
        bool duplicateRejected=false;
        try{CrabRules.Begin(new(){Kind=MachineKinds.Crab},new[]{new CrabBait(0,"(O)685",150,false)},new[]{new CrabAquatic("(O)"+GiantLobsterRules.Id,1)},35,false,false,1000,1);}
        catch(ArgumentException){duplicateRejected=true;}
        check(duplicateRejected,"machine kernel rejects any duplicate rare entry injected into the ordinary pool");
        foreach(bool habitat in new[]{false,true})foreach(double u in new[]{0d,Math.BitDecrement(.03),.03,Math.BitIncrement(.03),Math.BitDecrement(1d)})
            check(GiantLobsterRules.SelectCrab(habitat,u)==(habitat&&u<.03),$"fixed 3% production predicate boundary {habitat}/{u:R}");
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,-.01,1d})
        {bool threw=false;try{GiantLobsterRules.SelectCrab(true,invalid);}catch(ArgumentOutOfRangeException){threw=true;}check(threw,"invalid capture RNG rejected "+invalid);}
        foreach(var kind in Enum.GetValues<BaitKind>())foreach(bool eligible in new[]{false,true})foreach(bool normal in new[]{false,true})foreach(double u in new[]{0d,.009999999999,.01,.010000000001,.999999999})
            check(GiantLobsterRules.SelectSpirit(eligible,normal,kind,u)==(kind==BaitKind.GiantLobster&&eligible&&normal&&u<.01),$"shared Spirit predicate {kind}/{eligible}/{normal}/{u}");
        var rng=new Random(139715);int sharedHits=0;const int trials=100000;
        for(int i=0;i<trials;i++)if(GiantLobsterRules.SelectSpirit(true,true,BaitKind.GiantLobster,rng.NextDouble()))sharedHits++;
        check(Math.Abs(sharedHits-trials*.01)<6*Math.Sqrt(trials*.01*.99),"100000 actual shared-rule E, seed 139715, prespecified six-sigma tolerance");
        var machine=Machine(check);
        var crabMachine=CrabMachine(pool,check);
        var pot=NativePot(player,check);
        var selection=Manual(player,fish,check);
        return new{version="1.1.40",formal_parameters=true,live_game=false,shared=new{trials,seed=139715,hits=sharedHits,rate=(double)sharedHits/trials},machine,crabMachine,pot,selection};
    }

    private static object Machine(Action<bool,string> check)
    {
        var config=new BalanceConfig{BaitPerCycle=1,Normal=new(){Fish=1,OtherItem=0}};
        var fish=new[]{new FishChoice("(O)128",1),new FishChoice(Production.SpiritId,1,true)};
        int hits=0,quantity=0;const int trials=100000;
        for(int i=0;i<trials;i++)
        {
            var state=Production.Begin(config,new[]{new BaitSpend(0,1,BaitKind.GiantLobster,null)},fish,new[]{"(O)168"},1000,139000+i);
            if(state.LegendaryPlans.Count>0){hits++;quantity+=state.LegendaryPlans[0].Count;if(state.LegendaryPlans[0].Count>3)throw new Exception("Spirit over cap");}
        }
        check(Math.Abs(hits-trials*.01)<6*Math.Sqrt(trials*.01*.99),"100000 actual Production.Begin E, one special bait and reset fixture quota, six sigma");
        config.BaitPerCycle=350;
        int mixedHits=0;
        for(int seed=0;seed<2000;seed++)
        {
            var mixed=Production.Begin(config,new[]{new BaitSpend(0,349,BaitKind.Normal,null),new BaitSpend(1,1,BaitKind.GiantLobster,null)},fish,new[]{"(O)168"},1000,seed);
            var onlyOne=Production.Begin(new BalanceConfig{BaitPerCycle=1,Normal=new(){Fish=1,OtherItem=0}},new[]{new BaitSpend(0,1,BaitKind.GiantLobster,null)},fish,new[]{"(O)168"},1000,seed);
            if(mixed.LegendaryPlans.Count>0)mixedHits++;
            if(mixed.LegendaryPlans.Count!=onlyOne.LegendaryPlans.Count)throw new Exception("Ordinary bait acquired Spirit eligibility.");
            if(!ProductionState.Load(mixed.Save()).Pending.SequenceEqual(mixed.Pending))throw new Exception("Reload rerolled batch");
        }
        check(true,"2000 mixed 349 ordinary +1 special batches: only one eligible roll; pending outputs survive JSON reload");
        foreach(var kind in new[]{BaitKind.Normal,BaitKind.Deluxe,BaitKind.Targeted,BaitKind.Challenge})
        {
            var state=Production.Begin(config,new[]{new BaitSpend(0,650,kind,kind==BaitKind.Targeted?Production.SpiritId:null)},fish,new[]{"(O)168"},1000,3,new(){Level=3});
            check(state.Pending.All(p=>p.Id!=Production.SpiritId),"non-special bait cannot produce Spirit including target Spirit: "+kind);
        }
        return new{trials,seeds="139000..238999",eligible=trials,selected=hits,item_quantity=quantity,mixedTrials=2000,mixedHits};
    }

    private static object CrabMachine(IReadOnlyList<CrabAquatic> pool,Action<bool,string> check)
    {
        var rows=new List<object>();const int cycles=1000;
        foreach(int level in new[]{1,2})foreach(bool deluxe in new[]{false,true})foreach(bool lure in new[]{false,true})
        {
            // Disable crit ONLY in this probability experiment so output counts equal selections.
            // Production default crit/book behaviour is exercised separately below.
            var config=new CrabBalanceConfig{LevelOneCriticalChance=0,LevelTwoCriticalChance=0};
            int cost=config.Required(level,lure),giant=0,lobster=0,junk=0,total=0;
            for(int i=0;i<cycles;i++)
            {
                var state=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},new[]{new CrabBait(0,deluxe?"(O)DeluxeBait":"(O)685",cost,deluxe)},pool,35,lure,false,1000,140715+i,config,ocean:true);
                total+=state.Crab!.Rolls;giant+=state.Pending.Where(p=>p.Id=="(O)"+GiantLobsterRules.Id).Sum(p=>p.Count);
                lobster+=state.Pending.Where(p=>p.Id=="(O)715").Sum(p=>p.Count);junk+=state.OtherCount;
                if(!CrabRules.Valid(ProductionState.Load(state.Save())))throw new Exception("Crab new-species batch serialization invalid");
            }
            double expected=.03*(1-KingCrabRules.Chance);
            check(Math.Abs(giant-total*expected)<6*Math.Sqrt(total*expected*(1-expected)),$"actual CrabRules.Begin probability L{level}/deluxe={deluxe}/lure={lure}");
            var batch=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},new[]{new CrabBait(0,deluxe?"(O)DeluxeBait":"(O)685",cost,deluxe)},pool,35,lure,true,1000,140715,ocean:true);
            check(batch.Crab!.MachineCrits>0&&batch.Crab.BookDoubles>0&&batch.Pending.Any(p=>p.Id=="(O)"+GiantLobsterRules.Id)&&CrabRules.Valid(batch),"normal default crit/book/quality/persistence includes Giant Lobster "+level+"/"+deluxe+"/"+lure);
            var noBonuses=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},new[]{new CrabBait(0,deluxe?"(O)DeluxeBait":"(O)685",cost,deluxe)},pool,35,lure,false,1000,140715,config,ocean:true);
            check(noBonuses.FishCount==batch.FishCount&&noBonuses.OtherCount==batch.OtherCount,"crit/book toggles never change species opportunity counts");
            var dry=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},new[]{new CrabBait(0,"(O)685",cost,false)},pool,35,lure,false,1000,140715,config,ocean:false);
            check(dry.Pending.All(p=>p.Id!="(O)"+GiantLobsterRules.Id),"non-ocean explicit context cannot produce Giant even with ocean ordinary pool");
            rows.Add(new{level,deluxe,lure,cycles,seeds="140715..141714",total,giant,lobster,junk,expected,rate=(double)giant/total,baitPerCycle=cost});
        }
        return new{fixture=true,probabilityExperiment="crit=0, book=false; separate default-crit/book regression",rows};
    }

    private static Random DayRandom(double seedA,double seedB,double seedC)=>new Random(139715+(int)seedA+(int)seedB*17+(int)seedC);
    private static IEnumerable<CodeInstruction> InjectDayRandom(IEnumerable<CodeInstruction> input)
    {
        int count=0;foreach(var c in input)
        {
            if(c.operand is MethodInfo m&&m.DeclaringType==typeof(Utility)&&m.Name=="CreateDaySaveRandom")
            {count++;c.operand=typeof(LobsterChecks).GetMethod(nameof(DayRandom),All);}
            yield return c;
        }
        if(count!=1)throw new Exception("Native seed call-site fixture mismatch");
    }
    private static object NativePot(Farmer player,Action<bool,string> check)
    {
        var h=new Harmony("BSV.Lobster.Tests.HabitatFixture");
        Game1.locationData["LobsterFixtureOcean"].FishAreas=new(){["Default"]=new(){CrabPotFishTypes=new(){"ocean"},CrabPotJunkChance=.2f}};
        bool nativeSeed=true;string? seedBlocked=null;
        try{Utility.CreateDaySaveRandom(1,2,3);}catch(FileNotFoundException ex){nativeSeed=false;seedBlocked=ex.Message;}
        if(!nativeSeed)h.Patch(DrawPatchTargets.Require(typeof(CrabPot),"DayUpdate",typeof(void)),transpiler:new HarmonyMethod(typeof(LobsterChecks),nameof(InjectDayRandom)));
        try
        {
            var location=new GameLocation();location.name.Value="LobsterFixtureOcean";
            var pot=new CrabPot();pot.Location=location;pot.owner.Value=player.UniqueMultiplayerID;
            var output=new List<object>();var old=player.professions.ToArray();
            foreach(string professions in new[]{"None","Mariner","Luremaster","All"})foreach(string bait in new[]{"685","DeluxeBait","774","SpecificBait","SpecificGiant"})
            {
                bool mariner=professions is "Mariner" or "All";
                player.professions.Clear();if(mariner)player.professions.Add(10);if(professions=="Luremaster")player.professions.Add(11);if(professions=="All")for(int id=0;id<30;id++)player.professions.Add(id);
                int giant=0,lobster=0,junk=0,giantItems=0;var histogram=new Dictionary<string,int>();const int days=100000;
                for(int i=0;i<days;i++)
                {
                    pot=new CrabPot{Location=location};pot.owner.Value=player.UniqueMultiplayerID;
                    pot.TileLocation=new Vector2(i,mariner?3:2);pot.bait.Value=ItemRegistry.Create<SObject>("(O)"+(bait=="SpecificGiant"?"SpecificBait":bait));
                    if(bait is "SpecificBait" or "SpecificGiant"){pot.bait.Value.preservedParentSheetIndex.Value=bait=="SpecificGiant"?GiantLobsterRules.Id:"715";pot.bait.Value.preserve.Value=SObject.PreserveType.Bait;}
                    pot.DayUpdate();var item=pot.heldObject.Value;
                    if(item==null)throw new Exception("Native pot missing output");
                    histogram[item.ItemId]=histogram.GetValueOrDefault(item.ItemId)+1;
                    if(item.ItemId==GiantLobsterRules.Id){giant++;giantItems+=item.Stack;}else if(item.ItemId=="715")lobster++;else if(item.Category!=-4)junk++;
                    if(item.ItemId==GiantLobsterRules.Id&&item.Quality!=(bait=="DeluxeBait"?1:0))throw new Exception("Native quality was lost");
                }
                check(Math.Abs(giant-days*.03)<6*Math.Sqrt(days*.03*.97)&&lobster>0&&(!mariner||junk==0),"100000 actual native ocean opportunities, 3% six-sigma and Mariner no-trash: "+professions+"/"+bait+" giant="+giant+" lobster="+lobster+" junk="+junk);
                check(bait=="774"?giantItems>giant:giantItems==giant,"native Wild Bait quantity applies after species choice; other bait has one base output: "+professions+"/"+bait);
                output.Add(new{professions,mariner,bait,opportunities=days,giant,giantItems,lobster,junk,giantRate=(double)giant/days,lobsterRate=(double)lobster/days});
            }
            player.professions.Clear();pot=new CrabPot{Location=location};pot.owner.Value=player.UniqueMultiplayerID;
            pot.DayUpdate();check(pot.heldObject.Value==null,"unbaited non-Luremaster pot never enters capture opportunity");
            var stored=ItemRegistry.Create<SObject>("(O)715",2,2);pot.heldObject.Value=stored;pot.bait.Value=ItemRegistry.Create<SObject>("(O)685");
            pot.DayUpdate();check(ReferenceEquals(pot.heldObject.Value,stored)&&stored.Stack==2&&stored.Quality==2,"uncollected output not overwritten or rerolled next day");
            Game1.locationData["LobsterFixtureOcean"].FishAreas["Default"].CrabPotFishTypes=new(){"freshwater"};
            foreach(bool mariner in new[]{false,true})
            {
                player.professions.Clear();if(mariner)player.professions.Add(10);
                for(int i=0;i<1000;i++)
                {pot=new CrabPot{Location=location};pot.owner.Value=player.UniqueMultiplayerID;pot.TileLocation=new Vector2(i,4);pot.bait.Value=ItemRegistry.Create<SObject>("(O)685");pot.DayUpdate();if(pot.heldObject.Value?.ItemId==GiantLobsterRules.Id)throw new Exception("freshwater Giant");}
            }
            check(true,"2000 actual native freshwater opportunities exclude Giant with/without Mariner");
            Game1.locationData["LobsterFixtureOcean"].FishAreas["Default"].CrabPotFishTypes=new(){"ocean"};
            foreach(double u in new[]{0d,Math.BitDecrement(.03),.03,Math.BitIncrement(.03)})
            {
                pot=new CrabPot{Location=location};pot.owner.Value=player.UniqueMultiplayerID;
                LobsterCrabPots.SelectOrJunk(new FixedRandom(u),0,pot,2,1);
                check((pot.heldObject.Value?.ItemId==GiantLobsterRules.Id)==(u>=.003&&u<.033),"actual pot hook exact boundary "+u.ToString("R"));
                if(u>=.003&&u<.033)check(pot.heldObject.Value.Stack==2&&pot.heldObject.Value.Quality==1,"pot hook preserves native precomputed count/quality");
            }
            player.professions.Clear();foreach(int value in old)player.professions.Add(value);
            return new{nativeSeedStatus=nativeSeed?"PASS":"BLOCKED",seedBlocked,randomFixture=!nativeSeed,results=output};
        }
        finally {h.UnpatchAll(h.Id);}
    }

    private static object Manual(Farmer player,Dictionary<string,string> fish,Action<bool,string> check)
    {
        // Invoke the production scope/observer/selection methods with actual native Items.
        // Full getFish/rod/UI is separately marked NOT TESTED; this verifies the real one-roll guard.
        var state=new SpiritSelection.Scope{Eligible=true,Attempt=new()};var ordinary=ItemRegistry.Create("(O)128");state.Ordinary.Add(ordinary);
        var end=typeof(SpiritSelection).GetMethod("End",All)!;var oldRandom=Game1.random;
        try
        {
            var constructorRng=new FixedRandom(.009);Game1.random=constructorRng;ItemRegistry.Create(SpiritRules.Fish);
            var rng=new FixedRandom(.009);Game1.random=rng;object[] args={ordinary,state};end.Invoke(null,args);
            check(((Item)args[0]).QualifiedItemId==SpiritRules.Fish&&rng.Count==1+constructorRng.Count,"production manual selector returns Spirit before rod/UI and rolls once; actual="+((Item)args[0]).QualifiedItemId+" draws="+rng.Count);
            args[0]=ordinary;end.Invoke(null,args);check(rng.Count==1+constructorRng.Count&&((Item)args[0]).QualifiedItemId==SpiritRules.Fish,"same cast re-entry does not reroll");
            var invalid=new SpiritSelection.Scope{Eligible=true,Attempt=new()};object[] trash={ItemRegistry.Create("(O)168"),invalid};int beforeTrash=rng.Count;end.Invoke(null,trash);
            check(rng.Count==beforeTrash&&((Item)trash[0]).QualifiedItemId=="(O)168","unvalidated/trash results cannot enter E");
            Game1.toolData=new Dictionary<string,StardewValley.GameData.Tools.ToolData>{["FiberglassRod"]=new(){ClassName="FishingRod",Name="Fiberglass Rod",DisplayName="Fiberglass Rod"}};
            ItemRegistry.AddTypeDefinition(new StardewValley.ItemTypeDefinitions.ToolDataDefinition());
            var rod=new FishingRod(2){ItemId="FiberglassRod"};player.Items.Clear();player.Items.Add(rod);player.CurrentToolIndex=0;rod.attachments[0]=ItemRegistry.Create<SObject>(GiantLobsterRules.Bait,1);
            check(SpiritSelection.HasBait(player),"manual eligibility reads actual attached special bait");
            rod.attachments[0]=ItemRegistry.Create<SObject>("(O)SpecificBait");rod.attachments[0].preservedParentSheetIndex.Value=SpiritRules.Id;
            check(!SpiritSelection.HasBait(player),"targeted Spirit metadata does not substitute for special bait");
        }
        finally {Game1.random=oldRandom;}
        NativeSelection(player,check);
        const int trials=100000;int hits=0;Game1.random=new Random(139163);
        try
        {
            for(int i=0;i<trials;i++)
            {
                var scope=new SpiritSelection.Scope{Eligible=true,Attempt=new()};scope.Ordinary.Add(ordinary);
                object[] values={ordinary,scope};end.Invoke(null,values);if(((Item)values[0]).QualifiedItemId==SpiritRules.Fish)hits++;
            }
            check(Math.Abs(hits-trials*.01)<6*Math.Sqrt(trials*.01*.99),"100000 actual manual End selector opportunities, seed 139163, six sigma");
        }
        finally{Game1.random=oldRandom;}
        return new{scope_test=true,trials,seed=139163,hits,full_rod_gameplay="NOT TESTED"};
    }
    private static void NativeSelection(Farmer player,Action<bool,string> check)
    {
        var map=new GameLocation();map.name.Value="Mountain";map.map=new xTile.Map("Mountain");player.currentLocation=map;
        Game1.locationData["Default"].Fish=new();
        Game1.locationContextData=new Dictionary<string,StardewValley.GameData.LocationContexts.LocationContextData>{["Default"]=new()};
        Game1.game1.instanceGameLocation=map;Game1.timeOfDay=1800;player.fishingLevel.Value=10;
        Game1.locationData["Mountain"].Fish=new(){new(){Id="ordinary-fixture",ItemId="(O)128",Chance=1,IgnoreFishDataRequirements=true,MinFishingLevel=0}};
        var rod=(FishingRod)player.CurrentTool;rod.lastUser=player;rod.isFishing=true;rod.attachments[0]=ItemRegistry.Create<SObject>(GiantLobsterRules.Bait,1);
        var screen=typeof(SpiritQuota).GetField("State",All)!.GetValue(SpiritQuota.Instance)!;
        var local=screen.GetType().GetProperty("Value")!.GetValue(screen)!;
        local.GetType().GetField("Granted",All)!.SetValue(local,true);local.GetType().GetField("Year",All)!.SetValue(local,Game1.year);
        var target=DrawPatchTargets.Require(typeof(GameLocation),"GetFishFromLocationData",typeof(Item),typeof(string),typeof(Vector2),typeof(int),typeof(Farmer),typeof(bool),typeof(bool),typeof(GameLocation),typeof(StardewValley.Internal.ItemQueryContext));
        Item Select()=>(Item)target.Invoke(null,new object?[]{"Mountain",new Vector2(2,2),5,player,false,false,map,null})!;
        var reset=typeof(SpiritSelection).GetMethod("Reset",All)!;
        try
        {
            check(map.GetData()!=null,"native location fixture resolves Mountain data");
            check(Game1.locationData["Default"].Fish!=null,"native Default fish list initialized");
            check(rod.GetBait()!=null&&SpiritQuota.CanCatch(player),"native manual bait and granted-quota fixture ready");
            reset.Invoke(null,new object[]{rod});Game1.random=new FixedRandom(.009);
            var first=Select();check(first.QualifiedItemId==SpiritRules.Fish,"actual GetFishFromLocationData pipeline reaches ordinary observer and single Spirit selection");
            rod.attachments[0]=null;
            check(ReferenceEquals(Select(),first),"last bait consumed: same native selection context retains chosen fish without rechecking depleted bait");
            reset.Invoke(null,new object[]{rod});check(Select().QualifiedItemId=="(O)128","next cast without special bait remains ordinary");
            rod.attachments[0]=ItemRegistry.Create<SObject>(GiantLobsterRules.Bait,1);reset.Invoke(null,new object[]{rod});Game1.random=new FixedRandom(.01);
            check(Select().QualifiedItemId=="(O)128","actual native pipeline exact 1% boundary returns ordinary fish");
        }
        finally{reset.Invoke(null,new object[]{rod});local.GetType().GetField("Granted",All)!.SetValue(local,false);rod.isFishing=false;}
    }
    private sealed class FixedRandom:Random {private readonly double Value;internal int Count;internal FixedRandom(double value)=>Value=value;public override double NextDouble(){Count++;return Value;}}
}

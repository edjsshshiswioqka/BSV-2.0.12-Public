using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using BetterStardewValley;
using BetterStardewValley.LobsterKing;
using Eddie.FishingMachine;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;
using StardewValley.ItemTypeDefinitions;
using StardewValley.GameData.FishPonds;
using Microsoft.Xna.Framework;
using SObject=StardewValley.Object;
internal static class KingChecks
{
    private sealed class Fixed:Random{private readonly double U;internal Fixed(double u){U=u;}public override double NextDouble()=>U;public override int Next(int min,int max)=>min;}
    internal static object Run(Farmer player,Dictionary<string,string> rows,Action<bool,string> check,IModHelper helper,IMonitor monitor,List<FishPondData> runtimePonds)
    {
        // Service patches installed at startup before native method use.
        var farm=new Farm();farm.name.Value="Farm";Game1.game1._locations.Add(farm);Game1._locationLookup.Clear();
        KingData.Objects(Game1.objectData,k=>k);KingData.Fish(rows);
        check(ItemRegistry.Create<SObject>(LobsterKingRules.Fish).Price==LobsterKingRules.Price,"native King object registered with distinct identity and price "+LobsterKingRules.Price);
        check(!GiantLobsterRules.OrdinaryTrapData(rows).ContainsKey(LobsterKingRules.Id)&&!CrabRules.NativePool(rows,new HashSet<string>{"ocean"}).Any(f=>LobsterKingRules.IsFish(f.Id)),"King excluded from both native/machine ordinary pools including Mariner");
        const int trials=1000000,seed=141003;var random=new Random(seed);int king=0,giant=0,crab=0,ordinary=0;
        for(int i=0;i<trials;i++)switch(LobsterKingRules.Select(true,true,random.NextDouble(),true)){case RareCrab.LobsterKing:king++;break;case RareCrab.GiantLobster:giant++;break;case RareCrab.KingCrab:crab++;break;default:ordinary++;break;}
        foreach(var t in new[]{(king,.003),(giant,.05),(crab,.03),(ordinary,.917)})check(Math.Abs(t.Item1-trials*t.Item2)<6*Math.Sqrt(trials*t.Item2*(1-t.Item2)),"one million actual shared selector, predeclared six-sigma tolerance: "+t.Item2);
        foreach(bool crabEnabled in new[]{false,true})foreach(bool eligible in new[]{false,true})foreach(bool ocean in new[]{false,true})foreach(double u in new[]{0d,Math.BitDecrement(.003),.003,Math.BitIncrement(.003),Math.BitDecrement(.053),.053,Math.BitIncrement(.053),Math.BitDecrement(.083),.083,Math.BitIncrement(.083),Math.BitDecrement(1d)})
        {
            var expected=!ocean?RareCrab.Ordinary:u<.003?(eligible?RareCrab.LobsterKing:RareCrab.Ordinary):u<.053?RareCrab.GiantLobster:u<.083&&crabEnabled?RareCrab.KingCrab:RareCrab.Ordinary;
            check(LobsterKingRules.Select(ocean,eligible,u,crabEnabled)==expected,$"mutually exclusive boundary {ocean}/{eligible}/{u:R}");
        }
        var pot=new CrabPot{Location=new GameLocation()};pot.Location.name.Value="LobsterFixtureOcean";
        Game1.year=3;Game1.season=Season.Spring;
        check(KingWildQuota.Available(),"actual farm-scoped quota initially available");
        BetterStardewValley.GiantLobster.LobsterCrabPots.SelectOrJunk(new Fixed(.001),0,pot,2,2);
        var wild=pot.heldObject.Value;string token=wild.modData[LobsterKingRules.ClaimKey];
        check(wild.QualifiedItemId==LobsterKingRules.Fish&&wild.Stack==1&&wild.Quality==2&&!KingWildQuota.Available(),"actual native capture hook persists exactly one quality-preserving wild King and seasonal claim; double quantity ignored");
        pot.heldObject.Value=null;BetterStardewValley.GiantLobster.LobsterCrabPots.SelectOrJunk(new Fixed(.001),0,pot,2,0);
        check(pot.heldObject.Value==null,"used seasonal King interval falls back without second King");
        BetterStardewValley.GiantLobster.LobsterCrabPots.SelectOrJunk(new Fixed(.003),0,pot,2,0);
        check(pot.heldObject.Value?.ItemId==GiantLobsterRules.Id&&pot.heldObject.Value.Stack==2,"Giant fixed interval and native double quantity survive used King quota");
        check(!KingWildQuota.Acquire(player,token)&&KingWildQuota.Read().FirstAcquired.Count==0,"generation alone or failed backpack insertion cannot acquire claim");
        var moved=wild.getOne();check(moved.modData[LobsterKingRules.ClaimKey]==token,"native getOne preserves provenance across chest/automation transfer");
        player.Items.Add(moved);check(KingWildQuota.Acquire(player,token),"real synchronized backpack plus committed claim acquires recipient once");
        check(!moved.modData.ContainsKey(LobsterKingRules.ClaimKey)&&!KingWildQuota.Acquire(player,token),"spent marker removed for stacking; duplicate claim has no second effect");
        check(KingWildQuota.Read().FirstAcquired.Count==1&&KingWildQuota.Read().Claims.Single().Recipient==player.UniqueMultiplayerID,"saved first recipient kept separately from production quota");
        var offspring=ItemRegistry.Create(LobsterKingRules.Fish);player.Items.Add(offspring);
        check(!KingWildQuota.Acquire(player,Guid.NewGuid().ToString("N")),"unknown and pond fish do not acquire wild claim");
        Game1.season=Season.Summer;check(KingWildQuota.Available(),"new season opens quota");Game1.season=Season.Winter;check(!KingWildQuota.Available(),"winter closed");Game1.year++;Game1.season=Season.Spring;check(KingWildQuota.Available(),"same season next year opens independent quota");
        var ledger=KingWildQuota.Read();string persisted=JsonSerializer.Serialize(ledger);check(JsonSerializer.Deserialize<KingLedger>(persisted)!.Claims.Single().Token==token,"committed source token round trip without reroll");
        var failed=new KingLedger();string saved="";bool output=false;bool threw=false;
        try{KingTransactions.Commit(failed,1,0,1,"fixture",l=>saved=JsonSerializer.Serialize(l),t=>{output=true;throw new Exception("injected output failure");},()=>output=false);}catch{threw=true;}
        check(threw&&!output&&JsonSerializer.Deserialize<KingLedger>(saved)!.Available(1,0),"injected commit failure restores output before reopening quota");
        failed=new();try{KingTransactions.Commit(failed,1,0,1,"fixture",l=>saved=JsonSerializer.Serialize(l),t=>throw new Exception(),()=>throw new Exception());}catch{}
        check(!JsonSerializer.Deserialize<KingLedger>(saved)!.Available(1,0)&&!JsonSerializer.Deserialize<KingLedger>(saved)!.Claims.Single().Committed,"rollback failure keeps durable prepared record fail-closed");
        int machineHits=0;var config=new CrabBalanceConfig();
        foreach(int level in new[]{1,2})foreach(bool lure in new[]{false,true})foreach(bool book in new[]{false,true})foreach(bool deluxe in new[]{false,true})
        {
            int kings=0;
            for(int n=0;n<40;n++)
            {
                int count=config.Required(level,lure);var state=CrabRules.Begin(new(){Kind=MachineKinds.Crab,Level=level},new[]{new CrabBait(0,deluxe?"(O)DeluxeBait":"(O)685",count,deluxe)},new[]{new CrabAquatic("(O)715",1)},35,lure,book,1000,n+141000,config,true,true);
                int k=state.Pending.Where(p=>LobsterKingRules.IsFish(p.Id)).Sum(p=>p.Count);if(k>1||!CrabRules.Valid(state))throw new Exception("machine King cap/stat failure");kings+=k;
                if(k==1){state.Pending=state.Pending.Select(p=>LobsterKingRules.IsFish(p.Id)?p with{WildClaim=Guid.NewGuid().ToString("N")}:p).ToList();var restored=ProductionState.Load(state.Save());check(restored.Pending.Single(p=>LobsterKingRules.IsFish(p.Id)).WildClaim!=null,"real machine state keeps wild token through serialization");}
            }
            check(kings>0,$"machine base rolls and final one-King cap {level}/{lure}/{book}/{deluxe}");machineHits+=kings;
        }
        var original=new FishPondData{Id="foreign",MaxPopulation=10};var ponds=new List<FishPondData>{original};
        KingData.Ponds(ponds,new[]{"390 5"});KingData.Ponds(ponds,new[]{"390 5"});var data=ponds.Single(p=>p.Id==LobsterKingRules.Id);
        check(ponds.Count==2&&ReferenceEquals(ponds[0],original)&&data.MaxPopulation==2&&data.PopulationGates.Count==1&&data.PopulationGates.ContainsKey(2),"explicit TEST-ONLY material fixture: own pond row idempotent, max two and sole gate at two; foreign row intact");
        KingData.Ponds(ponds);check(ponds.Count==2&&ponds.Single(p=>p.Id==LobsterKingRules.Id).PopulationGates.Count==0,"release row defers gate to actual final native Lobster data, no hardcoded materials");
        var nativeLobster=new FishPondData{Id="fixture-native-lobster",RequiredTags=new(){"item_lobster"},Precedence=-1000,
            PopulationGates=new(){[3]=new(){"390 2 4","388 3","382 1 2"},[5]=new(){"74 1"}}};
        runtimePonds.Add(nativeLobster);KingData.Ponds(runtimePonds);
        check(KingPonds.NativeGate()!.SequenceEqual(nativeLobster.PopulationGates[3]),"final loaded ordinary Lobster's first material pool resolved via native GetRawData");
        var pond=new FishPond();pond.fishType.Value=LobsterKingRules.Id;pond.currentOccupants.Value=1;pond.UpdateMaximumOccupancy();
        check(pond.maxOccupants.Value==1,"actual native UpdateMaximumOccupancy starts at one with a gate at two");
        NativeUpgrade(pond,nativeLobster,check);
        pond.lastUnlockedPopulationGate.Value=2;pond.UpdateMaximumOccupancy();
        check(pond.maxOccupants.Value==2&&pond.FishCount==1,"native gate completion expands capacity without adding a second parent");
        pond.SpawnFish();check(pond.FishCount==1,"actual native SpawnFish blocked only for King");
        check(!KingPonds.Tick(pond,100)&&pond.output.Value==null,"one parent has no roe/offspring");pond.currentOccupants.Value=2;
        check(!KingPonds.Tick(pond,101),"adding second parent starts future cycle, no immediate elapsed day");
        var cycle=JsonSerializer.Deserialize<KingPondState>(pond.modData[KingPonds.Key])!;int due=cycle.DueDay;
        check(due>=105&&due<=108,"independent saved cycle is four to seven full days");
        string cycleSaved=pond.modData[KingPonds.Key];check(!KingPonds.Tick(pond,101)&&cycleSaved==pond.modData[KingPonds.Key],"same-day repeat does not reroll cycle");
        Game1.season=Season.Winter;pond.goldenAnimalCracker.Value=true;
        check(KingPonds.Tick(pond,due)&&pond.output.Value?.QualifiedItemId==LobsterKingRules.Fish&&pond.output.Value.Stack==1&&pond.FishCount==2,"actual pond adapter deposits one offspring in winter, retains parents, ignores cracker multiplier");
        check(!pond.output.Value.modData.ContainsKey(LobsterKingRules.ClaimKey),"pond offspring has no wild claim");
        var retained=pond.output.Value;var produce=DrawPatchTargets.Require(typeof(FishPond),"GetFishProduce",typeof(Item),typeof(Random));
        check(ReferenceEquals(produce.Invoke(pond,new object[]{new Random(1)}),retained),"actual native production hook preserves pending output and cannot replace it with roe");
        KingPonds.Tick(pond,due+20);check(ReferenceEquals(pond.output.Value,retained),"long full output retained without overwriting");
        pond.output.Value=null;check(KingPonds.Tick(pond,due+20)&&!KingPonds.Tick(pond,due+20),"vacated overdue slot gets exactly one offspring, no same-day backlog");
        pond.currentOccupants.Value=1;KingPonds.Tick(pond,due+21);check(JsonSerializer.Deserialize<KingPondState>(pond.modData[KingPonds.Key])!.DueDay==-1&&pond.output.Value!=null,"removing parent resets unfinished cycle and preserves generated output");
        check(KingWildQuota.Read().Claims.Count==1,"all pond cycles leave wild ledger unchanged");
        var def=new ObjectDataDefinition();foreach(int quality in new[]{0,1,2,4})
        {
            var fish=ItemRegistry.Create<SObject>(LobsterKingRules.Fish,1,quality);var smoke=def.CreateFlavoredSmokedFish(fish);
            check(smoke.preservedParentSheetIndex.Value==LobsterKingRules.Id&&smoke.Quality==quality&&smoke.Price==9000,"native smoked King identity, quality and base price "+quality);
            foreach(int professions in new[]{0,1,2,3}){player.professions.Clear();if(professions>=1)player.professions.Add(6);if(professions>=2)player.professions.Add(8);if(professions==3)player.professions.Add(4);
            double q=quality switch{1=>1.25,2=>1.5,4=>2,_=>1};double m=professions>=2?1.5:professions==1?1.25:1;check(Math.Abs(smoke.sellToStorePrice(player.UniqueMultiplayerID)-9000*q*m*(professions==3?1.4:1))<2,"native smoked price profession replacement/Artisan "+quality+"/"+professions);}
        }
        KingData.Ponds(runtimePonds,null);
        runtimePonds.Remove(nativeLobster);
        var fixtureHarmony=new Harmony("BSV.King.NativeDailyFixture");
        var day=DrawPatchTargets.Require(typeof(CrabPot),"DayUpdate",typeof(void));
        fixtureHarmony.Patch(day,transpiler:new HarmonyMethod(typeof(LobsterChecks).GetMethod("InjectDayRandom",BindingFlags.Static|BindingFlags.NonPublic)!));
        try
        {
            Game1.year=9;Game1.season=Season.Fall;player.professions.Clear();foreach(int prof in Enumerable.Range(0,30))player.professions.Add(prof);
            int generated=0;
            for(int i=0;i<5000;i++)
            {
                var daily=new CrabPot{Location=pot.Location,TileLocation=new Vector2(i,8)};daily.owner.Value=player.UniqueMultiplayerID;daily.bait.Value=ItemRegistry.Create<SObject>("(O)774");daily.DayUpdate();
                if(daily.heldObject.Value?.QualifiedItemId==LobsterKingRules.Fish){generated++;check(daily.heldObject.Value.Stack==1&&daily.readyForHarvest.Value,"actual patched native DayUpdate: all professions/wild bait still exactly one ready King");}
            }
            check(generated==1,"5000 real native daily calls with farm quota produce exactly one autumn wild King; deterministic seed fixture replaces missing hash dependency ONLY");
        }
        finally{fixtureHarmony.UnpatchAll(fixtureHarmony.Id);}
        Game1.year=1;Game1.season=Season.Spring;player.professions.Clear();
        var sashimiChecks=KingSashimiChecks.Run(player,check);
        return new{trials,seed,king,giant,crab,ordinary,crabRate=(double)crab/trials,sashimiChecks,kingRate=(double)king/trials,giantRate=(double)giant/trials,machineHits,pond="native GetRawData/TryGetNeededItemData tested with explicit content/seed fixtures; final game material pool NOT TESTED",sashimi="production approved values",live_game=false};
    }
    private static int QuestSeed;
    private static int Seed(double a,double b,double c,double d,double e)=>QuestSeed;
    private static Random DaySeed(double a,double b,double c)=>new(QuestSeed);
    private static IEnumerable<CodeInstruction> QuestRandom(IEnumerable<CodeInstruction> code)
    {
        int matches=0;foreach(var instruction in code)
        {
            if(instruction.operand is MethodInfo m&&m.DeclaringType==typeof(Utility))
            {
                if(m.Name=="CreateRandomSeed"){instruction.operand=AccessTools.Method(typeof(KingChecks),nameof(Seed));matches++;}
                if(m.Name=="CreateDaySaveRandom"){instruction.operand=AccessTools.Method(typeof(KingChecks),nameof(DaySeed));matches++;}
            }
            yield return instruction;
        }
        if(matches!=2)throw new Exception("native quest seed fixture call sites changed");
    }
    private static void NativeUpgrade(FishPond pond,FishPondData original,Action<bool,string> check)
    {
        var method=DrawPatchTargets.Require(typeof(FishPond),"TryGetNeededItemData",typeof(bool),typeof(string).MakeByRefType(),typeof(int).MakeByRefType());
        var h=new Harmony("BSV.King.PondQuestSeedFixture");h.Patch(method,transpiler:new(typeof(KingChecks),nameof(QuestRandom)));
        try
        {
            check(pond.GetFishPondData().PopulationGates.ContainsKey(2),"native instance accessor receives resolved King gate before caching");
            var seen=new HashSet<string>();var quantities=new HashSet<int>();
            for(int i=0;i<1000;i++)
            {
                QuestSeed=i;object?[] args={null,0};check((bool)method.Invoke(pond,args)!,"native pond requests upgrade at capacity one");
                string item=(string)args[0]!;int count=(int)args[1]!;seen.Add(item);quantities.Add(count);
                check(item switch{"390"=>count>=2&&count<=4,"388"=>count==3,"382"=>count>=1&&count<=2,_=>false},"native random request is from source gate with original quantity bounds");
                object?[] again={null,0};method.Invoke(pond,again);check(Equals(args[0],again[0])&&Equals(args[1],again[1]),"same native seed keeps upgrade request stable");
            }
            check(seen.Count==3&&quantities.Count==4,"native ChooseFrom and ranged quantities both exercised");
            check(original.PopulationGates[3].SequenceEqual(new[]{"390 2 4","388 3","382 1 2"})&&original.PopulationGates[5].Single()=="74 1","ordinary pond gate lists not modified");
            original.PopulationGates[3]=new(){"388 7"};QuestSeed=42;object?[] changed={null,0};method.Invoke(pond,changed);
            check((string)changed[0]! =="388"&&(int)changed[1]! ==7,"changed loaded native material pool replaces stale King clone on next native query");
            pond.currentOccupants.Value=0;object?[] empty={null,0};check(!(bool)method.Invoke(pond,empty)!,"empty King pond cannot request upgrade");pond.currentOccupants.Value=1;
            pond.lastUnlockedPopulationGate.Value=2;pond.UpdateMaximumOccupancy();object?[] done={null,0};check(!(bool)method.Invoke(pond,done)!,"completed capacity two has no second upgrade request");
            pond.lastUnlockedPopulationGate.Value=0;pond.UpdateMaximumOccupancy();
        }
        finally{h.UnpatchAll(h.Id);}
    }
}

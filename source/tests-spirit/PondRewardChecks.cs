using System.Reflection;
using System.Text.Json;
using BetterStardewValley.SpiritFish;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.FishPonds;
using StardewValley.GameData.Objects;
using StardewModdingAPI;
using Newtonsoft.Json;
using SObject=StardewValley.Object;

internal static class PondRewardChecks
{
    sealed class Samples : Random
    {
        readonly Queue<double> values; internal int Draws;
        internal Samples(params double[] values) { this.values = new(values); }
        public override double NextDouble() { Draws++; return values.Count>0?values.Dequeue():.99; }
    }
    internal static object Run(List<FishPondData> ponds, Action<bool,string> check)
    {
        var vanilla = ponds.Single(p=>p.Id=="fixture-legendary");
        string before=JsonConvert.SerializeObject(vanilla);
        var get=typeof(FishPond).GetMethod("GetFishProduce",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Random)},null)!;
        Item? Produce(FishPond pond,Random rng)=>(Item?)get.Invoke(pond,new object[]{rng});
        foreach(var pair in new[]{("279","Magic Rock Candy"),("74","Prismatic Shard"),("852","Dragon Tooth"),("848","Cinder Shard"),("390","Stone")})
            Game1.objectData[pair.Item1]=new ObjectData{Name=pair.Item2,DisplayName=pair.Item2,Type="Basic",Category=-2,Price=1};
        check(typeof(FishPond).Module.ModuleVersionId==Guid.Parse("aa1f513a-94f0-4ef6-a35f-2d5b4a3721bf"),"pond tests execute audited Stardew 1.6.15 assembly");
        SpiritData.Ponds(ponds);SpiritData.Ponds(ponds);
        check(JsonConvert.SerializeObject(vanilla)==before,"loaded legendary template unaffected by repeated Spirit registration");
        var data=ponds.Single(p=>p.Id==SpiritRules.Id);
        check(data.ProducedItems.Count==5&&data.ProducedItems.Take(4).Select(p=>p.ItemId).SequenceEqual(SpiritPondRewards.Items),"actual loaded Spirit rows ordered once ahead of ordinary roe");
        double remaining=1;var effective=new List<double>();
        for(int i=0;i<4;i++)
        {
            var row=data.ProducedItems[i];effective.Add(remaining*row.Chance);remaining*=1d-row.Chance;
            check(Math.Abs(effective[i]-.1)<2e-8&&row.RequiredPopulation==0&&row.Precedence==int.MinValue&&row.Condition==null,"loaded conditional probability/eligibility/precedence "+i);
        }
        check(Math.Abs(remaining-.6)<4e-8,"native single-precision four misses preserve ~60% ordinary branch");
        var pond=new FishPond();pond.fishType.Value=SpiritRules.Id;pond.currentOccupants.Value=1;pond.maxOccupants.Value=1;
        var untouched=ItemRegistry.Create("(O)390",7);pond.output.Value=untouched;
        var log=(IMonitor)Recorder.Make(typeof(IMonitor));int start=Recorder.Logs.Count;
        SpiritPondRewards.ValidateLoaded(log);
        check(Recorder.Logs.Skip(start).Any(s=>s.StartsWith("Spirit pond loaded rewards verified")),"production runtime validator checks loaded entries and actual ItemRegistry using explicit object fixtures");
        check(ReferenceEquals(pond.output.Value,untouched)&&untouched.Stack==7,"data reload and validation never modify existing unclaimed output");
        var last=data.ProducedItems.Last();last.Precedence=int.MinValue;
        check(Produce(pond,new Samples(0))?.QualifiedItemId=="(O)279","even equal highest-priority ordinary row cannot replace first selected rare");
        last.Precedence=0;
        foreach(bool cracker in new[]{false,true})
        {
            pond.goldenAnimalCracker.Value=cracker;
            for(int i=0;i<4;i++)foreach(double u in new[]{0d,Math.BitDecrement((double)data.ProducedItems[i].Chance),(double)data.ProducedItems[i].Chance,Math.BitIncrement((double)data.ProducedItems[i].Chance)})
            {
                var sequence=Enumerable.Repeat(.99,i).Concat(new[]{u}).Concat(Enumerable.Repeat(.99,8)).ToArray();var rng=new Samples(sequence);
                var item=Produce(pond,rng);
                bool hit=u<data.ProducedItems[i].Chance;
                check(hit?item?.QualifiedItemId==SpiritPondRewards.Items[i]&&item.Stack==(cracker?2:1)&&rng.Draws==i+1:item?.QualifiedItemId=="(O)812",
                    $"native chance boundary {i}/{u:R}/cracker={cracker}; exclusive rare or ordinary roe");
            }
        }
        // Force all rare failures then the original roe: native flavored source and extra roe count survive.
        pond.goldenAnimalCracker.Value=false;
        var roe=(SObject)Produce(pond,new Samples(.99,.99,.99,.99,0,0,.99))!;
        check(roe.QualifiedItemId=="(O)812"&&roe.preservedParentSheetIndex.Value==SpiritRules.Id&&roe.Quality==0&&roe.Price==3030&&roe.Stack>=2,"native ordinary roe source, price, unstarred quality and geometric extra-quantity mechanism preserved");
        var plain=new FishPond();plain.fishType.Value="163";plain.currentOccupants.Value=1;
        var untouchedRng=new Samples(.99);var other=Produce(plain,untouchedRng);
        check(other?.QualifiedItemId=="(O)812"&&untouchedRng.Draws<4,"non-Spirit pond does not consume four reward RNG draws");
        // Deliberate legacy duplicates; remove only in Spirit clone, not shared template.
        vanilla.ProducedItems.Add(new(){ItemId="279",Chance=1});
        vanilla.ProducedItems.Add(new(){RandomItemId=new(){"(O)74","(O)390"},Chance=1});
        SpiritData.Ponds(ponds);var migrated=ponds.Single(p=>p.Id==SpiritRules.Id);
        check(migrated.ProducedItems.Count==6&&migrated.ProducedItems.Last().RandomItemId.SequenceEqual(new[]{"(O)390"}),"duplicate raw ID and random-list rare entries unified only in dedicated clone");
        check(vanilla.ProducedItems.Count==3&&vanilla.ProducedItems.Last().RandomItemId.Count==2,"duplicate normalization does not edit external template");
        vanilla.ProducedItems=new(){new(){Id="fixture-roe",ItemId="(O)812",Chance=.5f,RequiredPopulation=1},new(){Id="fixture-stone",ItemId="(O)390",Chance=.25f,RequiredPopulation=2}};
        SpiritData.Ponds(ponds);
        var summaries=new List<object>();Dictionary<string,int>? noCracker=null;
        foreach(int population in new[]{1,2,10})foreach(bool cracker in new[]{false,true})
        {
            const int trials=100000,seed=138279;
            var rng=new Random(seed);Game1.random=new Random(seed+1);pond.currentOccupants.Value=population;pond.goldenAnimalCracker.Value=cracker;
            var counts=new Dictionary<string,int>();bool quantity=true,source=true;
            for(int n=0;n<trials;n++)
            {
                var item=Produce(pond,rng);string key=item?.QualifiedItemId??"null";counts[key]=counts.GetValueOrDefault(key)+1;
                if(SpiritPondRewards.Items.Contains(key))quantity&=item!.Stack==(cracker?2:1);
                if(key=="(O)812")source&=((SObject)item!).preservedParentSheetIndex.Value==SpiritRules.Id&&item.Quality==0;
            }
            var expected=SpiritPondRewards.Items.ToDictionary(s=>s,s=>.1);expected["(O)812"]=.3;expected["(O)390"]=population>=2?.075:0;expected["null"]=population>=2?.225:.3;
            foreach(var pair in expected)
                check(Math.Abs(counts.GetValueOrDefault(pair.Key)-trials*pair.Value)<=6*Math.Sqrt(trials*pair.Value*(1-pair.Value))+1,$"native 100k E six-sigma distribution {population}/{cracker}/{pair.Key}");
            int normal=counts.GetValueOrDefault("(O)812")+counts.GetValueOrDefault("(O)390")+counts.GetValueOrDefault("null");
            check(Math.Abs(normal-trials*.6)<=6*Math.Sqrt(trials*.6*.4)+1&&counts.Values.Sum()==trials&&quantity&&source,$"all E included, quantity and roe metadata {population}/{cracker}");
            if (!cracker) noCracker=counts;
            else check(counts.All(p=>noCracker!.GetValueOrDefault(p.Key)==p.Value),"cracker changes quantity only, identical seeded item distribution at population "+population);
            summaries.Add(new{population,cracker,trials,seed,counts,ordinary=normal,baseRareQuantity=1,finalRareQuantity=cracker?2:1});
        }
        // Same existing instance reads refreshed data without reconstructing pond or re-rolling stored output.
        vanilla.ProducedItems=new(){new(){ItemId="(O)812",Chance=1,RequiredPopulation=1}};
        SpiritData.Ponds(ponds);check(ReferenceEquals(pond.output.Value,untouched),"100k selection simulations and content reload never write output or claim inventory");
        pond.fishType.Value="163";check(Produce(pond,new Samples(0,.99))?.QualifiedItemId=="(O)812","same pond changed to another fish no longer selects Spirit rewards");
        check(JsonConvert.SerializeObject(vanilla)==before,"ordinary template restored exactly after isolated fixtures");
        NativeFlow(check);
        return new{trialsTotal=600000,source="actual FishPond.GetFishProduce(Random) on real 1.6.15 DLL, BSV data editor, fixture objects/pond template",effectiveProbabilities=effective,ordinaryProbability=remaining,summaries,liveGame=false};
    }

    static void NativeFlow(Action<bool,string> check)
    {
        using var assembly=Mono.Cecil.AssemblyDefinition.ReadAssembly(typeof(FishPond).Assembly.Location);
        var type=assembly.MainModule.GetType(typeof(FishPond).FullName);
        var day=type.Methods.Single(m=>m.Name=="dayUpdate").Body.Instructions;
        var selection=type.Methods.Single(m=>m.Name=="GetFishProduce").Body.Instructions;
        int Index(string name)=>day.Select((x,i)=>(x,i)).First(p=>p.x.Operand is Mono.Cecil.MemberReference r&&r.Name==name).i;
        check(day.Count(i=>i.Operand is Mono.Cecil.MethodReference m&&m.Name=="GetFishProduce")==1&&Index("CreateDaySaveRandom")<Index("BaseMinProduceChance")&&Index("BaseMinProduceChance")<Index("GetFishProduce"),"native daily gate and seeded date/save/tile RNG precede single selection call (IL evidence)");
        check(Index("currentOccupants")<Index("CreateDaySaveRandom"),"native empty-population check precedes RNG/production (IL evidence)");
        check(selection.Count(i=>i.Operand is Mono.Cecil.FieldReference f&&f.Name=="goldenAnimalCracker")==1&&selection.Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Mul),"native selection retains one cracker check and multiply phase (IL evidence)");
        check(!selection.Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Newobj&&i.Operand is Mono.Cecil.MethodReference m&&m.DeclaringType.FullName=="System.Random"),"native selection does not create a new time-seeded RNG");
        foreach(string method in new[]{"dayUpdate","GetFishProduce","doAction"})
        {
            var target=typeof(FishPond).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Single(m=>m.Name==method);
            var patches=HarmonyLib.Harmony.GetPatchInfo(target);
            check(patches==null||!patches.Owners.Any(o=>o.StartsWith("BSV.Spirit")),"no second Spirit production or claim Harmony entrance: "+method);
        }
    }
}

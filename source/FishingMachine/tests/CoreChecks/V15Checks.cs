using System.Text.Json;
using System.Text.Json.Nodes;
using Eddie.FishingMachine.Core;

internal static class V15Checks
{
    internal static void Run(BalanceConfig config,Action<bool,string> check,Action<Action,string> reject)
    {
        check(config.FishCriticalChance==.45,"1.5 default critical chance is exactly 45 percent");
        check(JsonSerializer.Deserialize<BalanceConfig>("{}")!.FishCriticalChance==.45,"legacy config receives critical default");
        foreach(double p in new[]{-.01,1.01,double.NaN,double.PositiveInfinity})
            reject(()=>new BalanceConfig{FishCriticalChance=p}.Validate(),"invalid critical chance rejected");
        check(FishingCritical.Bonus(.45,.449999)==1 && FishingCritical.Bonus(.45,.45)==0,"critical threshold uses one independent probability");
        check(FishingCritical.Bonus(0,0)==0 && FishingCritical.Bonus(1,.999999)==1,"zero and guaranteed critical endpoints");
        reject(()=>FishingCritical.Bonus(.45,1),"invalid critical roll rejected");
        var stock=new List<IngredientSlot>{new(0,PreparationRules.Copper,20),new(1,PreparationRules.Iron,20),new(2,PreparationRules.Wood,400)};
        stock.AddRange(Enumerable.Range(3,12).Select(i=>new IngredientSlot(i,PreparationRules.FiberglassRod,1)));
        check(PreparationRules.Craftable(5,stock)==2,"six rods and 200 wood per craft");
        var craft=PreparationRules.PlanCraft(5,stock)!;
        foreach(var r in PreparationRules.Requirements)
            check(craft.Where(c=>stock.Single(s=>s.Index==c.Index).Id==r.Id).Sum(c=>c.Count)==r.Count,"exact new material total: "+r.Id);
        check(PreparationRules.Craftable(5,stock.Select(s=>s.Id==PreparationRules.Wood?s with{Count=199}:s))==0,"199 wood is insufficient");
        var fish=new[]{new FishChoice("(O)128",.5),new FishChoice("(O)129",.5)};
        var junk=new[]{"(O)168"};
        foreach(BaitKind kind in Enum.GetValues<BaitKind>().Where(k=>k!=BaitKind.Challenge))
        {
            var plan=new[]{new BaitSpend(0,350,kind,kind==BaitKind.Targeted?"(O)128":null)};
            var zero=JsonSerializer.Deserialize<BalanceConfig>(JsonSerializer.Serialize(config))!;zero.FishCriticalChance=0;
            var one=JsonSerializer.Deserialize<BalanceConfig>(JsonSerializer.Serialize(config))!;one.FishCriticalChance=1;
            for(int seed=0;seed<20;seed++)
            {
                var a=Production.Begin(zero,plan,fish,junk,1234,seed);
                var b=Production.Begin(one,plan,fish,junk,1234,seed);
                check(a.FishCount==b.FishCount && a.OtherCount==b.OtherCount && a.EmptyCount==b.EmptyCount,"crit never changes base roll or species RNG");
                check(b.CriticalFishCount==b.FishCount && b.ProducedFishCount==b.FishCount*2,"one extra fish per successful trial; no recursive critical");
                check(b.BaitUsed==350 && b.DueAt-a.StartedAt==2880,"crit costs no additional bait or time");
                foreach(var item in a.Pending)
                    check(b.Pending.Single(p=>p.Key==item.Key).Count==item.Count*(item.Id=="(O)168"?1:2),"same-species fish doubled; junk never doubled");
                check(ProductionState.Load(b.Save()).Pending.SequenceEqual(b.Pending),"critical catches persist exactly");
                check(Production.Begin(one,plan,fish,junk,1234,seed).Save()==b.Save(),"critical batch deterministic for fixed seed");
            }
            long primary=0,bonus=0;
            for(int seed=10000;seed<11000;seed++)
            {
                var b=Production.Begin(config,plan,fish,junk,500,seed);
                check(b.EmptyCount==0 && b.FishCount+b.OtherCount==350,"every bait produces a base catch");
                primary+=b.FishCount;bonus+=b.CriticalFishCount;
                check(b.Pending.Sum(p=>(long)p.Count)==(long)b.ProducedFishCount+b.OtherCount,"production inventory conserves base catches plus bonus");
                check(b.CriticalFishCount<=b.FishCount && b.CriticalFishCount>=0,"bonus bounded by successful fish count");
            }
            check(Math.Abs(bonus-primary*.45)<=6*Math.Sqrt(primary*.45*.55),"real production conditional critical rate: "+kind);
        }
        var normal=new[]{new BaitSpend(0,350,BaitKind.Normal,null)};
        var guaranteed=new BalanceConfig{FishCriticalChance=1,Normal=new(){Fish=1}};
        var full=Production.Begin(guaranteed,normal,new[]{fish[0]},junk,0,7);
        check(full.Pending.Sum(p=>p.Count)==700 && full.CriticalFishCount==350,"350 successes yield exactly 700 fish at guaranteed crit");
        var buffer=DepositPlanner.Plan(new[]{new BufferSlot("(O)128",999,999)},full.Pending,1,_=>999);
        check(buffer.Pending.Sum(p=>p.Count)==700,"overflow retains all critical fish");
        full.Pending=buffer.Pending.ToList();
        check(ProductionState.Load(full.Save()).Pending.Sum(p=>p.Count)==700,"critical overflow survives save");
        guaranteed.Normal=new(){OtherItem=1};
        var trash=Production.Begin(guaranteed,normal,fish,junk,0,7);
        check(trash.CriticalFishCount==0 && trash.Pending.Single().Count==350,"100 percent junk cannot critical");
        guaranteed.Normal=new();
        reject(()=>Production.Begin(guaranteed,normal,fish,junk,0,7),"zero weights rejected before consuming bait");
        var old=Production.Begin(new BalanceConfig{FishCriticalChance=0},normal,fish,junk,100,77);
        var oldJson=JsonNode.Parse(old.Save())!;oldJson["Schema"]=3;oldJson["CycleRulesVersion"]=0;oldJson.AsObject().Remove("CriticalFishCount");
        var legacy=ProductionState.Load(oldJson.ToJsonString());
        check(legacy.CriticalFishCount==0 && legacy.Pending.SequenceEqual(old.Pending),"1.4 batch loads without bonus or reroll");
        check(legacy.Complete(old.DueAt) && legacy.Pending.SequenceEqual(old.Pending),"old deadline delivers old results unchanged");
        var invalid=full.Snapshot();invalid.CriticalFishCount=351;
        reject(()=>ProductionState.Load(invalid.Save()),"reject bonus larger than primary fish count");
        invalid.CriticalFishCount=350;invalid.Schema=3;
        reject(()=>ProductionState.Load(invalid.Save()),"legacy schema cannot claim new critical fields");

        var anchor=new GridTile(10,20);var tiles=PreparationRules.Footprint(anchor);
        check(SingleViewPlacement.Reach==3 && SingleViewPlacement.Orientation==Facing.Right,"one view with three-tile reach");
        for(int y=16;y<=25;y++)for(int x=6;x<=15;x++)
        {
            var player=new GridTile(x,y);bool expected=x>=7 && x<=14 && y>=17 && y<=24 && !tiles.Contains(player);
            check(SingleViewPlacement.InReach(tiles,player)==expected,"new reach perimeter including diagonal and inner exclusion");
        }
        for(int mask=0;mask<16;mask++)
        {
            var water=tiles.Where((p,i)=>(mask&(1<<i))!=0).ToHashSet();
            check(SingleViewPlacement.CanPlace(anchor,water.Contains,tiles.Contains)==(mask!=0),"all mixed shore masks accepted iff some water");
            foreach(var blocked in tiles)
                check(!SingleViewPlacement.CanPlace(anchor,water.Contains,p=>tiles.Contains(p)&&p!=blocked),"water never bypasses occupied or off-map cells");
        }
        foreach(var clicked in tiles)
            check(SingleViewPlacement.Find(clicked,new(7,20),tiles.Contains,tiles.Contains)==anchor,"any clicked corner places machine from one extra tile away");
        check(SingleViewPlacement.Find(anchor,new(6,20),tiles.Contains,tiles.Contains)==null,"four-tile placement cannot bypass reach by snapping");
        foreach(Facing facing in Enum.GetValues<Facing>())
        {
            var oldTiles=SavedFootprint.Tiles(anchor,facing,true);
            check(oldTiles.Length==8 && SingleViewPlacement.InReach(oldTiles,new(oldTiles.Min(p=>p.X)-3,20)),"old eight-tile footprint remains accessible from new reach");
        }
        var queries=new Dictionary<GridTile,int>();
        SingleViewPlacement.Find(anchor,new(7,20),p=>{queries[p]=queries.GetValueOrDefault(p)+1;return true;},_=>true);
        check(queries.Values.All(n=>n==1),"single-view preview memoizes map checks");
        check(VisualState.For("idle",false)==new VisualState(false,false,false),"empty machine has no bait, line or ready badge");
        check(VisualState.For("idle",true)==new VisualState(true,false,false),"loaded but unopened batch keeps line retracted");
        check(VisualState.For("working",false)==new VisualState(true,true,false),"working shows committed fuel and lines even after inventory consumption");
        check(VisualState.For("ready",false)==new VisualState(false,false,true),"finished empty hopper retracts lines and shows fish badge");
        check(VisualState.For("ready",true)==new VisualState(true,false,true),"preloaded bait does not start lines before harvest is collected");
        check(!VisualState.For("error",false).Lines,"invalid cycle retracts lines");
        var layout=new MachineLayout();layout.Validate();
        check(layout.Rods.Length==6,"six independent rods");
        for(int i=0;i<3;i++)check(layout.Rods[i].Tip.X+layout.Rods[i+3].Tip.X==layout.Width
            && layout.Rods[i].Tip.Y==layout.Rods[i+3].Tip.Y,"rod tip attachments are symmetrical");
        var options=new VisualOptions();var pose=new PixelPoint[LineMotion.Segments+1];var repeat=new PixelPoint[pose.Length];
        var tip=new PixelPoint(40,8);var landing=new PixelPoint(10,128);
        for(int rod=0;rod<6;rod++)for(int frame=0;frame<65;frame++)
        {
            double t=frame/20d;int bob=FloatMotion.Offset(t,anchor,options);
            LineMotion.Pose(tip,landing,t,anchor,rod,bob,options,pose);
            LineMotion.Pose(tip,landing,t+options.LineSwayPeriodSeconds,anchor,rod,bob,options,repeat);
            check(pose[0]==new PixelPoint(tip.X,tip.Y+bob),"sway never detaches line from bobbing rod tip");
            check(Math.Abs(pose[^1].X-landing.X)<=.751 && Math.Abs(pose[^1].Y-landing.Y)<=.751,"float stays near water surface");
            check(pose.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)),"line points remain finite");
            check(pose.Zip(repeat).All(p=>Math.Abs(p.First.X-p.Second.X)<1e-8 && Math.Abs(p.First.Y-p.Second.Y)<1e-8),"line sway closes a loop without cumulative drift");
        }
        LineMotion.Pose(tip,landing,0,anchor,0,0,new VisualOptions{EnableLineSway=false},pose);
        LineMotion.Pose(tip,landing,99,anchor,0,0,new VisualOptions{EnableLineSway=false},repeat);
        check(pose.SequenceEqual(repeat) && pose[^1]==landing,"disabled sway is still with attached endpoints");
        reject(()=>new VisualOptions{LineSwayAmplitude=4}.Validate(),"excess sway amplitude rejected");
        reject(()=>new VisualOptions{LineSwayPeriodSeconds=0}.Validate(),"invalid sway period rejected");
        var desired=new PixelPoint(580,1500);
        check(VisualLanding.Resolve(desired,anchor,0,_=>true)==desired,"open water keeps approved float positions");
        var spots=Enumerable.Range(0,6).Select(i=>VisualLanding.Resolve(desired,anchor,i,p=>p==anchor)).ToArray();
        check(spots.Distinct().Count()==6 && spots.All(p=>(int)(p.X/64)==anchor.X && (int)(p.Y/64)==anchor.Y),"narrow bank keeps all six distinct floats on the only water tile");
        var cfgBefore=JsonSerializer.Serialize(config);
        LineMotion.Pose(tip,landing,1,anchor,2,2,config.Visual,pose);
        check(JsonSerializer.Serialize(config)==cfgBefore,"visual motion cannot mutate production settings");
    }
}

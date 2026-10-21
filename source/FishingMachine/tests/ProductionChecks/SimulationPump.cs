using Eddie.FishingMachine;
using System.Reflection;
internal static class SimulationPump
{
 internal static void Drain(ProductionService service)
 {
  var mod=(ModEntry)typeof(ProductionService).GetField("Mod",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(service)!;
  for(int tick=0;tick<5000;tick++)
  {
   string before=string.Join("\n",mod.Machines.All.Keys.Select(c=>service.Read(c).Save()));service.Tick();
   if(before==string.Join("\n",mod.Machines.All.Keys.Select(c=>service.Read(c).Save())))return;
  }
  throw new Exception("Simulation did not drain its persisted backlog.");
 }
}

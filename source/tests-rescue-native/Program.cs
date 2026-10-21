using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Text.Json;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

class Program
{
    static void Main(string[] args)
    {
        var checks = new List<string>();
        void Check(bool value,string description)
        { if(!value)throw new Exception(description); checks.Add(description); }
        var mod=Assembly.LoadFrom(args[0]);
        var service=mod.GetType("BetterStardewValley.MultiplayerRescue.RescueService",true)!;
        SafetyChecks.Prepare(service);
        var apple=mod.GetType("BetterStardewValley.GoldenAppleFeature",true)!;
        var helper=(IModHelper)Recorder.Make(typeof(IModHelper));
        var monitor=(IMonitor)Recorder.Make(typeof(IMonitor));
        var manifest=(IManifest)Recorder.Make(typeof(IManifest));
        object Create(Type type,params object[] values)=>Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,values,null)!;
        object? rescue=null;
        foreach(string name in args[1]=="apple-first"?new[]{"apple","rescue"}:new[]{"rescue","apple"})
        {
            if(name=="apple")Create(apple,helper,monitor);
            else rescue=Create(service,helper,monitor,manifest);
            Check(true,"production constructor and native detour installation: "+name);
        }
        string owner="Eddie.BetterStardewValley.MultiplayerRescue";
        var targets=Harmony.GetAllPatchedMethods().Where(m=>Harmony.GetPatchInfo(m)!.Owners.Contains(owner)).ToArray();
        Check(targets.Length==5,"exactly five rescue targets installed");
        foreach(var pair in new[]{(typeof(Farmer),"takeDamage"),(typeof(Farmer),"Update"),(typeof(Farmer),"CanBeDamaged"),(typeof(Farmer),"MovePosition"),(typeof(FarmerRenderer),"draw")})
            Check(targets.Count(m=>m.DeclaringType==pair.Item1&&m.Name==pair.Item2)==1,"unique native target: "+pair.Item2);
        var damage=targets.Single(m=>m.Name=="takeDamage");
        var finalDamage=PatchProcessor.GetCurrentInstructions(damage);
        Check(finalDamage.Count(i=>i.operand is MethodInfo m && m.DeclaringType==service && m.Name=="DamageAssigned")==1,"actual composed damage IL has exactly one rescue hit observer");
        Check(finalDamage.Count(i=>i.operand is MethodInfo m && m.DeclaringType==apple && m.Name=="SetDamageHealth")==1,"actual composed damage IL retains Golden Apple protection");
        var update=PatchProcessor.GetCurrentInstructions(targets.Single(m=>m.Name=="Update"));
        Check(update.Count(i=>i.operand is MethodInfo m && m.DeclaringType==service && m.Name=="DeathHealth")==1,"actual composed Farmer.Update has exactly one rescue death guard");
        foreach(string eventName in new[]{"add_SaveLoaded","add_DayStarted","add_UpdateTicked","add_ReturnedToTitle","add_ModMessageReceived","add_PeerDisconnected","add_Warped","add_ButtonPressed","add_ButtonsChanged","add_RenderedWorld","add_RenderedHud"})
            Check(Recorder.Events.Any(e=>e.Name==eventName&&(e.Handler.Method.DeclaringType==service||e.Handler.Method.DeclaringType?.DeclaringType==service)),"production event registered: "+eventName);
        Check(Recorder.Commands.ContainsKey("bsv_rescue_status")&&Recorder.Commands.ContainsKey("bsv_rescue_trace"),"readonly status and bounded trace commands registered");
        var missing=service.GetMethod("Exact",BindingFlags.NonPublic|BindingFlags.Static)!;
        bool rejected=false;
        try{missing.Invoke(null,new object[]{typeof(Farmer),"Update",typeof(void),Type.EmptyTypes});}
        catch(TargetInvocationException e){rejected=e.InnerException is MissingMethodException;}
        Check(rejected,"wrong native signature fails closed");
        var farmer=(Farmer)FormatterServices.GetUninitializedObject(typeof(Farmer));farmer.health=0;
        var guard=service.GetMethod("DeathHealth",BindingFlags.NonPublic|BindingFlags.Static)!;
        Check((int)guard.Invoke(null,new object[]{farmer})! == 0,"inactive world leaves native zero health unchanged");
        Check(farmer.health==0,"death guard never writes actual HP");
        SafetyChecks.Run(rescue!,Check);
        Console.WriteLine(JsonSerializer.Serialize(new {passed=true,checks,order=args[1],native_patch_installation=true,real_game_assembly=true,event_interfaces="recording proxy",live_game=false,real_multiplayer=false}));
        // Standalone process owns the detours; it exits rather than tearing down live game hooks.
    }
}

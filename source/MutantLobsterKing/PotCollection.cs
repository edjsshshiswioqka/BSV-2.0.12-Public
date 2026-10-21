using System.Reflection;
using System.Reflection.Emit;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using StardewValley;
using StardewValley.Objects;
namespace BetterStardewValley.MutantLobsterKing;

internal static class PotCollection
{
    internal static void Install(Harmony h)=>h.Patch(DrawPatchTargets.Require(typeof(CrabPot),"checkForAction",typeof(bool),typeof(Farmer),typeof(bool)),transpiler:new(typeof(PotCollection),nameof(Rewrite)));
    internal static uint Book(Stats stats,string key,CrabPot pot)=>UsagePolicy.Protected(pot.heldObject.Value)?0:stats.Get(key);
    internal static void InstallAutomate(Harmony h,Type type)
    {
        var target=type.GetMethod("GetOutput",BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly,Type.DefaultBinder,Type.EmptyTypes,null)
            ??throw new MissingMethodException(type.FullName,"GetOutput");
        h.Patch(target,transpiler:new(typeof(PotCollection),nameof(AutomateRewrite)));
    }
    private static IEnumerable<CodeInstruction> AutomateRewrite(IEnumerable<CodeInstruction> instructions)
    {
        var code=instructions.ToList();
        var getter=code.Where(c=>c.operand is MethodInfo m&&m.Name=="get_Machine"&&m.ReturnType==typeof(CrabPot)).Select(c=>(MethodInfo)c.operand).Distinct().ToArray();
        var sites=Enumerable.Range(1,code.Count-1).Where(i=>code[i-1].opcode==OpCodes.Ldstr&&Equals(code[i-1].operand,"Book_Crabbing")
            &&code[i].operand is MethodInfo m&&m.DeclaringType==typeof(Stats)&&m.Name=="Get"&&m.ReturnType==typeof(uint)).ToArray();
        if(getter.Length!=1||sites.Length!=1)throw new InvalidOperationException($"Automate crab book guard mismatch: getter={getter.Length}, book={sites.Length}.");
        for(int i=0;i<code.Count;i++)
        {
            if(i!=sites[0]){yield return code[i];continue;}
            yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(code[i].labels).WithBlocks(code[i].blocks);
            yield return new CodeInstruction(OpCodes.Call,getter[0]);
            yield return new CodeInstruction(OpCodes.Call,AccessTools.DeclaredMethod(typeof(PotCollection),nameof(Book)));
        }
    }
    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var code=instructions.ToList();
        var sites=Enumerable.Range(1,code.Count-1).Where(i=>code[i-1].opcode==OpCodes.Ldstr&&Equals(code[i-1].operand,"Book_Crabbing")
            &&code[i].operand is MethodInfo m&&m.DeclaringType==typeof(Stats)&&m.Name=="Get"&&m.ReturnType==typeof(uint)).ToArray();
        if(sites.Length!=1)throw new InvalidOperationException($"CrabPot collection book guard target mismatch: {sites.Length}; no partial patch.");
        for(int i=0;i<code.Count;i++)
        {
            if(i!=sites[0]){yield return code[i];continue;}
            yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(code[i].labels).WithBlocks(code[i].blocks);
            yield return new CodeInstruction(OpCodes.Call,AccessTools.DeclaredMethod(typeof(PotCollection),nameof(Book)));
        }
    }
}

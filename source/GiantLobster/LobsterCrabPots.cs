using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Extensions;
using Eddie.FishingMachine.Core;
namespace BetterStardewValley.GiantLobster;

// One fixed roll before native junk/species selection; keep native bait, ready state,
// quality, quantity and standard output/collection. No post-production extra reward.
internal static class LobsterCrabPots
{
    internal static void Install(Harmony h)=>h.Patch(DrawPatchTargets.Require(typeof(CrabPot),"DayUpdate",typeof(void)),
        transpiler:new HarmonyMethod(typeof(LobsterCrabPots),nameof(Rewrite)));
    internal static Dictionary<string,string> Pool(Dictionary<string,string> loaded)=>GiantLobsterRules.OrdinaryTrapData(loaded);
    internal static bool SelectOrJunk(Random random,double junkChance,CrabPot pot,int quantity,int quality)
    {
        if(Game1.IsMasterGame&&BetterStardewValley.MutantLobsterKing.MutantService.NativeCaptureReady&&BetterStardewValley.MutantLobsterKing.MutantService.Eligible(pot.Location,pot.TileLocation)
            &&BetterStardewValley.MutantLobsterKing.MutantWildQuota.AvailableAt(Game1.Date.TotalDays)
            &&MutantLobsterRules.Select(true,random.NextDouble()))
        {
            var previous=pot.heldObject.Value;
            if(BetterStardewValley.MutantLobsterKing.MutantWildQuota.CommitAt(Game1.Date.TotalDays,
                BetterStardewValley.LobsterKing.KingWildQuota.Source(pot,"pot"),token=>
                {
                    var item=ItemRegistry.Create<StardewValley.Object>(MutantLobsterRules.Item,1,quality);
                    item.modData[BetterStardewValley.MutantLobsterKing.MutantWildQuota.ClaimKey]=token;pot.heldObject.Value=item;
                },()=>pot.heldObject.Value=previous))return true;
        }
        if(Game1.IsMasterGame&&!BetterStardewValley.MutantLobsterKing.MutantService.IsSewer(pot.Location)
            &&pot.Location.GetCrabPotFishForTile(pot.TileLocation).Contains("ocean"))
        {
            var rare=LobsterKingRules.Select(true,BetterStardewValley.LobsterKing.KingWildQuota.Available(),random.NextDouble(),BetterStardewValley.KingCrab.KingCrabService.Ready);
            if(rare==RareCrab.KingCrab)
            {
                pot.heldObject.Value=ItemRegistry.Create<StardewValley.Object>(KingCrabRules.Fish,quantity,quality);
                return true;
            }
            if(rare==RareCrab.LobsterKing)
            {
                var previous=pot.heldObject.Value;
                if(BetterStardewValley.LobsterKing.KingWildQuota.Commit(BetterStardewValley.LobsterKing.KingWildQuota.Source(pot,"pot"),token=>
                {
                    var item=ItemRegistry.Create<StardewValley.Object>(LobsterKingRules.Fish,1,quality);
                    item.modData[LobsterKingRules.ClaimKey]=token;pot.heldObject.Value=item;
                },()=>pot.heldObject.Value=previous))return true;
            }
            else if(rare==RareCrab.GiantLobster)
            {
                pot.heldObject.Value=ItemRegistry.Create<StardewValley.Object>("(O)"+GiantLobsterRules.Id,quantity,quality);
                return true;
            }
        }
        return random.NextBool(junkChance);
    }
    private static bool LoadsLocal(CodeInstruction c)=>c.opcode==OpCodes.Ldloc||c.opcode==OpCodes.Ldloc_S
        ||c.opcode==OpCodes.Ldloc_0||c.opcode==OpCodes.Ldloc_1||c.opcode==OpCodes.Ldloc_2||c.opcode==OpCodes.Ldloc_3;
    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var code=instructions.ToList();
        var reads=code.Where(c=>c.operand is MethodInfo m&&m.DeclaringType==typeof(DataLoader)&&m.Name=="Fish"
            &&m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(LocalizedContentManager)})).ToArray();
        var gates=Enumerable.Range(0,code.Count-1).Where(i=>code[i].operand is MethodInfo m
            &&m.DeclaringType==typeof(RandomExtensions)&&m.Name=="NextBool"&&m.ReturnType==typeof(bool)
            &&m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(Random),typeof(double)})
            &&(code[i+1].opcode==OpCodes.Brtrue||code[i+1].opcode==OpCodes.Brtrue_S)).ToArray();
        // Infer and verify count/quality locals from BOTH native fish-creation branches.
        var outputs=Enumerable.Range(3,code.Count-3).Where(i=>code[i].operand is MethodInfo m
            &&m.DeclaringType==typeof(ItemRegistry)&&m.Name=="Create"&&m.IsGenericMethod
            &&m.GetGenericArguments().SequenceEqual(new[]{typeof(StardewValley.Object)})
            &&m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(string),typeof(int),typeof(int),typeof(bool)})
            &&code[i-1].opcode==OpCodes.Ldc_I4_0&&LoadsLocal(code[i-2])&&LoadsLocal(code[i-3])).ToArray();
        if(reads.Length!=1||gates.Length!=1||outputs.Length!=2
            ||!outputs.Skip(1).All(i=>code[i-3].opcode==code[outputs[0]-3].opcode&&Equals(code[i-3].operand,code[outputs[0]-3].operand)
                &&code[i-2].opcode==code[outputs[0]-2].opcode&&Equals(code[i-2].operand,code[outputs[0]-2].operand)))
            throw new InvalidOperationException($"Crab pot target mismatch: tables={reads.Length}, gates={gates.Length}, native outputs={outputs.Length}. No partial patch applied.");
        for(int i=0;i<code.Count;i++)
        {
            var c=code[i];
            if(i==gates[0])
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(c.labels).WithBlocks(c.blocks);
                yield return new CodeInstruction(code[outputs[0]-3].opcode,code[outputs[0]-3].operand);
                yield return new CodeInstruction(code[outputs[0]-2].opcode,code[outputs[0]-2].operand);
                yield return new CodeInstruction(OpCodes.Call,AccessTools.DeclaredMethod(typeof(LobsterCrabPots),nameof(SelectOrJunk),new[]{typeof(Random),typeof(double),typeof(CrabPot),typeof(int),typeof(int)}));
            }
            else yield return c;
            if(ReferenceEquals(c,reads[0]))
                yield return new CodeInstruction(OpCodes.Call,AccessTools.DeclaredMethod(typeof(LobsterCrabPots),nameof(Pool),new[]{typeof(Dictionary<string,string>)}));
        }
    }
}

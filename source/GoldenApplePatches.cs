using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using StardewValley;

namespace BetterStardewValley;

internal sealed partial class GoldenAppleFeature
{
    internal static IEnumerable<CodeInstruction> DamageTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var health = AccessTools.Field(typeof(Farmer), nameof(Farmer.health));
        int patched = 0;
        for (int i = 0; i < code.Count; i++)
        {
            // Strict semantic anchor: health = Math.Max(0, health - damage). Never touch Phoenix healing.
            if (!code[i].StoresField(health) || i < 3 || code[i - 1].operand is not MethodInfo method
                || method.DeclaringType != typeof(Math) || method.Name != nameof(Math.Max) || code[i - 2].opcode != OpCodes.Sub) continue;
            code[i].opcode = OpCodes.Call;
            code[i].operand = AccessTools.Method(typeof(GoldenAppleFeature), nameof(SetDamageHealth));
            patched++;
        }
        if (patched != 1) throw new InvalidOperationException($"BSV Tenacity requires exactly one final damage assignment; found {patched}.");
        return code;
    }
    internal static IEnumerable<CodeInstruction> SeasoningTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var getter = AccessTools.PropertyGetter(typeof(Item), nameof(Item.Quality));
        int patched = 0;
        foreach (var instruction in code)
            if (instruction.Calls(getter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(GoldenAppleFeature), nameof(SeasoningQuality));
                patched++;
            }
        if (patched != 1) throw new InvalidOperationException($"BSV Golden Apple requires exactly one seasoning quality check; found {patched}.");
        return code;
    }
}

using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using StardewValley;
using StardewValley.Menus;

namespace BetterStardewValley.DeveloperAppreciation;

// Native 1.6.15 marks read before construction and hands unclaimed attachments to
// a transient overflow menu. Scope the persistent entitlement adaptation to one mail.
internal static class DeveloperLetterAttachment
{
    internal static void Install(Harmony h)
    {
        h.Patch(DrawPatchTargets.Require(typeof(LetterViewerMenu), "HandleItemCommand", typeof(string), typeof(string)),
            prefix: new HarmonyMethod(typeof(DeveloperLetterAttachment), nameof(BeforeParse)));
        foreach (string method in new[] { "receiveLeftClick", "cleanupBeforeExit" })
            h.Patch(DrawPatchTargets.Require(typeof(LetterViewerMenu), method, typeof(void),
                method == "receiveLeftClick" ? new[] { typeof(int), typeof(int), typeof(bool) } : Type.EmptyTypes),
                transpiler: new HarmonyMethod(typeof(DeveloperLetterAttachment), nameof(Transfers)));
    }
    private static void BeforeParse(LetterViewerMenu __instance, ref string mail)
    {
        if (__instance.mailTitle != DeveloperLetterRules.Id) return;
        if (DeveloperLetterService.Instance?.Begin(__instance) != true)
            mail = mail.Replace(DeveloperLetterRules.Attachment, "");
    }
    private static IEnumerable<CodeInstruction> Transfers(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var original = instructions.ToList();
        var code = original.Select(c => new CodeInstruction(c)).ToList();
        string native = __originalMethod.Name == "receiveLeftClick" ? "addItemByMenuIfNecessary" : "addItemsByMenuIfNecessary";
        string replacement = __originalMethod.Name == "receiveLeftClick" ? nameof(One) : nameof(Many);
        int count = 0;
        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].operand is not MethodInfo m || m.DeclaringType != typeof(Farmer) || m.Name != native) continue;
            var load = new CodeInstruction(OpCodes.Ldarg_0); load.labels.AddRange(code[i].labels); code[i].labels.Clear();
            code.Insert(i++, load); code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(DeveloperLetterAttachment), replacement); count++;
        }
        string id = "LetterViewerMenu." + __originalMethod.Name;
        if (count != 1) { TranspilerGuard.Failed(id, "Developer letter native transfer target mismatch: " + __originalMethod.Name); return original; }
        TranspilerGuard.Succeeded(id);
        return code;
    }
    private static void One(Farmer who, Item item, ItemGrabMenu.behaviorOnItemSelect? callback, bool force, LetterViewerMenu menu)
    {
        if (menu.mailTitle != DeveloperLetterRules.Id) { who.addItemByMenuIfNecessary(item, callback, force); return; }
        DeveloperLetterService.Instance?.Transfer(menu, who, item);
    }
    private static void Many(Farmer who, List<Item> items, ItemGrabMenu.behaviorOnItemSelect? callback, bool force, LetterViewerMenu menu)
    {
        if (menu.mailTitle != DeveloperLetterRules.Id) { who.addItemsByMenuIfNecessary(items, callback, force); return; }
        foreach (var item in items) DeveloperLetterService.Instance?.Transfer(menu, who, item);
    }
}

namespace BetterStardewValley;

internal static class WineRules
{
    internal const string Prefix = "Eddie.BetterStardewValley";
    internal const string GrapeSeedId = "301", GrapeId = "398";
    internal const string GrapeWineId = Prefix + "_GrapeWine";
    internal const string BloodOfJesusId = Prefix + "_BloodOfJesus";
    internal const string GrapeWine = "(O)" + GrapeWineId;
    internal const string BloodOfJesus = "(O)" + BloodOfJesusId;
    internal const string Keg = "(BC)12", Cask = "(BC)163";
    internal const string BsvKegId = Prefix + "_Keg", BsvKeg = "(BC)" + BsvKegId;
    internal const string PendingKey = Prefix + "/WinePending";
    internal const string BatchKey = Prefix + "/WineBatch";
    internal const string KegRuleId = Prefix + "/GrapeWine";
    internal const int GrapeSeedPrice = 1200;
    internal const int GrapeDaysToMature = 15, GrapeRegrowDays = 7;
    internal const int GrapeWinePrice = 990, BloodOfJesusPrice = 30000;
    internal const int BloodOfJesusDenominator = 120;
    internal const double BloodOfJesusChance = 1.0 / BloodOfJesusDenominator;
    internal const double CellarCapacityMultiplier = 2.0;

    // Independent draws: no history, streak, pity, or per-player state.
    internal static bool IsRare(int uniformDraw) => uniformDraw == 0;
    // Any player may resolve: the result comes from the batch's saved draw, so it is the same whoever does it.
    internal static bool ShouldResolve(string machineId, string? outputId, bool pending)
        => machineId == BsvKeg && outputId == GrapeWine && pending;
    internal static bool IsIndexedKeg(string id) => id == Keg || id == BsvKeg;

    // Saved batch entropy is generated only on an actual input, never by a probe or collection.
    // A pending output has the same result across save/load even before its host resolution.
    internal static int BatchDraw(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        int seed = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return new Random(seed).Next(BloodOfJesusDenominator);
    }
}

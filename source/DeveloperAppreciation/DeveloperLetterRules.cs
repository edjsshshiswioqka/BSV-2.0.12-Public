using StardewValley;
[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("DeveloperLetterNativeChecks")]

namespace BetterStardewValley.DeveloperAppreciation;

internal static class DeveloperLetterRules
{
    internal const string Id = "Eddie.BetterStardewValley_DeveloperAppreciation";
    internal const string StateKey = "Eddie.BetterStardewValley/DeveloperAppreciation";
    internal const string Pending = "1|" + Id + "|pending";
    internal const string Claimed = "1|" + Id + "|claimed";
    internal const string Transferring = "1|" + Id + "|transferring";
    internal const string Attachment = "%item object " + WineRules.BloodOfJesus + " 1 %%";
    internal static bool Eligible(int year, Season season, int day) => year > 1 || year == 1 && season == Season.Winter && day >= 28;
    // Technical commands never come from translators. A malformed translation cannot
    // inject another reward, title separator, name substitution or gender branch.
    internal static string SafeText(string value) => value.Replace("%", "％").Replace("[#]", "［＃］").Replace("@", "＠").Replace("¦", "｜");
    internal static string Letter(Func<string, string> tr) => SafeText(tr("developer-letter.heading")) + "^^"
        + SafeText(tr("developer-letter.body")).Replace("\r", "").Replace("\n\n", "^^").Replace("\n", "^")
        + " " + Attachment + "[#]" + SafeText(tr("developer-letter.name"));
}

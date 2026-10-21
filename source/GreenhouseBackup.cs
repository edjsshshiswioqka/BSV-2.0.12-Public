using System;
using System.IO;

namespace BetterStardewValley;

internal static class GreenhouseBackup
{
    internal static string? Create(string? savePath, string backupRoot)
    {
        // Brand-new games have no on-disk save yet and nothing to back up.
        if (string.IsNullOrWhiteSpace(savePath) || !Directory.Exists(savePath)) return null;
        string saveName = new DirectoryInfo(savePath).Name;
        string mainSave = Path.Combine(savePath, saveName);
        if (!File.Exists(mainSave)) return null;
        string destination = Path.Combine(backupRoot, saveName,
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        Directory.CreateDirectory(destination);
        File.Copy(mainSave, Path.Combine(destination, saveName), overwrite: false);
        string info = Path.Combine(savePath, "SaveGameInfo");
        if (File.Exists(info)) File.Copy(info, Path.Combine(destination, "SaveGameInfo"), overwrite: false);
        return destination;
    }
}

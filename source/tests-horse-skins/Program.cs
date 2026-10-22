using BetterStardewValley;
using Microsoft.Xna.Framework;

// Runs the coat generator on the player's real horse sprite (horse.rgba, extracted locally, not in the repo)
// and compares every coat with the approved sample sheet (coats/<id>.rgba). Offline check, not in game.
internal static class Program
{
    private static int Main()
    {
        string dir = Environment.GetEnvironmentVariable("BSV_REAL_MAPS") ?? "";
        string path = Path.Combine(dir, "horse.rgba");
        if (!File.Exists(path)) { Console.WriteLine("SKIP: horse.rgba not found"); return 0; }
        var bytes = File.ReadAllBytes(path);
        const int w = 224, h = 128;
        var source = new Color[w * h];
        for (int i = 0; i < source.Length; i++) source[i] = new Color(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
        int failures = 0;
        foreach (var skin in HorseSkins.All)
        {
            var result = HorseSkins.Apply(source, w, h, skin) ?? source;
            string refPath = Path.Combine(dir, "coats", skin.Id + ".rgba");
            if (!File.Exists(refPath)) { Console.WriteLine($"MISSING reference {skin.Id}"); failures++; continue; }
            var expected = File.ReadAllBytes(refPath);
            int diff = 0;
            for (int i = 0; i < result.Length; i++)
                if (result[i].R != expected[i * 4] || result[i].G != expected[i * 4 + 1] || result[i].B != expected[i * 4 + 2] || result[i].A != expected[i * 4 + 3]) diff++;
            bool changed = !ReferenceEquals(result, source) || skin.Id == HorseSkins.Original;
            Console.WriteLine($"{(diff <= 8 && changed ? "PASS" : "FAIL")}: coat {skin.Id} differs from the approved sample in {diff} px");
            if (diff > 8 || !changed) failures++;
            var raw = new byte[result.Length * 4];
            for (int i = 0; i < result.Length; i++) { raw[i * 4] = result[i].R; raw[i * 4 + 1] = result[i].G; raw[i * 4 + 2] = result[i].B; raw[i * 4 + 3] = result[i].A; }
            Directory.CreateDirectory(Path.Combine(dir, "coats-out"));
            File.WriteAllBytes(Path.Combine(dir, "coats-out", skin.Id + ".rgba"), raw);
        }
        Console.WriteLine($"{{\"suite\":\"horse-coats\",\"coats\":{HorseSkins.All.Count},\"failures\":{failures},\"status\":\"{(failures == 0 ? "PASS" : "FAIL")}\"}}");
        return failures == 0 ? 0 : 1;
    }
}

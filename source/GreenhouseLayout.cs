using System;
using System.Globalization;

namespace BetterStardewValley;

internal sealed class GreenhouseLayout
{
    internal const int AddedWidth = 12;
    internal const int AddedHeight = 10;
    internal int SoilX { get; }
    internal int SoilY { get; }
    internal int CutX => SoilX + AddedWidth;
    internal int CutY => SoilY + AddedHeight;
    internal string Stamp => $"v1:{SoilX},{SoilY},12,10";

    internal GreenhouseLayout(int soilX, int soilY)
    {
        SoilX = soilX;
        SoilY = soilY;
    }

    internal int MoveX(int x) => x < CutX ? x : x + AddedWidth;
    internal int MoveY(int y) => y < CutY ? y : y + AddedHeight;
    internal float MovePixelX(float x) => x < CutX * 64 ? x : x + AddedWidth * 64;
    internal float MovePixelY(float y) => y < CutY * 64 ? y : y + AddedHeight * 64;

    internal bool IsInserted(int x, int y) =>
        (x >= CutX && x < CutX + AddedWidth) || (y >= CutY && y < CutY + AddedHeight);

    internal int SourceX(int x, int fillColumn) =>
        x < CutX ? x : x < CutX + AddedWidth ? fillColumn : x - AddedWidth;
    internal int SourceY(int y, int fillRow) =>
        y < CutY ? y : y < CutY + AddedHeight ? fillRow : y - AddedHeight;

    internal static GreenhouseLayout Parse(string value)
    {
        if (!value.StartsWith("v1:", StringComparison.Ordinal))
            throw new InvalidOperationException("Unknown greenhouse layout version.");
        string[] parts = value[3..].Split(',');
        if (parts.Length != 4 || parts[2] != "12" || parts[3] != "10")
            throw new InvalidOperationException("Unsupported greenhouse layout dimensions.");
        return new GreenhouseLayout(int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    // Only source coordinates move; destinations in other locations stay unchanged.
    internal string MoveProperty(string value, int stride, int xOffset, int yOffset)
    {
        string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length % stride != 0)
            throw new InvalidOperationException("Unrecognized coordinate property: " + value);
        for (int i = 0; i < parts.Length; i += stride)
        {
            parts[i + xOffset] = MoveX(int.Parse(parts[i + xOffset], CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
            parts[i + yOffset] = MoveY(int.Parse(parts[i + yOffset], CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
        }
        return string.Join(" ", parts);
    }
}

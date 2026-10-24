using System;
using System.Globalization;

namespace BetterStardewValley;

/// <summary>
/// Where BSV widens the native 12 x 10 greenhouse bed. Columns are inserted right of the native bed and rows below it,
/// so every native and planted tile left/above the cut keeps its coordinates.
/// v1 (2.0.12): +12 x +10 = 24 x 20 (480 tiles). v2: +23 x +10 = 35 x 20 (700 tiles), sized so quality (3 x 3),
/// iridium (5 x 5) and iridium + pressure nozzle (7 x 7) sprinklers all tile the bed in a regular grid.
/// </summary>
internal sealed class GreenhouseLayout
{
    internal const int BaseWidth = 12, BaseHeight = 10;
    internal const int CurrentAddedWidth = 23, CurrentAddedHeight = 10;
    internal int SoilX { get; }
    internal int SoilY { get; }
    internal int AddedWidth { get; }
    internal int AddedHeight { get; }
    internal int Version => AddedWidth == 12 && AddedHeight == 10 ? 1 : 2;
    internal int BedWidth => BaseWidth + AddedWidth;
    internal int BedHeight => BaseHeight + AddedHeight;
    internal int Tiles => BedWidth * BedHeight;
    internal int CutX => SoilX + BaseWidth;
    internal int CutY => SoilY + BaseHeight;
    internal string Stamp => $"v{Version}:{SoilX},{SoilY},{AddedWidth},{AddedHeight}";

    internal GreenhouseLayout(int soilX, int soilY, int addedWidth = CurrentAddedWidth, int addedHeight = CurrentAddedHeight)
    {
        SoilX = soilX;
        SoilY = soilY;
        AddedWidth = addedWidth;
        AddedHeight = addedHeight;
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

    /// <summary>Same base map, larger insert: the extra columns/rows are added after the older insert.</summary>
    internal bool Grows(GreenhouseLayout older) => older.SoilX == SoilX && older.SoilY == SoilY
        && AddedWidth >= older.AddedWidth && AddedHeight >= older.AddedHeight && (AddedWidth, AddedHeight) != (older.AddedWidth, older.AddedHeight);
    /// <summary>Moves a tile of a save expanded with <paramref name="older"/> into this layout.</summary>
    internal int UpgradeX(GreenhouseLayout older, int x) => x < older.CutX + older.AddedWidth ? x : x + AddedWidth - older.AddedWidth;
    internal int UpgradeY(GreenhouseLayout older, int y) => y < older.CutY + older.AddedHeight ? y : y + AddedHeight - older.AddedHeight;

    internal static GreenhouseLayout Parse(string value)
    {
        string[] parts = value.Length > 3 && value[0] == 'v' && value[2] == ':' ? value[3..].Split(',') : Array.Empty<string>();
        if (parts.Length != 4) throw new InvalidOperationException("Unknown greenhouse layout version.");
        var layout = new GreenhouseLayout(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture), int.Parse(parts[3], CultureInfo.InvariantCulture));
        bool known = (value[1], layout.AddedWidth, layout.AddedHeight) is ('1', 12, 10) or ('2', CurrentAddedWidth, CurrentAddedHeight);
        if (!known) throw new InvalidOperationException("Unsupported greenhouse layout dimensions.");
        return layout;
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

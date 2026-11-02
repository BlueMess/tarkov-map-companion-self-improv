namespace TarkovMapCompanion.Maps;

/// <summary>
/// A place name with its height band already resolved to floors, so the renderer never does that
/// work per frame.
/// </summary>
/// <remarks>
/// The catalog entry (<see cref="MapLabelJson"/>) carries <c>top</c>/<c>bottom</c> as game-world
/// heights; <see cref="Floors"/> is the same information expressed against this map's own
/// <see cref="MapFloor.Name"/>s. Nothing here is localized: place names are data, not UI text.
/// </remarks>
public sealed class MapLabel
{
    public required string Text { get; init; }

    /// <summary>Game-space [x, z].</summary>
    public required double X { get; init; }

    public required double Z { get; init; }

    /// <summary>Font size as a percentage, 100 being the baseline.</summary>
    public double? Size { get; init; }

    /// <summary>Rotation in degrees about the anchor.</summary>
    public double? Rotation { get; init; }

    /// <summary>True when the source declares a height band for this name.</summary>
    public bool HasFloorData { get; init; }

    /// <summary>
    /// <see cref="MapFloor.Name"/> values whose extents this name's band overlaps, in floor
    /// declaration order. Empty when the source has no band, or when the band belongs to no floor
    /// (i.e. the base level).
    /// </summary>
    public IReadOnlyList<string> Floors { get; init; } = [];

    public override string ToString() => $"{Text} ({X}, {Z})";
}

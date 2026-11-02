using System.Globalization;
using TarkovMapCompanion.Localization;

namespace TarkovMapCompanion.Maps;

/// <summary>Where a floor's imagery comes from.</summary>
public sealed record MapFloor(
    string Name,
    string? SvgLayerId,
    string? TilePathTemplate,
    bool ShownByDefault,
    IReadOnlyList<MapExtentJson> Extents)
{
    /// <summary>
    /// True when something at this height and horizontal position belongs on this floor.
    /// An extent with no footprints applies to the whole map at that height.
    /// </summary>
    public bool Covers(GamePosition position)
    {
        foreach (var extent in Extents)
        {
            var height = extent.Height;
            if (height is { Count: >= 2 } && (position.Y < height[0] || position.Y > height[1]))
                continue;

            if (extent.Bounds is not { Count: > 0 })
                return true;

            foreach (var footprint in extent.Bounds)
            {
                if (footprint.Contains(position.X, position.Z))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The band form of <see cref="Covers"/>: true when any height between <paramref name="low"/>
    /// and <paramref name="high"/> at this x/z belongs to this floor. A null bound means unbounded on
    /// that side. Place names are the only caller -- they carry a height band, not a single point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test is "do the two bands overlap", not "which band wins at one height", so the 999,
    /// ±10000 and similar sentinel values in the data need no special case: the extents use the same
    /// convention, and overlap resolves them exactly as it resolves any other band.
    /// </para>
    /// <para>
    /// A band with extent (<c>low &lt; high</c>) has to overlap the extent's interior: touching at
    /// one boundary is <em>not</em> an overlap, so a name spanning [34, 999] is on the floor that
    /// starts at 34 and not also on the one that ends there. That is what keeps interchange's 30
    /// shop names, all of which sit at exactly 34 -- the height the two mall floors share -- off the
    /// 2nd-floor view. A degenerate band (or a single height, such as ground-zero's
    /// <c>Science Office</c> at 28/28) is a point, and a point exactly on a floor's own floor height
    /// belongs to that floor, so that case keeps <see cref="Covers"/>'s inclusive test.
    /// </para>
    /// <para>
    /// Bounds in either order are accepted; an inverted pair is swapped rather than silently
    /// matching nothing.
    /// </para>
    /// </remarks>
    public bool CoversBand(double x, double z, double? low, double? high)
    {
        var bandLow = low ?? double.NegativeInfinity;
        var bandHigh = high ?? double.PositiveInfinity;

        if (bandLow > bandHigh)
            (bandLow, bandHigh) = (bandHigh, bandLow);

        var point = bandLow == bandHigh;

        foreach (var extent in Extents)
        {
            var height = extent.Height;
            if (height is { Count: >= 2 })
            {
                var disjoint = point
                    ? bandLow < height[0] || bandLow > height[1]
                    : bandHigh <= height[0] || bandLow >= height[1];

                if (disjoint)
                    continue;
            }

            if (extent.Bounds is not { Count: > 0 })
                return true;

            foreach (var footprint in extent.Bounds)
            {
                if (footprint.Contains(x, z))
                    return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A map we can actually render: the interactive variant of a tarkov.dev map group, with its
/// projection resolved and its floors flattened into a usable list.
/// </summary>
public sealed class GameMap
{
    internal GameMap(MapGroupJson group, MapVariantJson variant)
    {
        NormalizedName = group.NormalizedName;

        // The data's own name (keyed by map id) wins; the derived name is the fallback.
        DisplayName = MapNames.Display(group.NormalizedName);
        Description = group.Description;
        Key = variant.Key;

        Author = variant.Author;
        AuthorLink = variant.AuthorLink;

        Projection = new MapProjection(variant.CoordinateRotation ?? 0, variant.Transform);

        Bounds = variant.Bounds ?? throw new InvalidOperationException(
            $"map '{group.NormalizedName}' has no bounds; it cannot be projected");

        BaseRect = Projection.ToBaseRect(Bounds);

        if (UpstreamDataFixes.TryCorrectBounds(group.NormalizedName, Bounds, Projection, out var corrected))
        {
            BoundsWereCorrected = true;
            BaseRect = corrected;
        }

        // Only Reserve differs here, but getting it wrong silently misplaces the whole map.
        SvgBaseRect = variant.SvgBounds is { Count: >= 2 }
            ? Projection.ToBaseRect(variant.SvgBounds)
            : BaseRect;

        SvgUrl = variant.SvgPath;
        BaseSvgLayerId = variant.SvgLayer;
        BaseTilePathTemplate = variant.TilePath;

        // tarkov.dev's Leaflet layer defaults tileSize to 256 when the field is absent.
        TileSize = variant.TileSize ?? 256;
        MinZoomLevel = variant.MinZoom ?? 0;
        MaxZoomLevel = variant.MaxZoom ?? 6;

        HeightRange = variant.HeightRange is { Count: >= 2 }
            ? (variant.HeightRange[0], variant.HeightRange[1])
            : null;

        Floors = (variant.Layers ?? [])
            .Select(layer => new MapFloor(
                layer.Name,
                layer.SvgLayer,
                layer.TilePath,
                layer.Show,
                layer.Extents ?? []))
            .ToArray();

        Labels = (variant.Labels ?? [])
            .Where(label => label.Position is { Count: >= 2 })
            .Select(ResolveLabel)
            .ToArray();
    }

    public string NormalizedName { get; }
    public string DisplayName { get; }
    public string? Description { get; }
    public string Key { get; }

    public string? Author { get; }
    public string? AuthorLink { get; }

    public MapProjection Projection { get; }

    /// <summary>Game-space bounds as <c>[[x0, z0], [x1, z1]]</c>.</summary>
    public IReadOnlyList<IReadOnlyList<double>> Bounds { get; }

    /// <summary>Base-space rect the map covers. Tiles and marker culling use this.</summary>
    public MapRect BaseRect { get; }

    /// <summary>True when <see cref="BaseRect"/> came from a local fix rather than the source data.</summary>
    public bool BoundsWereCorrected { get; }

    /// <summary>Base-space rect the SVG overlay is stretched to. Equals <see cref="BaseRect"/> except on Reserve.</summary>
    public MapRect SvgBaseRect { get; }

    public string? SvgUrl { get; }
    public string? BaseSvgLayerId { get; }
    public string? BaseTilePathTemplate { get; }

    public int TileSize { get; }
    public int MinZoomLevel { get; }
    public int MaxZoomLevel { get; }

    /// <summary>Height band the base imagery represents, when the source declares one.</summary>
    public (double Min, double Max)? HeightRange { get; }

    public IReadOnlyList<MapFloor> Floors { get; }

    /// <summary>
    /// The place names the artwork carries, with their height band already resolved to floors so the
    /// renderer does no per-frame work.
    /// </summary>
    public IReadOnlyList<MapLabel> Labels { get; }

    /// <summary>
    /// Turns one catalog entry into a resolved <see cref="MapLabel"/>. Runs once, in the constructor.
    /// </summary>
    private MapLabel ResolveLabel(MapLabelJson source)
    {
        var x = source.Position![0];
        var z = source.Position![1];
        var hasFloorData = source.Top is not null || source.Bottom is not null;

        return new MapLabel
        {
            Text = source.Text,
            X = x,
            Z = z,
            Size = source.Size,
            Rotation = source.Rotation,
            HasFloorData = hasFloorData,
            Floors = hasFloorData ? FloorsOverlappingBand(x, z, source.Top, source.Bottom) : [],
        };
    }

    /// <summary>
    /// The floors a place name's height band overlaps, in floor declaration order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A source that gives both bounds is a band, and either order is accepted (<c>top</c> above
    /// <c>bottom</c> is the normal shape, but one inverted pair would otherwise match nothing).
    /// </para>
    /// <para>
    /// A source that gives only one bound is read as a <em>point</em> at that height, not as a
    /// half-infinite slab. That is the measured reading of the data: the vendored catalog has 27
    /// such labels, all reserve, all carrying <c>bottom: -6</c> and no <c>top</c>, and -6 is that
    /// map's own ground height. Treating the missing side as unbounded below drags 12 of them
    /// (tarmac, dome, the pawns) onto reserve's 2nd-4th floors, which the source plainly does not
    /// mean. <see cref="MapFloor.CoversBand"/> keeps the unbounded reading for its own contract;
    /// this is the call site that decides what a lone bound means.
    /// </para>
    /// </remarks>
    private IReadOnlyList<string> FloorsOverlappingBand(double x, double z, double? top, double? bottom)
    {
        double? low, high;

        if (top is { } t && bottom is { } b)
        {
            low = Math.Min(t, b);
            high = Math.Max(t, b);
        }
        else
        {
            low = high = top ?? bottom;
        }

        var floors = new List<string>();

        foreach (var floor in Floors)
        {
            if (floor.CoversBand(x, z, low, high))
                floors.Add(floor.Name);
        }

        return floors;
    }

    /// <summary>
    /// The floor that claims this position, or null when none does — which means the base level.
    /// </summary>
    /// <remarks>
    /// Declaration order is priority, not best fit: tarkov.dev's height bands overlap (customs has
    /// two floors claiming [5.7,10000]) and the footprints are what separate them, so this must stay
    /// the same "first one wins" rule the map data was authored against.
    /// </remarks>
    public MapFloor? FloorAt(GamePosition position) => Floors.FirstOrDefault(f => f.Covers(position));

    public bool HasSvg => !string.IsNullOrWhiteSpace(SvgUrl);
    public bool HasTiles => !string.IsNullOrWhiteSpace(BaseTilePathTemplate);

    /// <summary>
    /// Whether a game position falls inside the map's extent.
    /// </summary>
    /// <remarks>
    /// Tested in base space rather than against the raw game-coordinate bounds, so that a map
    /// whose bounds needed correcting (see <see cref="UpstreamDataFixes"/>) is judged by the
    /// corrected extent. For every other map the two are equivalent.
    /// </remarks>
    public bool ContainsPosition(GamePosition position) => BaseRect.Contains(ToBase(position));

    public MapPoint ToBase(GamePosition position) => Projection.ToBase(position);

    public override string ToString() => DisplayName;

    /// <summary>
    /// "streets-of-tarkov" -> "Streets of Tarkov". The source JSON has no display name for every
    /// map, so derive one; the short words stay lowercase so it reads like a place name.
    /// </summary>
    internal static string ToDisplayName(string normalizedName)
    {
        if (string.IsNullOrWhiteSpace(normalizedName))
            return normalizedName;

        string[] lowercase = ["of", "the", "and"];
        var words = normalizedName.Split('-', StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', words.Select((word, index) =>
            index > 0 && lowercase.Contains(word)
                ? word
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word)));
    }
}

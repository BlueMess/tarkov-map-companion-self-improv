using SkiaSharp;
using TarkovMapCompanion.Maps;

namespace TarkovMapCompanion.Rendering;

/// <summary>
/// The place names drawn in the map artwork, as a layer: white text with a dark halo, exactly the
/// styling tarkov.dev uses for its place-names layer.
/// </summary>
/// <remarks>
/// <para>
/// The names come from the map catalog's <c>labels[]</c> (<see cref="GameMap.Labels"/>), placed with
/// the same projection every marker uses. They are always English and are drawn straight from
/// <see cref="MapLabel.Text"/>: this is place data, not UI text, so it must never pass through the
/// localizer.
/// </para>
/// <para>
/// A label's <c>top</c>/<c>bottom</c> are a game-world height band, not a pixel nudge, and they now
/// decide which floors a name is drawn on: <see cref="GameMap"/> resolves them to floor names once,
/// and <see cref="IsOnActiveFloor"/> filters against the selection the user has ticked. Positioning
/// is still purely from <c>[x, z]</c>, which is what the source gives. <c>size</c> is a percentage
/// with 100 as the baseline; <c>rotation</c> is degrees about the anchor.
/// </para>
/// </remarks>
public sealed class LandmarkOverlay : IMapOverlay
{
    /// <summary>Font size for a label with <c>size: 100</c> (or no size at all).</summary>
    private const float BaseTextSize = 12f;

    /// <summary>
    /// Above the base imagery and the heatmap, but below the POI, annotation and quest markers, the
    /// guide line and the player marker. Names are background context, so the markers that answer
    /// "what is here" and "where am I going" all draw over them.
    /// </summary>
    public int ZOrder => 400;

    public bool IsVisible { get; set; } = true;

    public GameMap Map { get; set; }

    /// <summary>
    /// Floors the canvas is showing; null until the session pushes it, which draws every name.
    /// </summary>
    public IReadOnlyCollection<string>? ActiveFloors { get; set; }

    /// <summary>
    /// Whether the ground level is drawn. Only matters to names that declare a floor band; see
    /// <see cref="IsOnActiveFloor"/>.
    /// </summary>
    public bool ShowBaseLayer { get; set; } = true;

    public LandmarkOverlay(GameMap map) => Map = map;

    /// <summary>
    /// Whether this place name belongs to a level the user is showing. A name whose band no floor
    /// claims is the base level, so it follows <see cref="ShowBaseLayer"/>. A name the source says
    /// nothing about (<see cref="MapLabel.HasFloorData"/> false) is never hidden: dropping data we
    /// cannot place would delete the whole layer on customs, streets, woods and lighthouse.
    /// </summary>
    public bool IsOnActiveFloor(MapLabel label)
    {
        if (ActiveFloors is null)
            return true;

        if (!label.HasFloorData)
            return true;

        if (label.Floors.Any(floor => ActiveFloors.Contains(floor)))
            return true;

        return label.Floors.Count == 0 && ShowBaseLayer;
    }

    public void Draw(SKCanvas canvas, Viewport viewport)
    {
        var typeface = AppFonts.Label;

        using var text = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Typeface = typeface,
        };
        using var halo = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            Typeface = typeface,
        };

        foreach (var label in Map.Labels)
        {
            if (!IsOnActiveFloor(label) || string.IsNullOrEmpty(label.Text))
                continue;

            var screen = viewport.ToScreen(Map.Projection.ToBase(label.X, label.Z));

            // size is a percentage, with 100 (or a missing value) as the baseline.
            var textSize = BaseTextSize * (float)((label.Size ?? 100) / 100.0);
            text.TextSize = textSize;
            halo.TextSize = textSize;

            // Centre the text on its anchor, then rotate about the anchor so a rotated name spins
            // in place instead of orbiting the point.
            var dx = -text.MeasureText(label.Text) / 2f;
            var dy = textSize * 0.35f;

            canvas.Save();
            canvas.Translate((float)screen.X, (float)screen.Y);

            if (label.Rotation is { } rotation)
                canvas.RotateDegrees((float)rotation);

            canvas.DrawText(label.Text, dx, dy, halo);
            canvas.DrawText(label.Text, dx, dy, text);
            canvas.Restore();
        }
    }
}

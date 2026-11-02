namespace TarkovMapCompanion.Maps;

/// <summary>The layer state a view should show for one position.</summary>
/// <remarks>
/// Transient view state, not a preference: it describes what should be drawn right now and must
/// never be written back to <c>AppSettings.ShowBaseLayer</c>. Following the player down a staircase
/// is not the same decision as the user choosing to draw the map without its ground level, and
/// persisting it would leave the next map (or the next launch) showing a hole.
/// </remarks>
public readonly record struct FloorView(bool ShowBaseLayer, IReadOnlyList<string> Floors);

/// <summary>
/// Decides which map floor to show for a player position, so the view can follow the player through
/// a building without the user ticking a box on every staircase.
/// </summary>
/// <remarks>
/// Deliberately free of Avalonia types and of <c>MapCanvas</c>: the whole feature is one decision,
/// and keeping it a pure function is what makes the interesting cases -- off, no floor, already
/// there -- testable without a window.
/// </remarks>
public static class FloorFollow
{
    /// <summary>
    /// The state that shows the floor this position is on, or null when nothing should change:
    /// the feature is off for this map, there is no map, or the view already shows exactly that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ground layer is switched <em>off</em> whenever a floor claims the position, because the
    /// artwork stacks floors as opaque geometry: an underground level is invisible behind the
    /// ground floor until that layer is off (the same reason Factory's ground checkbox exists).
    /// A position no floor claims is the base level, so there the ground layer comes back on and no
    /// named floor is drawn.
    /// </para>
    /// <para>
    /// Returning null when the view already matches is what keeps a burst of screenshots from
    /// re-applying the same state over and over -- each application is a full repaint and a push to
    /// the minimap and the WebSync client.
    /// </para>
    /// </remarks>
    public static FloorView? Decide(
        GameMap? map, GamePosition position, bool enabled,
        IReadOnlyCollection<string> activeFloors, bool showBaseLayer)
    {
        if (!enabled || map is null)
            return null;

        var floor = map.FloorAt(position);

        // No floor claims the position, so it is the base level: ground on, nothing else ticked.
        var target = floor is null
            ? new FloorView(true, [])
            : new FloorView(false, [floor.Name]);

        return IsShowing(activeFloors, showBaseLayer, target) ? null : target;
    }

    /// <summary>
    /// Compares the current layer state with a target as a set plus the ground flag. Ordinal
    /// throughout: floor names are the layer lookup keys, not display strings.
    /// </summary>
    private static bool IsShowing(
        IReadOnlyCollection<string> activeFloors, bool showBaseLayer, FloorView target)
    {
        if (showBaseLayer != target.ShowBaseLayer || activeFloors.Count != target.Floors.Count)
            return false;

        foreach (var name in target.Floors)
        {
            if (!activeFloors.Contains(name, StringComparer.Ordinal))
                return false;
        }

        return true;
    }
}

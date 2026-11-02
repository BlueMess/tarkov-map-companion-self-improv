using TarkovMapCompanion.Maps;
using TarkovMapCompanion.Screenshots;

namespace TarkovMapCompanion.Tools;

/// <summary>
/// Pushes real screenshots through the real position pipeline and says which floor the app would
/// show for each one.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the feature it checks cannot be verified from a test alone and cannot be
/// verified by hand at all without a game client: "follow the player's floor" only ever acts on a
/// screenshot, and the screenshots in <c>tests/screenshots</c> are the real thing. So the probe
/// takes the file names -- the app's entire input -- parses them with
/// <see cref="ScreenshotNameParser"/>, resolves the map and the floor with the same
/// <see cref="GameMap.FloorAt"/> the app calls, and then replays the decision in
/// <see cref="FloorFollow"/> over the frames in order.
/// </para>
/// <para>
/// Read-only and offline: no Avalonia, no network, no image decoding. The pixels are irrelevant
/// here; only the names are.
/// </para>
/// <para>
/// One thing it makes plain: a position alone does not name a map. <see cref="GameMap.ContainsPosition"/>
/// is a bounds test in each map's own base space, and the maps overlap heavily -- Factory's capture
/// is "inside" all thirteen of them. The app lives with that because it has a map selected and only
/// asks "is this fix on the map I am showing"; the probe therefore takes the map from the table, or
/// from an explicit argument, rather than inventing a winner.
/// </para>
/// </remarks>
public static class FloorProbe
{
    /// <summary>
    /// Runs over <paramref name="directory"/>, or over the repository's <c>tests/screenshots</c>.
    /// </summary>
    /// <returns>0 every expectation matched, 1 a mismatch, 2 the fixtures are missing.</returns>
    public static int Run(string? directory, TextWriter output) => Run(directory, null, output);

    /// <summary>
    /// Runs over <paramref name="directory"/>, treating <paramref name="mapName"/> as the map the
    /// app would have selected for any file the table does not describe.
    /// </summary>
    /// <remarks>
    /// The map argument is what makes this useful on somebody else's screenshot folder: it is the
    /// same piece of state the app has, and without it a fix that several maps' bounds claim has no
    /// floor to report.
    /// </remarks>
    public static int Run(string? directory, string? mapName, TextWriter output)
    {
        output.WriteLine("floor probe -- which floor the app resolves from real screenshot names");

        var folder = directory is { Length: > 0 }
            ? Path.GetFullPath(directory)
            : FloorProbeTable.FindFixtureDirectory();

        if (folder is null || !Directory.Exists(folder))
        {
            output.WriteLine(folder is null
                ? $"  could not find {FloorProbeTable.RelativeFolder} by walking up from "
                  + $"{Directory.GetCurrentDirectory()}"
                : $"  no such folder: {folder}");
            output.WriteLine("  usage: TarkovMapCompanion --floor-test [<screenshot folder>] [<map>]");
            return 2;
        }

        var catalog = MapCatalog.LoadEmbedded();

        var assumed = mapName is { Length: > 0 } ? catalog.Find(mapName) : null;
        if (assumed is null && mapName is { Length: > 0 })
        {
            output.WriteLine($"  no map called '{mapName}'. Available:");
            foreach (var candidate in catalog.Maps)
                output.WriteLine($"    {candidate.NormalizedName}");
            return 2;
        }

        output.WriteLine($"  folder  {folder}");
        if (assumed is not null)
            output.WriteLine($"  map     {assumed.NormalizedName} (assumed for files the table does not list)");

        var ordered = ReadFolder(folder, output);

        if (ordered.Count == 0)
        {
            output.WriteLine("  no readable screenshots in that folder; nothing to check");
            return 2;
        }

        output.WriteLine($"  fixtures {ordered.Count} readable, {catalog.Maps.Count} maps in the catalog");
        output.WriteLine();

        var rows = ordered.Select(fix => Inspect(catalog, fix, assumed)).ToList();

        WriteTable(rows, output);
        output.WriteLine();

        var walkFailures = WriteWalk(rows, output);
        output.WriteLine();

        return Report(rows, walkFailures, output);
    }

    /// <summary>A fixture, what the pipeline made of it, and whether that is what we expect.</summary>
    private sealed record Row(
        PlayerFix Fix,
        string Label,
        string Position,
        GameMap? Map,
        string MapText,
        string FloorText,
        FloorExpectation? Expected,
        bool Matches);

    private static Row Inspect(MapCatalog catalog, PlayerFix fix, GameMap? assumed)
    {
        // The label is the fixture's own date/time stamp plus the map, which is how the README
        // names the frames. The raw name is 100+ characters of quaternion and unreadable in a table.
        var stamp = fix.FileName.Split('_')[0];
        var expected = FloorProbeTable.For(fix.FileName);

        var candidates = catalog.Maps.Where(m => m.ContainsPosition(fix.Position)).ToArray();

        // Which map this fix belongs to, in the app's own terms. The table first, because that is
        // the documented answer and a drifting footprint must show up as a mismatch rather than be
        // silently re-labelled; then the caller's map, which is what the app would have selected;
        // and only then a bounds-based guess, which is only worth anything when it is unique.
        var documented = expected is null ? null : catalog.Find(expected.MapName);
        var map = documented is not null && documented.ContainsPosition(fix.Position)
            ? documented
            : assumed is not null && assumed.ContainsPosition(fix.Position)
                ? assumed
                : candidates.Length == 1
                    ? candidates[0]
                    : null;

        var mapText = map switch
        {
            not null when ReferenceEquals(map, assumed) => $"{map.NormalizedName} (assumed)",
            not null => map.NormalizedName,
            _ when candidates.Length > 1 => $"{candidates.Length} maps",
            _ => "none",
        };

        // "base", not "ground": a diagnostic has to be unambiguous whatever the UI language, and
        // "ground" would read as the ground *layer*, which is a different thing (it is on for the
        // base level and off for a named floor).
        var floorText = map?.FloorAt(fix.Position)?.Name ?? (map is null ? "-" : "base");

        var matches = expected is not null
            && map is not null
            && floorText == (expected.FloorName ?? "base");

        return new Row(
            fix,
            $"{stamp} {expected?.MapName ?? map?.NormalizedName ?? "?"}",
            FormattableString.Invariant(
                $"{fix.Position.X:F2}, {fix.Position.Y:F2}, {fix.Position.Z:F2}"),
            map,
            mapText,
            floorText,
            expected,
            matches);
    }

    private static List<PlayerFix> ReadFolder(string folder, TextWriter output)
    {
        var fixes = new List<PlayerFix>();

        foreach (var path in Directory.EnumerateFiles(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (ScreenshotNameParser.TryParse(path, out var fix))
            {
                fixes.Add(fix);
                continue;
            }

            // Anything a game screenshot could be named, that the parser refuses, is worth saying
            // out loud -- silence here looks exactly like "the app ignored my file". Everything
            // else (this README, a stray .txt) is simply not ours.
            var extension = Path.GetExtension(path);
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine($"  skipped {Path.GetFileName(path)}: the name does not parse");
            }
        }

        // Same ordering the watcher uses: filenames only carry minutes, so the in-raid clock breaks
        // ties and a walk within one minute still replays in the right order.
        return RaidSession.InChronologicalOrder(fixes).ToList();
    }

    private static void WriteTable(IReadOnlyList<Row> rows, TextWriter output)
    {
        var labelWidth = Math.Max(7, rows.Max(r => r.Label.Length));
        var mapWidth = Math.Max(3, rows.Max(r => r.MapText.Length));

        output.WriteLine(
            $"  {"fixture".PadRight(labelWidth)}  {"map".PadRight(mapWidth)}  "
            + $"{"x, y, z".PadRight(24)}  {"floor".PadRight(10)}  result");

        foreach (var row in rows)
        {
            var result = row.Expected is null
                ? "(no expectation)"
                : row.Matches
                    ? "ok"
                    : $"FAIL expected {row.Expected.FloorName ?? "base"}";

            output.WriteLine(
                $"  {row.Label.PadRight(labelWidth)}  {row.MapText.PadRight(mapWidth)}  "
                + $"{row.Position.PadRight(24)}  {row.FloorText.PadRight(10)}  {result}");
        }
    }

    /// <summary>
    /// Replays the frames through <see cref="FloorFollow.Decide"/> in order, so the state machine is
    /// visible rather than asserted: a floor gets switched to, the same state again changes
    /// nothing, and a base-level position brings the ground layer back.
    /// </summary>
    /// <returns>How many decisions did not come out as claimed.</returns>
    private static int WriteWalk(IReadOnlyList<Row> rows, TextWriter output)
    {
        output.WriteLine("  raid walk (FloorFollow.Decide, enabled, starting from floors=[] base=True)");

        var activeFloors = new List<string>();
        var showBaseLayer = true;
        GameMap? shownMap = null;
        var failures = 0;
        var switches = 0;
        var noOps = 0;
        var restored = 0;

        foreach (var row in rows)
        {
            if (row.Map is not { } map)
            {
                output.WriteLine(
                    $"  {row.Label}: {row.MapText} claim this position, so nothing to follow "
                    + "(pass a map name)");
                continue;
            }

            // A map switch rebuilds the canvas's layer state from scratch: only the floors the data
            // marks ShownByDefault are ticked (none of the maps here has one) and the ground layer
            // goes back to the user's own setting, which is on. Following the player never writes
            // either of those, so this is the state a new map starts from. It is also why the five
            // repository frames cannot show a return to the ground: each one is on a new map.
            var switched = !ReferenceEquals(map, shownMap);
            if (switched)
            {
                shownMap = map;
                activeFloors.Clear();
                showBaseLayer = true;
            }

            var view = FloorFollow.Decide(map, row.Fix.Position, enabled: true, activeFloors, showBaseLayer);

            if (view is not { } applied)
            {
                noOps++;
                output.WriteLine(
                    $"  {row.Label}: no change{(switched ? "  (new map, layers reset to the ground)" : "")}");
                continue;
            }

            switches++;
            if (applied.Floors.Count == 0)
                restored++;

            output.WriteLine($"  {row.Label}: {Describe(applied)}");

            activeFloors.Clear();
            activeFloors.AddRange(applied.Floors);
            showBaseLayer = applied.ShowBaseLayer;
        }

        // The last state, asked once more with the last position: what a burst of screenshots on one
        // floor must not turn into. Each application is a full repaint plus a push to the minimap and
        // every WebSync client, so "already showing that" has to come back null.
        if (rows.LastOrDefault(r => r.Map is not null) is { } last)
        {
            var repeat = FloorFollow.Decide(
                last.Map!, last.Fix.Position, enabled: true, activeFloors, showBaseLayer);

            if (repeat is not null)
                failures++;

            output.WriteLine(
                $"  {last.Label}: same state again -> "
                + (repeat is null
                    ? "no change  (a burst of frames on one floor repaints nothing)"
                    : "FAIL: it re-applied"));
        }

        // The leg the repository's own frames cannot reach, because each of them is on a different
        // map and a map switch resets the layers before the next frame is looked at: no frame comes
        // back up while a floor is still showing. So ask the state machine the question directly,
        // with a real ground-level position and the layer state the walk ended in. "The ground comes
        // back by itself" is the half a user notices when it is missing -- the map looks blank.
        if (restored == 0
            && rows.FirstOrDefault(r => r.FloorText == "base" && r.Map is not null) is { } ground)
        {
            var leg = FloorFollow.Decide(
                ground.Map!, ground.Fix.Position, enabled: true, activeFloors, showBaseLayer);

            if (leg is null)
            {
                failures++;
                output.WriteLine(
                    $"  {ground.Label}: a ground-level fix against that state -> FAIL: no change from "
                    + $"floors=[{string.Join(", ", activeFloors)}]");
            }
            else
            {
                output.WriteLine($"  {ground.Label}: a ground-level fix against that state -> {Describe(leg.Value)}");
            }
        }

        output.WriteLine(
            $"  summary  {rows.Count} frames, {switches} floor switch(es), {noOps} no-op(s), "
            + $"{restored} return(s) to the ground layer");
        return failures;
    }

    private static string Describe(FloorView view) =>
        $"apply -> floors=[{string.Join(", ", view.Floors)}] base={view.ShowBaseLayer}"
        + (view.Floors.Count > 0
            ? "  (ground off: the artwork is opaque, so the floor is behind it)"
            : "  (ground back on)");

    private static int Report(IReadOnlyList<Row> rows, int walkFailures, TextWriter output)
    {
        var listed = rows.Where(r => r.Expected is not null).ToList();
        var failures = listed.Where(r => !r.Matches).ToList();

        foreach (var row in failures)
        {
            output.WriteLine(
                $"  {row.Label}: expected {row.Expected!.FloorName ?? "base"} on "
                + $"{row.Expected.MapName}, resolved {row.FloorText} on {row.MapText}");
        }

        // A fixture the table names but the folder does not have means the table has drifted from
        // the repository, not that the feature is broken -- that is the "missing" exit code. But a
        // folder that holds none of the documented fixtures is somebody's own screenshot folder,
        // which is a supported thing to point this at rather than an error.
        var absent = FloorProbeTable.Expectations
            .Where(e => rows.All(r => !string.Equals(
                r.Fix.FileName, e.FileName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (absent.Count > 0 && listed.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"MISSING  {absent.Count} of {FloorProbeTable.Expectations.Count} "
                             + "documented fixtures are not in that folder:");

            foreach (var expectation in absent)
                output.WriteLine($"  {expectation.FileName}");

            return 2;
        }

        if (absent.Count > 0)
        {
            output.WriteLine(
                $"  note  {absent.Count} documented fixture(s) are not in this folder, so the "
                + "repository's five frames were not checked");
        }

        if (failures.Count > 0 || walkFailures > 0)
        {
            output.WriteLine();
            output.WriteLine(
                $"FAIL  {failures.Count} of {listed.Count} fixtures disagree with the table, "
                + $"{walkFailures} walk decision(s) did not hold");
            return 1;
        }

        var unresolved = rows.Count(r => r.Map is null);

        // Nothing could be resolved at all: unlike "some frames are outside the map you named", this
        // is a folder this probe cannot say anything about -- a table row or a map name is required.
        if (listed.Count == 0 && unresolved == rows.Count)
        {
            output.WriteLine();
            output.WriteLine(
                $"FAIL  0 of {rows.Count} frames could be tied to a map. Pass the map the app has "
                + "selected: --floor-test <folder> <map>");
            return 2;
        }

        output.WriteLine();
        output.WriteLine(listed.Count > 0
            ? $"PASS  {listed.Count} of {listed.Count} fixtures resolve to the expected floor "
              + "and the walk behaved"
            : $"PASS  {rows.Count - unresolved} of {rows.Count} frames resolved to a floor and the "
              + "walk behaved");

        if (unresolved > 0)
            output.WriteLine($"  ({unresolved} frame(s) had no map to resolve against)");

        return 0;
    }
}

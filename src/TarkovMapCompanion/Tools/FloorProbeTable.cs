namespace TarkovMapCompanion.Tools;

/// <summary>One real screenshot fixture and what the position pipeline must make of it.</summary>
/// <param name="FileName">The fixture's file name, exactly as it sits in <c>tests/screenshots</c>.</param>
/// <param name="MapName">Normalized name of the map that must claim the position.</param>
/// <param name="FloorName">
/// The floor that must claim it, or null for the base level -- which is the answer
/// <see cref="Maps.GameMap.FloorAt"/> gives as <c>null</c> and every renderer reads as "ground".
/// </param>
public sealed record FloorExpectation(string FileName, string MapName, string? FloorName);

/// <summary>
/// The five real Chinese-client frames, with the floor each one resolves to.
/// </summary>
/// <remarks>
/// <para>
/// One table, two consumers: the <c>--floor-test</c> diagnostic (which a human reads) and
/// <see cref="Tests"/>-side <c>RealScreenshotFloorTests</c> (which CI enforces). They must agree,
/// or the thing the owner checks by hand stops being the thing that runs in CI.
/// </para>
/// <para>
/// The expectations are behavioural, not derived: they are the answers the vendored map data
/// actually gives, confirmed by running this probe. A floor band or footprint that moves has to be
/// looked at deliberately rather than silently relocating the player.
/// </para>
/// </remarks>
public static class FloorProbeTable
{
    /// <summary>Where the fixtures live, relative to the repository root.</summary>
    public const string RelativeFolder = "tests/screenshots";

    /// <summary>
    /// Chronological order, which is also the order the app would see them in one session.
    /// </summary>
    public static IReadOnlyList<FloorExpectation> Expectations { get; } =
    [
        // Customs, y=1.63. No band claims it: 2nd Floor starts at 2.7 over the dorms and
        // Underground ends at 0.5, so this is the ground level.
        new(
            "2026-10-12[20-52]_618.08, 1.63, -102.96_-0.03896, -0.34227, -0.01421, 0.93869_9.17 (0).png",
            "customs",
            null),

        // Ground Zero, y=24.31 -- high, but between Garage [-1000,21] and 2nd Floor [26,32.3],
        // which leaves it on the ground level. Height alone is not enough on this map.
        new(
            "2026-10-13[19-48]_121.68, 24.31, -1.07_-0.00633, 0.79176, -0.00821, -0.61075_13.69 (0).png",
            "ground-zero",
            null),

        // Factory, y=-1.12: inside Tunnels [-10000,-1]. Factory's floors carry no footprints at
        // all, so height is the only discriminator.
        new(
            "2026-10-16[18-14]_-31.86, -1.12, 32.79_0.00681, 0.03618, -0.00027, 0.99932_15.47 (0).png",
            "factory",
            "Tunnels"),

        // Streets, y=2.56: every band is a bare height range, and the lowest one (2nd Floor)
        // starts at 10. So this is the ground level despite being an indoor-looking spot.
        new(
            "2026-10-17[11-32]_-150.62, 2.56, 500.42_0.00580, -0.79012, 0.00680, 0.61289_15.80 (0).png",
            "streets-of-tarkov",
            null),

        // Factory again, y=-1.13: Tunnels as well. Two independent frames landing in the tunnels is
        // what makes the floor-following feature worth having at all.
        new(
            "2026-10-18[15-16]_41.46, -1.13, -8.61_-0.00850, 0.99077, -0.07126, -0.11497_15.47 (0).png",
            "factory",
            "Tunnels"),
    ];

    /// <summary>The expectation for a file name, or null when the file is not one of the five.</summary>
    public static FloorExpectation? For(string? fileName) =>
        fileName is null
            ? null
            : Expectations.FirstOrDefault(e =>
                string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds <c>tests/screenshots</c> by walking up from the working directory and from the
    /// build output, or null when this is running somewhere without the repository.
    /// </summary>
    /// <remarks>
    /// <c>--render-test</c> plots fixes out of the <em>configured</em> screenshot folder (the
    /// user's own), so there was no repo-fixture lookup to reuse; this is the equivalent for the
    /// fixtures, and the reason the probe and its test share one implementation rather than each
    /// guessing a path of their own. Both entry points matter: <c>dotnet run --project …</c>
    /// starts in the repository root, while a test host starts in
    /// <c>tests/TarkovMapCompanion.Tests/bin/…</c>.
    /// </remarks>
    public static string? FindFixtureDirectory()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (FindFixtureDirectory(start) is { } found)
                return found;
        }

        return null;
    }

    private static string? FindFixtureDirectory(string start)
    {
        if (string.IsNullOrWhiteSpace(start))
            return null;

        var directory = new DirectoryInfo(start);

        // Deep enough for tests/bin/Debug/net8.0-windows10.0.19041.0/win-x64, shallow enough that
        // walking to the filesystem root is never attempted in earnest.
        for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName, "tests", "screenshots");

            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.png").Any())
                return candidate;
        }

        return null;
    }
}

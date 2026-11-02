using TarkovMapCompanion.Maps;
using TarkovMapCompanion.Screenshots;
using TarkovMapCompanion.Tools;
using Xunit;

namespace TarkovMapCompanion.Tests;

/// <summary>
/// Reads the five real screenshots in <c>tests/screenshots</c> with the app's own name parser and
/// pins the map and the floor each one resolves to.
/// </summary>
/// <remarks>
/// <para>
/// This is the first test in the repository that runs <see cref="ScreenshotNameParser"/> over the
/// real fixture <em>files</em>. The OCR tests deliberately feed captured OCR text instead of images,
/// and the watcher tests synthesize names in a temp folder; until now nothing checked that the names
/// the game actually wrote still parse and still land where the data says.
/// </para>
/// <para>
/// The fixtures are the Chinese-client reference frames (<c>tests/screenshots/README.md</c>), so the
/// same files serve two purposes: the delivery panel of a Chinese client, and a genuine set of
/// positions. Only the file <em>names</em> are read here -- no pixels, no OCR -- which is also why
/// these assertions are cheap enough to run in CI.
/// </para>
/// <para>
/// The expectations live in <see cref="FloorProbeTable"/>, which the <c>--floor-test</c> diagnostic
/// prints from as well: what a human reads there and what CI enforces here cannot drift apart.
/// </para>
/// </remarks>
public sealed class RealScreenshotFloorTests
{
    private static readonly MapCatalog Catalog = MapCatalog.LoadEmbedded();

    public static IEnumerable<object?[]> Fixtures() =>
        FloorProbeTable.Expectations.Select(e => new object?[] { e.FileName, e.MapName, e.FloorName });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void RealScreenshot_ResolvesToTheDocumentedMapAndFloor(
        string fileName, string mapName, string? floorName)
    {
        var path = Path.Combine(FixtureFolder(), fileName);
        Assert.True(File.Exists(path), $"fixture missing: {path}");

        Assert.True(
            ScreenshotNameParser.TryParse(path, out var fix),
            $"{fileName} no longer parses. The name format is the app's only input, so a change here "
            + "means positions stop arriving at all.");

        var map = Catalog.Find(mapName);
        Assert.NotNull(map);

        // ContainsPosition is the app's own membership test (base space, so a corrected bounds still
        // counts), and it is what decides whether a screenshot even offers that map.
        Assert.True(
            map!.ContainsPosition(fix!.Position),
            $"'{mapName}' no longer contains {fix.Position}. A bounds correction or a data refresh "
            + "moved the map out from under its own screenshot.");

        // FloorAt is the single floor primitive: the player marker, the WebSync player field and
        // floor following all call it, so this pins the answer all three get.
        Assert.Equal(floorName, map.FloorAt(fix.Position)?.Name);
    }

    [Fact]
    public void TheTableAndTheFolderAgreeOnWhichFramesExist()
    {
        var present = Directory
            .EnumerateFiles(FixtureFolder(), "*.png")
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

        var absent = FloorProbeTable.Expectations
            .Where(e => !present.Contains(e.FileName, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.FileName)
            .ToList();

        Assert.True(
            absent.Count == 0,
            "the probe table names fixtures that are not in tests/screenshots:"
            + Environment.NewLine + string.Join(Environment.NewLine, absent));

        // The other direction, and the reason this is a test rather than a comment: a new frame
        // dropped into the folder is exactly the case where nobody thinks to write down what floor
        // it is on. Fail here rather than let the probe quietly report "(no expectation)".
        var undocumented = present
            .Where(name => FloorProbeTable.For(name) is null)
            .ToList();

        Assert.True(
            undocumented.Count == 0,
            "these fixtures have no row in FloorProbeTable, so nothing pins what they resolve to:"
            + Environment.NewLine + string.Join(Environment.NewLine, undocumented));
    }

    [Fact]
    public void TheTwoFactoryFrames_SwitchTheViewToTheTunnels()
    {
        var factory = Catalog.Find("factory")!;

        var frames = FloorProbeTable.Expectations
            .Where(e => e.MapName == "factory")
            .Select(e => Path.Combine(FixtureFolder(), e.FileName))
            .Where(ScreenshotNameParser.IsScreenshotFileName)
            .Select(path =>
            {
                Assert.True(ScreenshotNameParser.TryParse(path, out var fix));
                return fix!;
            })
            .ToList();

        Assert.Equal(2, frames.Count);

        foreach (var fix in frames)
        {
            // From the state a freshly loaded map starts in: ground only, no floor ticked.
            var view = FloorFollow.Decide(
                factory, fix.Position, enabled: true, activeFloors: [], showBaseLayer: true);

            Assert.NotNull(view);
            Assert.Equal(["Tunnels"], view!.Value.Floors);

            // The ground layer has to go off in the same step: Factory's artwork stacks the levels,
            // so the tunnels stay invisible behind the ground floor otherwise.
            Assert.False(view.Value.ShowBaseLayer);
        }
    }

    [Fact]
    public void TheProbe_ReportsSuccessOnTheRepositoryFixtures()
    {
        // The exit code is the contract CI and the owner's shell rely on: 0 means every documented
        // fixture matched, and the walking state machine behaved.
        using var output = new StringWriter();

        Assert.Equal(0, FloorProbe.Run(directory: null, output));
        Assert.Contains("PASS", output.ToString());
    }

    private static string FixtureFolder() =>
        FloorProbeTable.FindFixtureDirectory()
        ?? throw new InvalidOperationException(
            $"could not find {FloorProbeTable.RelativeFolder} above "
            + $"{AppContext.BaseDirectory}; run the tests from inside the repository");
}

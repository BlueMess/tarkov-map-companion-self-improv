using TarkovMapCompanion.Maps;
using TarkovMapCompanion.Rendering;
using TarkovMapCompanion.Settings;
using Xunit;

namespace TarkovMapCompanion.Tests;

/// <summary>
/// The place-names layer is bound to the floor toggles: a label's <c>top</c>/<c>bottom</c> is a
/// game-world height band, so it decides which floors the name is drawn on.
/// </summary>
/// <remarks>
/// These assert against the vendored catalog, so a data refresh that reshapes a height band or a
/// footprint has to be looked at rather than silently moving a name onto another floor. Only ASCII
/// names are used: interchange carries non-ASCII store names and a test that spelled one would be
/// a test of the file encoding, not of the binding.
/// </remarks>
public sealed class LandmarkFloorTests
{
    private static readonly MapCatalog Catalog = MapCatalog.LoadEmbedded();

    private static MapLabel Label(string mapName, string text) =>
        Catalog.Find(mapName)!.Labels.First(label => label.Text == text);

    // ---- the resolution itself (GameMap -> MapLabel.Floors) --------------------------------

    [Fact]
    public void Interchange_KibaIsOnTheSecondFloor()
    {
        // Band [25,33] against 2nd Floor's [25,34]: one floor claims it and 3rd Floor's [34,1000]
        // starts above the top of the band.
        var kiba = Label("interchange", "Kiba");

        Assert.True(kiba.HasFloorData);
        Assert.Equal(["2nd Floor"], kiba.Floors);
    }

    [Fact]
    public void Interchange_TarkovstarReachesTheThirdFloorAndOnlyTheThirdFloor()
    {
        // top 999 / bottom 34: the band starts exactly at the height the two mall floors share, and
        // 2nd Floor's [25,34] *ends* there. Touch at a boundary is not overlap, so this name is the
        // 3rd Floor's alone -- which is what keeps it off the 2nd-floor view. Read inclusively it
        // would be drawn on both, and so would the 29 other shop names that sit at 34 as well.
        var tarkovstar = Label("interchange", "Tarkovstar");

        Assert.True(tarkovstar.HasFloorData);
        Assert.Equal(["3rd Floor"], tarkovstar.Floors);
    }

    [Fact]
    public void Interchange_PowerStationIsBaseLevelDespiteABand()
    {
        // Band [-1,10] sits in the gap under the mall's [25,34], so the source does declare a height
        // and yet no floor claims it: that is the base level, and it must not be read as "unknown".
        var powerStation = Label("interchange", "Power Station");

        Assert.True(powerStation.HasFloorData);
        Assert.Empty(powerStation.Floors);
    }

    [Fact]
    public void TheLab_AdministrativeOfficeIsOnTheSecondLevel()
    {
        // Band [3,1000]. Technical is [-10000,-0.9] over the whole map, so only the band test keeps
        // this name off it -- the footprint alone would have claimed it.
        var office = Label("the-lab", "Administrative Office");

        Assert.Equal(["Second Level"], office.Floors);
    }

    [Fact]
    public void TheLab_ParkingIsBaseLevel()
    {
        // Band [-0.8,2]: just above Technical's ceiling of -0.9 and just below Second Level's 3.
        var parking = Label("the-lab", "Parking");

        Assert.True(parking.HasFloorData);
        Assert.Empty(parking.Floors);
    }

    [Fact]
    public void GroundZero_OasisIsOnTheSecondFloorWithoutStraddlingTheGarage()
    {
        // Band [22,30]. Garage ends at 21 and 2nd Floor starts at 28, so the band's midpoint (26)
        // belongs to no floor at all -- a single-height test would put this name on the base level.
        // Only a band overlap finds 2nd Floor's [28,32.3], and that is the whole reason
        // MapFloor.CoversBand exists.
        var oasis = Label("ground-zero", "Oasis");

        Assert.Equal(["2nd Floor"], oasis.Floors);
    }

    [Fact]
    public void GroundZero_ScienceOfficeAtOneHeightIsOnTheFloorThatStartsThere()
    {
        // top = bottom = 28, exactly 2nd Floor's own [28,32.3] floor height: a degenerate band is a
        // point, and a point on a floor's floor height is on that floor. This is the case the strict
        // rule must not lose while it is keeping 34 out of 2nd Floor.
        var office = Label("ground-zero", "Science Office");

        Assert.True(office.HasFloorData);
        Assert.Equal(["2nd Floor"], office.Floors);
    }

    [Fact]
    public void Interchange_NoNameBelongsToBothMallFloors()
    {
        // The regression the boundary rule exists for. 2nd Floor is [25,34] and 3rd Floor [34,1000],
        // so a name at the shared height 34 -- the shop names, Tarkovstar -- would match both under
        // an inclusive overlap test and stay drawn over the 2nd-floor view. If this ever fails,
        // either the rule or a data refresh moved the boundary; both need looking at.
        var interchange = Catalog.Find("interchange")!;
        var both = interchange.Labels
            .Where(label => label.Floors.Contains("2nd Floor") && label.Floors.Contains("3rd Floor"))
            .Select(label => label.Text)
            .ToArray();

        Assert.Empty(both);
        Assert.Contains(interchange.Labels, label => label.Floors.Count == 1 && label.Floors[0] == "3rd Floor");
    }

    [Fact]
    public void Customs_DormsHasNoBandAtAll()
    {
        // Most labels carry no height data. They are never filtered, or the layer would vanish on
        // customs, streets, woods and lighthouse.
        var dorms = Label("customs", "Dorms");

        Assert.False(dorms.HasFloorData);
        Assert.Empty(dorms.Floors);
    }

    [Fact]
    public void Reserve_OneSidedBandsArePointsNotHalfInfiniteSlabs()
    {
        // All 27 reserve names carry `bottom: -6` and no `top`, and -6 is that map's own ground
        // height. Read as a point (see GameMap.FloorsOverlappingBand) they are base level; read as an
        // unbounded slab, 12 of them -- tarmac, the pawns, this one -- land on reserve's 2nd-4th
        // floors. The point can still reach a floor when that height really is inside one: Dome's -6
        // falls in the Bunkers 'dome tunnels' extent, which reaches up to 18.
        var kBuildings = Label("reserve", "K Buildings");
        var dome = Label("reserve", "Dome");

        Assert.True(kBuildings.HasFloorData);
        Assert.Empty(kBuildings.Floors);

        Assert.True(dome.HasFloorData);
        Assert.Equal(["Bunkers"], dome.Floors);
    }

    // ---- the filter the overlay applies ---------------------------------------------------

    [Fact]
    public void OneFloorTicked_HidesNamesOnOtherFloorsButKeepsBaseLevel()
    {
        // 2nd Floor is the only floor ticked. A name on the 3rd Floor must go, a name on the 2nd
        // must stay, and a name no floor claims is the base level, which is still showing.
        var overlay = new LandmarkOverlay(Catalog.Find("interchange")!)
        {
            ActiveFloors = ["2nd Floor"],
            ShowBaseLayer = true,
        };

        Assert.True(overlay.IsOnActiveFloor(Label("interchange", "Kiba")));
        Assert.False(overlay.IsOnActiveFloor(Label("interchange", "Tarkovstar")));

        // No floor claims it, so it is the base level.
        Assert.True(overlay.IsOnActiveFloor(Label("the-lab", "Parking")));
    }

    [Fact]
    public void Tarkovstar_IsHiddenWhileTheSecondFloorIsShownAlone()
    {
        // The other half of the boundary rule as the user sees it: with the ground off and only
        // 2nd Floor ticked, a 3rd-floor name is not drawn; ticking its own floor brings it back.
        var tarkovstar = Label("interchange", "Tarkovstar");

        foreach (var (floor, expected) in new[] { ("2nd Floor", false), ("3rd Floor", true) })
        {
            var overlay = new LandmarkOverlay(Catalog.Find("interchange")!)
            {
                ActiveFloors = [floor],
                ShowBaseLayer = false,
            };

            Assert.Equal(expected, overlay.IsOnActiveFloor(tarkovstar));
        }
    }

    [Fact]
    public void NoFloorTickedAndNoGround_HidesOnlyNamesThatHaveAFloor()
    {
        var overlay = new LandmarkOverlay(Catalog.Find("customs")!)
        {
            ActiveFloors = [],
            ShowBaseLayer = false,
        };

        Assert.False(overlay.IsOnActiveFloor(Label("the-lab", "Parking")));

        // A name with no band cannot be placed, so it is never hidden -- this is what keeps the
        // layer alive on the maps that ship no height data at all.
        Assert.True(overlay.IsOnActiveFloor(Label("customs", "Dorms")));
    }

    [Fact]
    public void NothingPushedYet_DrawsEveryName()
    {
        // ActiveFloors is null until the session pushes the canvas's selection, and the render
        // harness relies on that: an overlay that defaulted to an empty set would draw nothing.
        var overlay = new LandmarkOverlay(Catalog.Find("interchange")!) { ActiveFloors = null };

        Assert.True(overlay.IsOnActiveFloor(Label("interchange", "Kiba")));
        Assert.True(overlay.IsOnActiveFloor(Label("interchange", "Tarkovstar")));
        Assert.True(overlay.IsOnActiveFloor(Label("interchange", "Power Station")));
        Assert.True(overlay.IsOnActiveFloor(Label("customs", "Dorms")));
    }

    // ---- the session wiring ---------------------------------------------------------------

    [Fact]
    public void ApplyFloorStateReachesThePlaceNames()
    {
        // The session is the only path the windows have to the overlays, so a missing push here
        // would leave the names filtered by whatever the previous map left behind.
        var settings = new AppSettings
        {
            AllowNetwork = false,
            CurrentMap = "interchange",
            CullMode = CullMode.Off,
            ScreenshotFolder = Path.Combine(Path.GetTempPath(), "tmc-landmarks-noshots"),
            ShowLandmarks = true,
        };

        using var session = new MapSession(settings, Catalog);
        session.ApplyFloorState(["3rd Floor"], showBaseLayer: false);

        Assert.Equal(["3rd Floor"], session.Landmarks.ActiveFloors!);
        Assert.False(session.Landmarks.ShowBaseLayer);
        Assert.True(session.Landmarks.IsOnActiveFloor(Label("interchange", "Tarkovstar")));
        Assert.False(session.Landmarks.IsOnActiveFloor(Label("interchange", "Kiba")));
    }
}

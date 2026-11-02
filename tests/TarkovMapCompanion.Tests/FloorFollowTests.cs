using TarkovMapCompanion.Maps;
using Xunit;

namespace TarkovMapCompanion.Tests;

/// <summary>
/// Pins <see cref="FloorFollow.Decide"/>, the decision behind "after a screenshot, show the floor the
/// player is on".
/// </summary>
/// <remarks>
/// The three cases that matter are all decisions rather than drawing: the switch being off, a
/// position on the base level, and the view already showing the right floor. The last one is what
/// stops a burst of screenshots re-applying the same state -- and repainting, and pushing to the
/// minimap and the WebSync client -- on every frame.
/// </remarks>
public sealed class FloorFollowTests
{
    private static readonly MapCatalog Catalog = MapCatalog.LoadEmbedded();

    // The dorms footprint, at a height both 2nd Floor [2.7,6.5] and 3rd Floor [5.7,1000] claim;
    // declaration order resolves it to 2nd Floor (see PlayerFloorTests).
    private static readonly GamePosition CustomsDormsSecondFloor = new(200, 4.0, 160);

    // Factory's floor bands are Tunnels [-10000,-1], 2nd [3,6] and 3rd [6,10000], so this height is
    // claimed by no floor at all -- the base level.
    private static readonly GamePosition FactoryGroundLevel = new(-100, 0, -50);

    [Fact]
    public void SwitchOff_DoesNothingEvenWhenThePositionBelongsToAFloor()
    {
        var customs = Catalog.Find("customs")!;

        // The position does claim a floor; the switch is the only thing keeping the view put.
        Assert.NotNull(customs.FloorAt(CustomsDormsSecondFloor));

        Assert.Null(FloorFollow.Decide(
            customs, CustomsDormsSecondFloor, enabled: false,
            activeFloors: [], showBaseLayer: true));
    }

    [Fact]
    public void SwitchOn_BaseLayerOnly_SwitchesToTheSingleFloorAndTurnsTheGroundOff()
    {
        var customs = Catalog.Find("customs")!;

        var view = FloorFollow.Decide(
            customs, CustomsDormsSecondFloor, enabled: true,
            activeFloors: [], showBaseLayer: true);

        Assert.NotNull(view);
        Assert.Equal(new[] { "2nd Floor" }, view!.Value.Floors);

        // The ground layer has to go off: the artwork is opaque, so the floor is invisible behind it.
        Assert.False(view.Value.ShowBaseLayer);
    }

    [Fact]
    public void SwitchOn_AlreadyShowingThatFloor_ChangesNothing()
    {
        var customs = Catalog.Find("customs")!;

        Assert.Null(FloorFollow.Decide(
            customs, CustomsDormsSecondFloor, enabled: true,
            activeFloors: ["2nd Floor"], showBaseLayer: false));
    }

    [Fact]
    public void SwitchOn_SeveralFloorsTicked_NormalizesToTheOneFloorThePlayerIsOn()
    {
        var customs = Catalog.Find("customs")!;

        var view = FloorFollow.Decide(
            customs, CustomsDormsSecondFloor, enabled: true,
            activeFloors: ["2nd Floor", "3rd Floor"], showBaseLayer: true);

        Assert.NotNull(view);
        Assert.Equal(new[] { "2nd Floor" }, view!.Value.Floors);
        Assert.False(view.Value.ShowBaseLayer);
    }

    [Fact]
    public void FloorNamesAreComparedOrdinal()
    {
        var customs = Catalog.Find("customs")!;

        // Floor names are the layer lookup keys, so a case-folded match would be a different layer.
        // The view is not showing "2nd Floor", so this must produce a change rather than null.
        var view = FloorFollow.Decide(
            customs, CustomsDormsSecondFloor, enabled: true,
            activeFloors: ["2nd floor"], showBaseLayer: false);

        Assert.NotNull(view);
        Assert.Equal(["2nd Floor"], view!.Value.Floors);
    }

    [Fact]
    public void SwitchOn_GroundLevel_ShowsTheBaseLayerAndNoFloor()
    {
        var factory = Catalog.Find("factory")!;

        var view = FloorFollow.Decide(
            factory, FactoryGroundLevel, enabled: true,
            activeFloors: ["Tunnels"], showBaseLayer: false);

        Assert.NotNull(view);
        Assert.Empty(view!.Value.Floors);

        // Coming back up is the other half of the feature: the ground has to return by itself.
        Assert.True(view.Value.ShowBaseLayer);
    }

    [Fact]
    public void SwitchOn_AlreadyOnTheBaseLevel_ChangesNothing()
    {
        var factory = Catalog.Find("factory")!;

        Assert.Null(FloorFollow.Decide(
            factory, FactoryGroundLevel, enabled: true,
            activeFloors: [], showBaseLayer: true));
    }

    [Fact]
    public void NoMap_ChangesNothing()
    {
        Assert.Null(FloorFollow.Decide(
            map: null, CustomsDormsSecondFloor, enabled: true,
            activeFloors: [], showBaseLayer: false));
    }

    [Fact]
    public void MapWithNoFloors_IsAlwaysTheBaseLevel()
    {
        var woods = Catalog.Find("woods")!;
        Assert.Empty(woods.Floors);

        // A map whose data declares no floors can never put the player on one, so the only move is
        // back to (or staying on) the ground layer.
        var view = FloorFollow.Decide(
            woods, new GamePosition(0, 0, 0), enabled: true,
            activeFloors: ["Underground"], showBaseLayer: false);

        Assert.NotNull(view);
        Assert.Empty(view!.Value.Floors);
        Assert.True(view.Value.ShowBaseLayer);

        Assert.Null(FloorFollow.Decide(
            woods, new GamePosition(0, 0, 0), enabled: true,
            activeFloors: [], showBaseLayer: true));
    }
}

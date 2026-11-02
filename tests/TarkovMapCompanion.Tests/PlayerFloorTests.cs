using TarkovMapCompanion.Maps;
using Xunit;

namespace TarkovMapCompanion.Tests;

/// <summary>
/// Pins <see cref="GameMap.FloorAt"/>, the single primitive three call sites now share: the player
/// marker's floor dimming, the WebSync player's floor field, and anything that later needs to know
/// which level a position belongs to.
/// </summary>
/// <remarks>
/// These assert against the vendored map data, so a refresh that reshapes a height band or a
/// footprint has to be looked at rather than silently changing which floor the player is put on.
/// </remarks>
public sealed class PlayerFloorTests
{
    private static readonly MapCatalog Catalog = MapCatalog.LoadEmbedded();

    [Fact]
    public void CustomsDorms_OverlappingBandsResolveByDeclarationOrder()
    {
        // Both floors claim the dorms footprint at this height: 2nd Floor's [2.7, 6.5] and 3rd
        // Floor's [5.7, 1000]. "First one wins" is the rule the data was authored against, so the
        // overlap resolves to 2nd Floor rather than by any best-fit notion.
        var customs = Catalog.Find("customs")!;

        Assert.Equal("2nd Floor", customs.FloorAt(new GamePosition(200, 4.0, 160))?.Name);
        Assert.Equal("3rd Floor", customs.FloorAt(new GamePosition(200, 8.0, 160))?.Name);
    }

    [Fact]
    public void Factory_WholeMapBandsResolveByHeightAlone()
    {
        // Factory's floors carry no footprints at all, so height is the only discriminator.
        var factory = Catalog.Find("factory")!;

        Assert.Equal("Tunnels", factory.FloorAt(new GamePosition(-100, -20, -50))?.Name);
        Assert.Equal("3rd Floor", factory.FloorAt(new GamePosition(-100, 50, -50))?.Name);
        Assert.Equal("2nd Floor", factory.FloorAt(new GamePosition(-100, 4, -50))?.Name);
    }

    [Fact]
    public void Factory_BaseLevelHeightIsClaimedByNoFloor()
    {
        // The floors are Tunnels [-10000,-1], 2nd [3,6] and 3rd [6,10000], so the gap between the
        // tunnels and the second floor is the ground level. Null is what the renderers read as
        // "base layer", so this is the case that keeps the ground floor drawable.
        var factory = Catalog.Find("factory")!;

        Assert.Null(factory.FloorAt(new GamePosition(-100, 0, -50)));
    }

    [Fact]
    public void Streets_ResolvesUndergroundAndUpperFloorsWithoutFootprints()
    {
        // Every streets floor is a bare height band covering the whole map.
        var streets = Catalog.Find("streets-of-tarkov")!;

        Assert.Equal("Underground", streets.FloorAt(new GamePosition(0, -20, 0))?.Name);
        Assert.Equal("2nd Floor", streets.FloorAt(new GamePosition(0, 12, 0))?.Name);
    }

    [Fact]
    public void Interchange_OutsideTheMallFootprintFallsBackToTheBaseLevel()
    {
        // Interchange is the case where the height band alone is not enough: both upper floors are
        // restricted to the mall, so a position out in the open at the same height is ground level.
        var interchange = Catalog.Find("interchange")!;

        Assert.Equal("2nd Floor", interchange.FloorAt(new GamePosition(0, 30, 0))?.Name);
        Assert.Null(interchange.FloorAt(new GamePosition(500, 30, 500)));
    }

    [Fact]
    public void Woods_HasNoFloorsSoEverythingIsBaseLevel()
    {
        var woods = Catalog.Find("woods")!;

        Assert.Empty(woods.Floors);
        Assert.Null(woods.FloorAt(new GamePosition(0, 0, 0)));
        Assert.Null(woods.FloorAt(new GamePosition(-720.10, -48.62, 430.51)));
    }

    [Fact]
    public void Shoreline_RealCaptureSitsOnTheBaseLevel()
    {
        // (-720.10, -48.62, 430.51) is straight out of a real Shoreline screenshot (the same fix
        // MapCatalogTests pins its bounds check with). Its height is far below 2nd Floor's [-1, 2]
        // band and nowhere near the Underground footprints in the west wing or admin building, so
        // no floor claims it and the player is drawn on the base layer. Changing a shoreline
        // height band or footprint breaks this assertion first.
        var shoreline = Catalog.Find("shoreline")!;

        Assert.Null(shoreline.FloorAt(new GamePosition(-720.10, -48.62, 430.51)));
    }
}

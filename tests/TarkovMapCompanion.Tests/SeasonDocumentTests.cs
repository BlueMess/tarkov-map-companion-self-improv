using TarkovMapCompanion.Data;
using TarkovMapCompanion.Data.Models;
using TarkovMapCompanion.Maps;
using TarkovMapCompanion.Settings;
using Xunit;

namespace TarkovMapCompanion.Tests;

/// <summary>
/// Guards the season document layer: the embedded snapshot's parsing and map-name folding, and the
/// builder that turns a point into an amber (or dimmed) map marker.
/// </summary>
/// <remarks>
/// Self-contained on purpose. The shipped <c>season-documents.json</c> is written by a separate
/// pipeline, so these tests use their own fixture and never read that file; a broken regeneration
/// fails the store's own load test rather than these.
/// </remarks>
public sealed class SeasonDocumentTests : IDisposable
{
    private static readonly MapCatalog Catalog = MapCatalog.LoadEmbedded();

    private readonly LanguageScope _language = new(AppLanguage.English);

    public void Dispose() => _language.Dispose();

    /// <summary>
    /// Inline snapshot in the shipped schema. Deliberately mixes a confirmed and an unconfirmed
    /// point, a prose map name, and both key and floor metadata.
    /// </summary>
    private const string Fixture = """
    {
      "schemaVersion": 1,
      "source": "test fixture",
      "generatedAt": "2026-10-16T00:00:00.000Z",
      "points": [
        {
          "id": "doc_customs_001",
          "map": "customs",
          "nameZh": "财务文件·5号仓库 铁架子（1F）",
          "typeZh": "财务文件",
          "titleZh": "5号仓库 铁架子",
          "floor": "1F",
          "keyRequiredZh": null,
          "confirmed": true,
          "x": 478.13,
          "z": -88.2
        },
        {
          "id": "doc_customs_002",
          "map": "customs",
          "nameZh": "财务文件·未知点",
          "typeZh": "财务文件",
          "titleZh": "未知点",
          "floor": "2F",
          "keyRequiredZh": "黑边钥匙",
          "confirmed": false,
          "x": 100.5,
          "z": -20.0
        },
        {
          "id": "doc_gz_001",
          "map": "ground-zero",
          "nameZh": "财务文件·测试点 A",
          "typeZh": "财务文件",
          "titleZh": "测试点 A",
          "floor": "1F",
          "keyRequiredZh": null,
          "confirmed": true,
          "x": 10.0,
          "z": 20.0
        },
        {
          "id": "doc_lab_001",
          "map": "the-lab",
          "nameZh": "技术文件·实验区",
          "typeZh": "技术文件",
          "titleZh": "实验区",
          "floor": null,
          "keyRequiredZh": null,
          "confirmed": true,
          "x": 1.0,
          "z": 2.0
        }
      ]
    }
    """;

    private static MapDataStore LoadStore()
    {
        var store = new MapDataStore(
            new AppSettings { AllowNetwork = false },
            cacheDirectory: Path.Combine(Path.GetTempPath(), "tmc-tests-nocache", Guid.NewGuid().ToString("N")));

        store.LoadLocal();
        return store;
    }

    /// <summary>
    /// Schema v2 snapshot: the same points plus <c>floorKey</c>, which is the floor layer's name
    /// and null for the ground layer. Carries the other v2 metadata too, so an unknown field
    /// cannot silently be assumed inert.
    /// </summary>
    private const string FixtureV2 = """
    {
      "schemaVersion": 2,
      "source": "test fixture",
      "generatedAt": "2026-10-24T00:00:00.000Z",
      "points": [
        {
          "id": "doc_customs_upstairs",
          "map": "customs",
          "nameZh": "财务文件·二楼办公室",
          "nameEn": "Financial document, upstairs office",
          "slug": "financial-document-upstairs-office",
          "type": "financial",
          "origin": "KORD BREACH",
          "floor": "2F",
          "floorZh": "二楼",
          "floorKey": "2nd Floor",
          "keyRequiredZh": null,
          "confirmed": true,
          "x": 478.13,
          "z": -88.2
        },
        {
          "id": "doc_customs_ground",
          "map": "customs",
          "nameZh": "财务文件·地面帐篷",
          "nameEn": "Financial document, ground tent",
          "slug": "financial-document-ground-tent",
          "type": "financial",
          "origin": "KORD BREACH",
          "floor": null,
          "floorZh": null,
          "floorKey": null,
          "keyRequiredZh": null,
          "confirmed": true,
          "x": 100.5,
          "z": -20.0
        }
      ]
    }
    """;

    [Fact]
    public void StoreParsesTheSchemaAndBucketsPointsByMap()
    {
        var store = SeasonDocumentStore.FromJson(Fixture);

        Assert.Equal(2, store.CountFor("customs"));
        Assert.Equal(1, store.CountFor("ground-zero"));
        Assert.Equal(0, store.CountFor("woods"));

        var customs = store.FindFor("customs");
        Assert.Equal(2, customs.Count);
        Assert.Equal("财务文件·5号仓库 铁架子（1F）", customs.Single(p => p.Id == "doc_customs_001").NameZh);
        Assert.False(customs.Single(p => p.Id == "doc_customs_002").Confirmed);
    }

    [Fact]
    public void MapNamesAreFoldedSoProseFindsTheSlug()
    {
        var store = SeasonDocumentStore.FromJson(Fixture);

        // The snapshot keys on tarkov.dev slugs; the catalogue talks in prose.
        Assert.Equal(1, store.CountFor("Ground Zero"));
        Assert.Equal(1, store.CountFor("The Lab"));
        Assert.Equal(2, store.CountFor("Customs"));

        Assert.Equal(
            SeasonDocumentStore.NormalizeMap("ground-zero"),
            SeasonDocumentStore.NormalizeMap("Ground Zero"));
        Assert.Equal(
            SeasonDocumentStore.NormalizeMap("the-lab"),
            SeasonDocumentStore.NormalizeMap("The Lab"));
    }

    [Fact]
    public void AnEmptyOrMissingMapReturnsNothingRatherThanThrowing()
    {
        var store = SeasonDocumentStore.FromJson(Fixture);

        Assert.Empty(store.FindFor("does-not-exist"));
        Assert.Equal(0, store.CountFor("does-not-exist"));
    }

    [Fact]
    public void SchemaV1PointsAreAllGroundLevelBecauseTheyHaveNoFloorKey()
    {
        var store = SeasonDocumentStore.FromJson(Fixture);
        Assert.Equal(1, store.SchemaVersion);

        // The v1 file has a human-readable "1F"/"2F" in `floor` and no layer name at all. That
        // must not be mistaken for a floor: every point belongs to the ground layer, which is what
        // the layer did before floors were modelled.
        foreach (var map in new[] { "customs", "ground-zero", "the-lab" })
        {
            Assert.NotEmpty(store.FindFor(map));
            Assert.All(store.FindFor(map), point => Assert.Null(point.FloorKey));
        }
    }

    [Fact]
    public void SchemaV2PointsCarryTheirFloorKeyAndNullMeansGround()
    {
        var store = SeasonDocumentStore.FromJson(FixtureV2);
        Assert.Equal(2, store.SchemaVersion);

        var points = store.FindFor("customs");

        Assert.Equal("2nd Floor", points.Single(p => p.Id == "doc_customs_upstairs").FloorKey);
        Assert.Null(points.Single(p => p.Id == "doc_customs_ground").FloorKey);
    }

    [Fact]
    public void BuilderPassesTheFloorKeyThroughToTheMarker()
    {
        var data = new MapPoiData { NormalizedName = "customs" };
        var map = Catalog.Find("customs")!;
        var store = LoadStore();

        var pois = PoiBuilder.Build(map, data, store, notes: null, seasonDocuments: SeasonDocumentStore.FromJson(FixtureV2));

        var docs = pois.Where(p => p.Kind == PoiKind.SeasonDocuments).ToArray();
        Assert.Equal(2, docs.Length);

        // The layer name is drawn and filtered by, never displayed, so it has to survive the build
        // unchanged -- a label like "2F" would match no floor toggle in the window.
        Assert.Equal("2nd Floor", docs.Single(p => p.Id == "doc_customs_upstairs").FloorName);
        Assert.Null(docs.Single(p => p.Id == "doc_customs_ground").FloorName);
    }

    [Fact]
    public void BuilderLeavesEveryV1PointOnTheGroundLayer()
    {
        var data = new MapPoiData { NormalizedName = "customs" };
        var map = Catalog.Find("customs")!;
        var store = LoadStore();

        var pois = PoiBuilder.Build(map, data, store, notes: null, seasonDocuments: SeasonDocumentStore.FromJson(Fixture));

        Assert.All(
            pois.Where(p => p.Kind == PoiKind.SeasonDocuments),
            poi => Assert.Null(poi.FloorName));
    }

    [Fact]
    public void BuilderEmitsProjectedSeasonDocumentPois()
    {
        var data = new MapPoiData { NormalizedName = "customs" };
        var map = Catalog.Find("customs")!;
        var store = LoadStore();
        var season = SeasonDocumentStore.FromJson(Fixture);

        var pois = PoiBuilder.Build(map, data, store, notes: null, seasonDocuments: season);
        var docs = pois.Where(p => p.Kind == PoiKind.SeasonDocuments).ToArray();

        Assert.Equal(2, docs.Length);

        var confirmed = docs.Single(p => p.Id == "doc_customs_001");
        Assert.False(confirmed.IsUnverified);
        Assert.Equal(map.Projection.ToBase(478.13, -88.2), confirmed.Base);
        Assert.Equal(478.13, confirmed.Position.X, 6);
        Assert.Equal(0, confirmed.Position.Y, 6);
        Assert.Equal(-88.2, confirmed.Position.Z, 6);
    }

    [Fact]
    public void BuilderComposesNameAndDetailsFromTheChineseSource()
    {
        var data = new MapPoiData { NormalizedName = "customs" };
        var map = Catalog.Find("customs")!;
        var store = LoadStore();
        var season = SeasonDocumentStore.FromJson(Fixture);

        var pois = PoiBuilder.Build(map, data, store, notes: null, seasonDocuments: season);

        var confirmed = pois.Single(p => p.Id == "doc_customs_001");
        Assert.Equal("财务文件·5号仓库 铁架子（1F）", confirmed.Name);
        Assert.Equal("财务文件·5号仓库 铁架子（1F）", confirmed.NameZh);
        Assert.Equal(new[] { "财务文件", "楼层 1F" }, confirmed.Details);
        Assert.Equal(confirmed.Details, confirmed.DetailsZh);

        var unverified = pois.Single(p => p.Id == "doc_customs_002");
        Assert.Equal(new[] { "财务文件", "楼层 2F", "需钥匙：黑边钥匙" }, unverified.Details);
    }

    [Fact]
    public void UnconfirmedPointsAreKeptAndFlaggedNotFiltered()
    {
        var data = new MapPoiData { NormalizedName = "customs" };
        var map = Catalog.Find("customs")!;
        var store = LoadStore();
        var season = SeasonDocumentStore.FromJson(Fixture);

        var pois = PoiBuilder.Build(map, data, store, notes: null, seasonDocuments: season);

        // The unconfirmed point is a MapPoi, not a QuestMark: the quest-tracking checkbox list has
        // no seam here, so it survives and is marked unverified rather than dropped.
        var unverified = pois.Single(p => p.Kind == PoiKind.SeasonDocuments && p.IsUnverified);
        Assert.Equal("doc_customs_002", unverified.Id);

        // And the layer is on by default, so it is not gated behind any tracking switch either.
        Assert.True(new AppSettings().PoiLayers["SeasonDocuments"]);
    }

    [Fact]
    public void MigrateBackfillsTheLayerForAnOlderSettingsFile()
    {
        // A file written before the layer existed has a poiLayers map with no SeasonDocuments key.
        var settings = new AppSettings { Version = 2 };
        settings.PoiLayers.Remove("SeasonDocuments");

        settings.Migrate();

        Assert.True(settings.PoiLayers["SeasonDocuments"]);
    }

    [Fact]
    public void BuildingWithoutASeasonStoreStillWorks()
    {
        var data = new MapPoiData { NormalizedName = "customs" };
        var map = Catalog.Find("customs")!;
        var store = LoadStore();

        var pois = PoiBuilder.Build(map, data, store, notes: null, seasonDocuments: null);

        Assert.DoesNotContain(pois, p => p.Kind == PoiKind.SeasonDocuments);
    }
}

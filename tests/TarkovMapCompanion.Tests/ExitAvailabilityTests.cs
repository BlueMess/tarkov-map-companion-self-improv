using TarkovMapCompanion.Data;
using TarkovMapCompanion.Maps;
using TarkovMapCompanion.Vision;
using Xunit;

namespace TarkovMapCompanion.Tests;

/// <summary>
/// Matching read text to known exits, and refusing to when it would be a guess.
/// </summary>
public sealed class NameMatcherTests
{
    /// <summary>The real Customs exit list, which is where the hard cases come from.</summary>
    private static readonly string[] Customs =
    [
        "sniper roadblock", "ruaf roadblock", "warehouse 17", "warehouse 4",
        "boiler room basement co op", "crossroads", "dorms v ex", "old gas station",
        "trailer park", "trailer park workers shack", "zb 1011", "zb 013",
        "smugglers boat", "smugglers bunker zb 1012", "railroad to port",
        "railroad to tarkov", "railroad to military base",
        "transit to reserve", "transit to factory", "transit to interchange", "transit to shoreline",
    ];

    [Fact]
    public void RecoversFromTheReaderMisreadingAWord()
    {
        // The reader turns RUAF into RIJAF: it splits the U into an I and a J. This is the actual
        // error seen on real screenshots, and the whole reason matching is fuzzy.
        var match = NameMatcher.Match("RIJAF Roadblock", Customs);

        Assert.NotNull(match);
        Assert.Equal("ruaf roadblock", match.NormalizedName);
    }

    [Fact]
    public void PunctuationAndCaseDoNotMatter()
    {
        var match = NameMatcher.Match("Boiler Room Basement (Co-op)", Customs);

        Assert.NotNull(match);
        Assert.Equal("boiler room basement co op", match.NormalizedName);
    }

    [Fact]
    public void TellsApartNamesThatDifferOnlyInTheirNumber()
    {
        var seventeen = NameMatcher.Match("Warehouse 17", Customs);
        var four = NameMatcher.Match("Warehouse 4", Customs);

        Assert.Equal("warehouse 17", seventeen?.NormalizedName);
        Assert.Equal("warehouse 4", four?.NormalizedName);
    }

    [Fact]
    public void RefusesToChooseBetweenTwoEquallyCloseNames()
    {
        // A digit misread on Reserve's ZB-1011 leaves it exactly as close to ZB-1012. Guessing puts
        // a marker on the wrong side of the map; declining leaves every exit shown, which is safe.
        var match = NameMatcher.Match("ZB-101I", ["zb 1011", "zb 1012"]);

        Assert.Null(match);
    }

    [Fact]
    public void TwoExitsSharingADisplayNameIsNotAnAmbiguity()
    {
        // Customs lists a PMC and a Scav "RUAF Roadblock". One place, two rule sets -- not a
        // decision to be made, and it must not be treated as one.
        var match = NameMatcher.Match("RUAF Roadblock", ["ruaf roadblock", "ruaf roadblock", "crossroads"]);

        Assert.NotNull(match);
        Assert.Equal("ruaf roadblock", match.NormalizedName);
    }

    [Theory]
    [InlineData("Compass")]
    [InlineData("R1500")]
    [InlineData("MP-133")]
    [InlineData("")]
    public void StrayHudTextMatchesNothing(string text)
    {
        Assert.Null(NameMatcher.Match(text, Customs));
    }

    [Fact]
    public void NormalizeCollapsesEverythingThatIsNotALetterOrDigit()
    {
        Assert.Equal("smugglers boat", NameMatcher.Normalize("  Smugglers'   Boat!  "));
        Assert.Equal("", NameMatcher.Normalize("   "));
        Assert.Equal("", NameMatcher.Normalize(null));
    }

    [Fact]
    public void CjkSpacesBetweenCharactersAreNotSeparators()
    {
        // Measured zh-Hans engine output: a space between every character. Treated as separators on
        // one side only, no Chinese name can ever equal its candidate.
        Assert.Equal("宿舍载具撤离点", NameMatcher.Normalize("宿 舍 载 具 撤 离 点"));
        Assert.Equal("铁路通道信号弹", NameMatcher.Normalize("铁 路 通 道 （ 信 号 弹 ）"));

        // The candidates are inconsistent about the space ("Mira 大道" has one, "Pinewood地下室"
        // does not), which is exactly why both sides have to drop it.
        Assert.Equal("mira大道信号弹", NameMatcher.Normalize("Mira 大道（信号弹）"));
        Assert.Equal("mira大道信号弹", NameMatcher.Normalize("Mira大道（信号弹）"));
    }

    [Fact]
    public void LatinOnlyStringsKeepTheirSpaces()
    {
        // The English path must stay byte-identical: only a CJK read gets the space treatment.
        Assert.Equal("boiler room basement co op", NameMatcher.Normalize("Boiler Room Basement (Co-op)"));
        Assert.Equal("transit to reserve", NameMatcher.Normalize("Transit to Reserve"));
    }

    [Fact]
    public void AFourCharacterCjkNameWithOneWrongCharacterStillMatches()
    {
        // 1 - 1/4 = 0.75, which the English floor rejects and a short-name-prone feature cannot.
        var match = NameMatcher.Match("十宇路口", ["十字路口"]);

        Assert.NotNull(match);
        Assert.Equal("十字路口", match.NormalizedName);
    }

    [Fact]
    public void AThreeCharacterCjkNameWithOneWrongCharacterIsNotGuessed()
    {
        // 1 - 1/3 = 0.667. The safety rule: an 8号门 read as 3号门 must never light an exit.
        Assert.Null(NameMatcher.Match("3号门", ["8号门"]));
        Assert.Null(NameMatcher.Match("8号门", ["3号门"]));
    }

    [Fact]
    public void TheBowGlyphIsNeverRewrittenByNormalizeButStillScoresAsThree()
    {
        // 弓 is an ordinary character, and a future snapshot could legitimately name an exit
        // 弓形门. Normalize is the canonical form that gets stored -- the candidate surface and the
        // English name Includes looks up -- so a rewrite here would silently corrupt that POI
        // everywhere, with no warning and a green suite.
        Assert.Equal("弓形门", NameMatcher.Normalize("弓 形 门"));
        Assert.Equal("弓形门", NameMatcher.Normalize("弓形门"));

        // The correction lives in the scoring path instead, symmetric on both sides, so a captured
        // "弓号门" read still resolves to 3号门 (the scav Factory panel's only numbered exit).
        var match = NameMatcher.Match("弓号门", ["3号门", "0号门"]);

        Assert.NotNull(match);
        Assert.Equal("3号门", match.NormalizedName);
    }

    [Fact]
    public void ZeroAndTwoAreRivalsBecauseTheReaderConfusesThem()
    {
        // Captured: 0号门 read as 2号门, while Gate 2 is also a real Factory exit. There is no way
        // to tell them apart, so refusing is correct -- dimming both beats lighting the wrong one.
        var match = NameMatcher.Match("2号门", ["0号门", "2号门"]);

        Assert.Null(match);
    }
}

/// <summary>
/// Turning a panel reading into the set of exits to keep bright.
/// </summary>
public sealed class ExitAvailabilityTests
{
    private static MapPoi Exit(string name, PoiKind kind = PoiKind.ExtractPmc, string? zh = null) => new()
    {
        Kind = kind,
        Name = name,
        NameZh = zh,
        Position = new GamePosition(0, 0, 0),
        Base = new MapPoint(0, 0),
    };

    private static ExtractPanelReading Reading(params string[] names) => new()
    {
        PanelFound = true,
        Rows = names.Select(n => new PanelRow(PanelRowKind.Extract, n, n)).ToArray(),
    };

    [Fact]
    public void KeepsOnlyTheExitsThePanelNamed()
    {
        MapPoi[] exits = [Exit("Sniper Roadblock"), Exit("Warehouse 17"), Exit("Crossroads")];

        var availability = ExitAvailability.Resolve(
            Reading("Sniper Roadblock", "Warehouse 17"), exits, "customs", DateTime.Now);

        Assert.NotNull(availability);
        Assert.True(availability.Includes(exits[0]));
        Assert.True(availability.Includes(exits[1]));
        Assert.False(availability.Includes(exits[2]));
    }

    [Fact]
    public void OneNamedRowLightsUpEveryExitThatSharesTheName()
    {
        var pmc = Exit("RUAF Roadblock");
        var scav = Exit("RUAF Roadblock", PoiKind.ExtractScav);

        var availability = ExitAvailability.Resolve(
            Reading("RIJAF Roadblock"), [pmc, scav], "customs", DateTime.Now);

        Assert.NotNull(availability);
        Assert.True(availability.Includes(pmc));
        Assert.True(availability.Includes(scav));
    }

    [Fact]
    public void ResolvesARowWhoseIdWasReadAsGibberish()
    {
        // What a 720p frame actually produced. The id is unrecoverable; the name is not.
        var boiler = Exit("Boiler Room Basement (Co-op)", PoiKind.ExtractShared);
        var factory = Exit("Transit to Factory", PoiKind.Transit);

        var reading = new ExtractPanelReading
        {
            PanelFound = true,
            Rows =
            [
                new PanelRow(PanelRowKind.Unknown, "EXIT u Boiler Room Basement (Co-op)",
                    "EXIT u Boiler Room Basement (Co-op)",
                    ["EXIT u Boiler Room Basement (Co-op)", "u Boiler Room Basement (Co-op)", "Boiler Room Basement (Co-op)"]),
                new PanelRow(PanelRowKind.Unknown, "TRANSIT Q Transit to Factory",
                    "TRANSIT Q Transit to Factory",
                    ["TRANSIT Q Transit to Factory", "Q Transit to Factory", "Transit to Factory"]),
            ],
        };

        var availability = ExitAvailability.Resolve(reading, [boiler, factory], "customs", DateTime.Now);

        Assert.NotNull(availability);
        Assert.True(availability.Includes(boiler));
        Assert.True(availability.Includes(factory));
        Assert.Empty(availability.Unresolved);
    }

    [Fact]
    public void NoPanelMeansNoOpinion()
    {
        Assert.Null(ExitAvailability.Resolve(
            ExtractPanelReading.NotFound, [Exit("Crossroads")], "customs", DateTime.Now));
    }

    [Fact]
    public void APanelWhereNothingResolvedMeansNoOpinion()
    {
        // Returning an empty set instead would dim every exit on the map, which reads as
        // "you have no way out" -- a confident-looking lie built out of a failed read.
        var availability = ExitAvailability.Resolve(
            Reading("qqqq zzzz", "wwww vvvv"), [Exit("Crossroads")], "customs", DateTime.Now);

        Assert.Null(availability);
    }

    [Fact]
    public void ReportsRowsItCouldNotPlace()
    {
        MapPoi[] exits = [Exit("Sniper Roadblock"), Exit("Crossroads")];

        var availability = ExitAvailability.Resolve(
            Reading("Sniper Roadblock", "Qqqzzz Wvvxx"), exits, "customs", DateTime.Now);

        Assert.NotNull(availability);
        Assert.Equal(["Qqqzzz Wvvxx"], availability.Unresolved);
    }

    [Fact]
    public void DoesNotReportStrayHudTextAsAnUnrecognizedExit()
    {
        // "Compass" is a hotbar label that landed inside the panel's bounds. Calling it an
        // unrecognized exit would teach the user to ignore that message.
        var reading = new ExtractPanelReading
        {
            PanelFound = true,
            Rows =
            [
                new PanelRow(PanelRowKind.Extract, "Sniper Roadblock", "EXIT01 Sniper Roadblock") { HasIdKeyword = true },
                new PanelRow(PanelRowKind.Unknown, "Compass", "Compass"),
            ],
        };

        var availability = ExitAvailability.Resolve(
            reading, [Exit("Sniper Roadblock")], "customs", DateTime.Now);

        Assert.NotNull(availability);
        Assert.Empty(availability.Unresolved);
    }

    [Fact]
    public void CombinesTwoLooksAtTheSameRaidsList()
    {
        // A screenshot can catch the panel part-way through opening. On its own that reading would
        // dim exits an earlier, fuller look had already found.
        var trailer = Exit("Trailer Park");
        var dorms = Exit("Dorms V-Ex");
        MapPoi[] exits = [trailer, dorms];

        var full = ExitAvailability.Resolve(
            Reading("Trailer Park", "Dorms V-Ex"), exits, "customs", DateTime.Now);

        var partial = ExitAvailability.Resolve(
            Reading("Trailer Park"), exits, "customs", DateTime.Now);

        var merged = partial!.MergedWith(full);

        Assert.True(merged.Includes(trailer));
        Assert.True(merged.Includes(dorms));
    }

    [Fact]
    public void DoesNotCombineReadingsFromDifferentMaps()
    {
        var crossroads = Exit("Crossroads");

        var customs = ExitAvailability.Resolve(
            Reading("Crossroads"), [crossroads], "customs", DateTime.Now);

        var woods = ExitAvailability.Resolve(
            Reading("Outskirts"), [Exit("Outskirts")], "woods", DateTime.Now);

        Assert.False(woods!.MergedWith(customs).Includes(crossroads));
    }

    [Fact]
    public void AMapWithNoExitDataMeansNoOpinion()
    {
        Assert.Null(ExitAvailability.Resolve(
            Reading("Sniper Roadblock"), [], "customs", DateTime.Now));
    }

    [Fact]
    public void AChineseReadLightsThePoiAndIncludesStillAnswersForItsEnglishName()
    {
        // The C3b trap: if a Chinese hit were stored as "宿舍载具撤离点", Includes would compare it
        // with Normalize(poi.Name) and return false for every POI -- the map would go dark while
        // every test stayed green. The matched set must hold the English canonical name.
        var dorms = Exit("Dorms V-Ex", PoiKind.ExtractPmc, "宿舍载具撤离点");
        var crossroads = Exit("Crossroads", PoiKind.ExtractPmc, "十字路口");

        var availability = ExitAvailability.Resolve(
            Reading("宿舍载具撤离点"), [dorms, crossroads], "customs", DateTime.Now);

        Assert.NotNull(availability);
        Assert.True(availability.Includes(dorms));
        Assert.False(availability.Includes(crossroads));
        Assert.Equal(1, availability.NameCount);
    }

    [Fact]
    public void AChineseRowThatMatchesNothingIsReported()
    {
        // C5: the id keyword comes from 撤离点 here, so a Chinese row that failed to resolve is
        // still worth telling the user about.
        var crossroads = Exit("Crossroads", PoiKind.ExtractPmc, "十字路口");

        var reading = new ExtractPanelReading
        {
            PanelFound = true,
            Rows =
            [
                new PanelRow(PanelRowKind.Extract, "十字路口", "撤 离 点 亿 1 十 字 路 口") { HasIdKeyword = true },
                new PanelRow(PanelRowKind.Extract, "不存在的撤离点", "撤 离 点 亿 2 不 存 在 的 撤 离 点") { HasIdKeyword = true },
            ],
        };

        var availability = ExitAvailability.Resolve(reading, [crossroads], "customs", DateTime.Now);

        Assert.NotNull(availability);
        Assert.True(availability.Includes(crossroads));
        Assert.Equal(["不存在的撤离点"], availability.Unresolved);
    }

    [Fact]
    public void DoesNotReportAStrayChineseHotbarLabelAsAnExit()
    {
        var crossroads = Exit("Crossroads", PoiKind.ExtractPmc, "十字路口");

        var reading = new ExtractPanelReading
        {
            PanelFound = true,
            Rows =
            [
                new PanelRow(PanelRowKind.Extract, "十字路口", "撤 离 点 亿 1 十 字 路 口") { HasIdKeyword = true },
                new PanelRow(PanelRowKind.Unknown, "夹板", "夹 板"),
            ],
        };

        var availability = ExitAvailability.Resolve(reading, [crossroads], "customs", DateTime.Now);

        Assert.NotNull(availability);
        Assert.Empty(availability.Unresolved);
    }
}

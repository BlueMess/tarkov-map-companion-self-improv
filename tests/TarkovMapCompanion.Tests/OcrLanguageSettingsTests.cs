using TarkovMapCompanion.Settings;
using Xunit;

namespace TarkovMapCompanion.Tests;

/// <summary>
/// The OCR engine language is a setting of its own, separate from the UI language, and the Windows
/// tag it resolves to is what actually points the engine at the game client.
/// </summary>
/// <remarks>
/// Pinned here because a wrong tag fails quietly: the engine starts, reads the panel, and returns
/// wrong text that looks like "no exits found". Nothing downstream can tell that from a raid with no
/// extraction list on screen, so the mapping itself has to be the thing under test. This touches no
/// OCR engine, so it runs on a machine with no language packs installed.
/// </remarks>
public sealed class OcrLanguageSettingsTests
{
    [Theory]
    [InlineData(OcrLanguage.English, "en-US")]
    [InlineData(OcrLanguage.ChineseSimplified, "zh-Hans")]
    public void TheConfiguredLanguageMapsToTheExpectedWindowsTag(OcrLanguage language, string expected) =>
        Assert.Equal(expected, MapSession.OcrLanguageTag(language));

    [Fact]
    public void EveryOcrLanguageResolvesToATag()
    {
        // Iterating the enum rather than naming the members means a future addition that forgot its
        // own arm is caught here instead of silently taking the default branch.
        foreach (var language in Enum.GetValues<OcrLanguage>())
            Assert.False(string.IsNullOrWhiteSpace(MapSession.OcrLanguageTag(language)));
    }
}

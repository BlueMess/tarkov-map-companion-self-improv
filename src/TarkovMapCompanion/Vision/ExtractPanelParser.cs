using System.Text;
using System.Text.RegularExpressions;

namespace TarkovMapCompanion.Vision;

public enum PanelRowKind
{
    /// <summary>The "Find an extraction point" banner above the list.</summary>
    ListHeader,

    /// <summary>The "Stay in the extraction point" banner shown while extracting.</summary>
    ActiveHeader,

    /// <summary>
    /// An exit row: <c>EXFIL<i>nn</i></c> on a PMC raid, <c>EXIT<i>nn</i></c> on a Scav one.
    /// </summary>
    Extract,

    /// <summary>A <c>TRANSIT<i>nn</i></c> row.</summary>
    Transit,

    /// <summary>Inside the panel, but with no id we recognized.</summary>
    Unknown,
}

public sealed record PanelRow(
    PanelRowKind Kind,
    string Name,
    string RawText,
    IReadOnlyList<string>? Readings = null)
{
    /// <summary>
    /// Every plausible reading of this row's name, best guess first.
    /// </summary>
    /// <remarks>
    /// The id column is the least legible thing on the panel: short, set in a heavy face, and made
    /// of exactly the characters a reader invents. At low resolutions it comes back as "EXIT u" or
    /// "TRANSIT Q", which no pattern strips without also eating the first word of names that
    /// legitimately begin with "Transit". Offering both readings and letting the name match decide
    /// is safer than making the pattern cleverer.
    /// </remarks>
    public IReadOnlyList<string> NameCandidates => Readings is { Count: > 0 } ? Readings : [Name];

    /// <summary>Whether the row carries an id keyword, however mangled.</summary>
    public bool HasIdKeyword { get; init; }

    /// <summary>
    /// Whether failing to identify this row is worth telling the user about.
    /// </summary>
    /// <remarks>
    /// Text can land inside the panel's bounds without being part of it -- a hotbar label, when
    /// the panel opens lower down the screen. Those are picked up opportunistically and failing to
    /// match one is not news; a row that plainly says EXFIL and still matches nothing is.
    /// </remarks>
    public bool LooksLikeAnExitRow => Kind is PanelRowKind.Extract or PanelRowKind.Transit || HasIdKeyword;
}

/// <summary>What one screenshot's extraction panel said.</summary>
public sealed class ExtractPanelReading
{
    public static readonly ExtractPanelReading NotFound = new();

    /// <summary>
    /// True only when we are confident we were looking at the exit list. Everything downstream is
    /// gated on this, because an empty reading and a reading of an empty list are very different
    /// things: one means "no information", the other would mean "you have no exits".
    /// </summary>
    public bool PanelFound { get; init; }

    public IReadOnlyList<PanelRow> Rows { get; init; } = [];

    /// <summary>Rows that name an exit the player could walk to.</summary>
    public IReadOnlyList<PanelRow> Exits =>
        Rows.Where(r => r.Kind is PanelRowKind.Extract or PanelRowKind.Transit or PanelRowKind.Unknown).ToArray();
}

/// <summary>
/// Turns the text read off a screenshot into the rows of Tarkov's extraction panel.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and separate from the reader, because this is where every judgement call lives and it
/// needs to be testable against captured text without an OCR engine in the loop.
/// </para>
/// <para>
/// The panel is a table. Its rows are found geometrically -- fragments sharing a vertical band are
/// one row -- rather than by trusting the reader's own line breaking, which splits the id column
/// away from the name for some rows and merges them for others. Nothing assumes a fixed number of
/// rows: the list is different every raid, and transits may not be there at all.
/// </para>
/// </remarks>
public static partial class ExtractPanelParser
{
    /// <summary>
    /// The id in the left column: EXIT01, TRANSIT02, EXFIL05. The digits are deliberately loose
    /// because Tarkov's font uses a slashed zero, which readers report as O, Ø, D or Q.
    /// </summary>
    /// <remarks>
    /// <para>
    /// At least one digit-like character is required. Without that, "Transit to Reserve" would
    /// match the TRANSIT keyword and get its first word eaten.
    /// </para>
    /// <para>
    /// The trailing lookahead is what makes the loose character class safe. "EXIT Sniper Roadblock"
    /// cannot match, because S is followed by more letters rather than a space, so widening the
    /// class to cover the reader's inventions does not let it start eating real names.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"^(?<kind>EXTRACT|TRANSIT|EXFIL|EXIT)[\s\-_.]*[0-9OØQDIl|uUMmGSBZ]{1,4}(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IdPrefix();

    /// <summary>Keywords that begin the id column, for the fallback readings.</summary>
    private static readonly string[] IdKeywords = ["EXTRACT", "TRANSIT", "EXFIL", "EXIT"];

    /// <summary>
    /// The Chinese id column: the token the game uses for each section, then the one or two
    /// digit-like characters the reader supplies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The token is the game's own wording, confirmed against real 2026 client frames: <c>撤离点</c>
    /// for a PMC exit, <c>退出</c> for a Scav exit, and <c>转移点</c> for a transit. The backlog's
    /// language-table prediction of <c>目的地</c> for transits was wrong; the panel really says
    /// <c>转移点</c>.
    /// </para>
    /// <para>
    /// It is matched against a compacted copy of the line, because the reader puts a space between
    /// every Chinese character ("撤 离 点 亿 1"). A leading non-letter run absorbs the stray
    /// punctuation a row sometimes picks up ("、 转 移 点 21").
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"^[^\p{L}\p{N}]*?(?<kind>撤离点|退出|转移点)(?<digits>[0-9匠亿@弓弱日]{0,2})",
        RegexOptions.CultureInvariant)]
    private static partial Regex CjkIdPrefix();

    /// <summary>
    /// A Chinese row whose id token was damaged or merged past recognition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two shapes. The token may keep its first character and lose the rest ("转2前往立交桥"), or the
    /// reader may mangle that first character but keep the "移点" of 转移点 ("车专移点1前往储备站").
    /// </para>
    /// <para>
    /// It must stay this specific rather than "any few CJK characters then digits": a loose version
    /// matches hotbar noise like "弓22/322", which then becomes an anchor and drags every HUD label
    /// into the panel. Only ever a fallback reading; the match decides which reading is an exit name.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"^[^\p{L}\p{N}]*?(?<kind>[撤离退转][\p{L}]{0,3}|[\p{L}]{0,2}移点)(?<digits>[0-9匠亿@弓弱日]{1,2})",
        RegexOptions.CultureInvariant)]
    private static partial Regex CjkIdLoose();

    /// <summary>Keywords that begin a Chinese id column.</summary>
    private static readonly string[] CjkIdKeywords = ["撤离点", "退出", "转移点"];

    /// <summary>
    /// A trailing countdown, e.g. "0:00:54".
    /// </summary>
    /// <remarks>
    /// The digit class has to cover both cases of the slashed zero. The reader returns the
    /// uppercase Ø at 1440p and the lowercase ø when the text is smaller, and a class carrying only
    /// one of them leaves "Transit to Factory ø:øø:54" as the name -- which then falls below the
    /// match floor and reports a perfectly legible row as unreadable. It looked like a resolution
    /// limit and was nothing of the kind.
    /// </remarks>
    [GeneratedRegex(
        @"\s*[0-9OØQDIl|]{1,2}[:.;][0-9OØQDIl|]{2}([:.;][0-9OØQDIl|]{2})?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingTimer();

    /// <summary>
    /// A Chinese countdown whose separators survived, e.g. "匠 34 ： 57" or "匠亿匠 57".
    /// </summary>
    /// <remarks>
    /// The engine reports the digits as glyphs it invents (匠/亿/@) and frequently drops the colons
    /// entirely. Without stripping these the remains glue onto the name -- "前往森林匠@匠57" -- and
    /// the row falls below the match floor for a reason that has nothing to do with the name.
    /// </remarks>
    [GeneratedRegex(
        @"(?:[匠亿@弓弱日]){0,3}\d{1,2}[:：](?:[匠亿@弓弱日]){0,3}\d{1,2}(?:[:：](?:[匠亿@弓弱日]){0,3}\d{1,2})?\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TrailingCjkTimerWithSeparator();

    /// <summary>
    /// A Chinese countdown whose colons were lost entirely, e.g. "匠亿匠57" ("0:00:57") or "匠2匠54".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reader mixes real digits into the invented glyphs arbitrarily -- captured "匠2匠54" sits
    /// beside "匠亿匠57" -- so the run is any blend of the confusion glyphs and digits, ending in
    /// one or two digits.
    /// </para>
    /// <para>
    /// It has to start with a glyph, not merely contain one. Allowing a leading digit would let the
    /// run begin inside a real name: "ZB-1013匠亿匠57" would match from the 3 in "1013" and eat the
    /// name's digits. Requiring the glyph first leaves "ZB-1013" untouched, and the earlier bug --
    /// "any non-ASCII" matching the last character of "前往储备站" -- cannot come back because only
    /// the specific confusion glyphs are in the class, never arbitrary ideographs.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"[匠亿@弓弱日][匠亿@弓弱日0-9]*\d{1,2}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingCjkTimer();

    public static ExtractPanelReading Parse(IReadOnlyList<OcrLine> lines)
    {
        var usable = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToArray();
        if (usable.Length == 0)
            return ExtractPanelReading.NotFound;

        // Anchor on the id column. Every row of the panel starts with one, so the leftmost id tells
        // us where the panel begins -- which is the only reliable way to discard the hotbar and
        // weapon labels that share the top of the frame and would otherwise be merged into a row.
        //
        // A header alone can still prove the panel is open, so it is an acceptable anchor when the
        // id column was read but produced nothing recognizable.
        var idLines = usable.Where(l => LooksLikeIdLine(l.Text.TrimStart())).ToArray();
        var anchorLines = idLines.Length > 0
            ? idLines
            : usable.Where(l => LooksLikeHeader(l.Text)).ToArray();

        if (anchorLines.Length == 0)
            return ExtractPanelReading.NotFound;

        var panelLeft = anchorLines.Min(l => l.Bounds.X);
        var slack = Math.Max(12.0, anchorLines.Average(l => l.Bounds.Height) * 0.75);

        // Judged on the middle of the text, not its left edge. A row whose id picks up a stray
        // glyph from the panel border starts slightly outside and would be dropped whole, while a
        // hotbar label that merely reaches the panel's edge is still mostly outside it.
        var inPanel = usable
            .Where(l => l.Bounds.X + (l.Bounds.Width / 2.0) >= panelLeft - slack)
            .ToArray();

        var rows = new List<PanelRow>();
        var prefixedExits = 0;
        var sawListHeader = false;
        var sawActiveHeader = false;

        foreach (var group in GroupIntoRows(inPanel))
        {
            var raw = string.Join(" ", group.OrderBy(l => l.Bounds.X).Select(l => l.Text.Trim()));

            // A requirement sub-line ("需要 TerraGroup 实验室访问钥匙卡（1）") belongs to the row
            // above it. Reported as a row of its own it would look like an exit that failed to
            // match, which is exactly the message that has to stay trustworthy.
            if (IsRequirementLine(raw))
                continue;

            var row = Classify(raw);

            if (row.Kind == PanelRowKind.ListHeader)
                sawListHeader = true;

            if (row.Kind == PanelRowKind.ActiveHeader)
                sawActiveHeader = true;

            if (row.Kind is PanelRowKind.Extract or PanelRowKind.Transit)
                prefixedExits++;

            rows.Add(row);
        }

        // Two independent ways to be sure: the list's own header, or several rows that are
        // unmistakably list rows.
        //
        // "Stay in the extraction point" vetoes the second of those. That banner shows the exit
        // being used rather than the ones on offer, and its rows are numbered exactly like the
        // list's, so counting rows cannot tell them apart -- only the wording can.
        var found = sawListHeader || (prefixedExits >= 2 && !sawActiveHeader);

        return found
            ? new ExtractPanelReading { PanelFound = true, Rows = rows }
            : ExtractPanelReading.NotFound;
    }

    /// <summary>
    /// Groups fragments that share a vertical band into table rows.
    /// </summary>
    /// <remarks>
    /// Banding rather than exact tops: the id column is set in a heavier, taller face than the
    /// name beside it, so the two never share a baseline and grouping on y alone would split every
    /// row in half.
    /// </remarks>
    private static List<List<OcrLine>> GroupIntoRows(IReadOnlyList<OcrLine> lines)
    {
        var ordered = lines.OrderBy(l => l.Bounds.CenterY).ToArray();

        var heights = ordered.Select(l => l.Bounds.Height).OrderBy(h => h).ToArray();
        var medianHeight = heights[heights.Length / 2];
        var tolerance = Math.Max(6.0, medianHeight * 0.7);

        var groups = new List<List<OcrLine>>();

        foreach (var line in ordered)
        {
            var current = groups.Count > 0 ? groups[^1] : null;

            if (current is not null && Math.Abs(line.Bounds.CenterY - current.Average(l => l.Bounds.CenterY)) <= tolerance)
                current.Add(line);
            else
                groups.Add([line]);
        }

        return groups;
    }

    private static PanelRow Classify(string raw)
    {
        var text = raw.Trim();

        // The active-extraction banner shares 撤离点 with the list header and its rows are numbered
        // exactly like the list's, so only the wording separates them. "Stay" is the English veto;
        // "停留" and "回到" are its Chinese counterparts. Checked before the id column because a
        // Chinese banner row also begins with the id token.
        var normalized = NameMatcher.Normalize(text);
        var activeEnglish = normalized.Contains("extraction point", StringComparison.Ordinal)
            && normalized.Contains("stay", StringComparison.Ordinal);

        if (activeEnglish
            || normalized.Contains("停留", StringComparison.Ordinal)
            || normalized.Contains("回到", StringComparison.Ordinal))
        {
            return new PanelRow(PanelRowKind.ActiveHeader, "", text);
        }

        var kind = PanelRowKind.Unknown;
        var readings = new List<string>();

        if (IdPrefix().Match(text) is { Success: true } match)
        {
            // EXFIL and EXIT are the same thing seen from different sides: Tarkov numbers a PMC's
            // exits EXFILnn and a Scav's EXITnn. Reading EXFIL as "the exit being used right now"
            // -- which is how it looks in the extracting banner -- silently threw away every row
            // of every PMC raid's list.
            kind = match.Groups["kind"].Value.ToUpperInvariant() switch
            {
                "TRANSIT" => PanelRowKind.Transit,
                _ => PanelRowKind.Extract,
            };

            // Stripping the id worked, so that is the best guess.
            readings.Add(Clean(text[match.Length..]));
        }
        else if (TryStripCjkId(Compact(text), out var cjkKind, out var cjkRests))
        {
            kind = cjkKind;

            foreach (var rest in cjkRests)
            {
                if (rest.Length > 0)
                    readings.Add(Clean(rest));
            }

            if (readings.Count == 0)
            {
                // The id survived but the name did not. Keeping the whole text as a reading would
                // turn the id itself into a name; an empty row is honest.
                return new PanelRow(PanelRowKind.Unknown, "", text) { HasIdKeyword = true };
            }
        }
        else if (normalized.Contains("extraction point", StringComparison.Ordinal)
            || normalized.Contains("找到撤离点", StringComparison.Ordinal))
        {
            // No id, but the wording says this is the list's own banner.
            return new PanelRow(PanelRowKind.ListHeader, "", text);
        }
        else
        {
            // The id did not strip, so the whole row is the best guess -- and it may genuinely be
            // one, for a row whose id the reader dropped completely.
            readings.Add(Clean(text));
        }

        // Fallbacks for an id mangled past recognition: drop the first token, and the first two
        // when the second is short enough to be a stray digit rather than part of a name.
        //
        // Only meaningful for the English column, where a "token" is a word. Splitting a Chinese
        // line on the reader's spaces yields single characters, so "撤 离 点" would fall back to
        // "离 点" -- a fragment of the id masquerading as a name.
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Contains rather than StartsWith: a stray glyph in front of the id ("WEXFIL01") is common
        // enough, and it defeats an anchored pattern while leaving the keyword perfectly visible.
        var compact = Compact(text);
        var hasKeyword = (tokens.Length > 0
                && IdKeywords.Any(k => tokens[0].Contains(k, StringComparison.OrdinalIgnoreCase)))
            || CjkIdKeywords.Any(k => compact.Contains(k, StringComparison.Ordinal));

        if (!NameMatcher.ContainsCjk(text) && tokens.Length > 1 && hasKeyword)
        {
            readings.Add(Clean(string.Join(' ', tokens.Skip(1))));

            if (tokens.Length > 2 && tokens[1].Length <= 3)
                readings.Add(Clean(string.Join(' ', tokens.Skip(2))));
        }

        // The damaged-token reading ("转2前往立交桥" -> "前往立交桥") is a guess that only the match
        // can validate, so it is offered alongside the full-token strip rather than instead of it.
        if (CjkIdLoose().Match(compact) is { Success: true } loose
            && loose.Length < compact.Length)
        {
            readings.Add(Clean(compact[loose.Length..]));
        }

        readings.Add(Clean(text));

        var distinct = readings
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return distinct.Length == 0
            ? new PanelRow(kind, "", text) { HasIdKeyword = hasKeyword }
            : new PanelRow(kind, distinct[0], text, distinct) { HasIdKeyword = hasKeyword };
    }

    /// <summary>
    /// Whether a line looks like the panel's own banner.
    /// </summary>
    private static bool LooksLikeHeader(string text)
    {
        var normalized = NameMatcher.Normalize(text);

        return normalized.Contains("extraction point", StringComparison.Ordinal)
            || normalized.Contains("找到撤离点", StringComparison.Ordinal)
            || normalized.Contains("停留", StringComparison.Ordinal)
            || normalized.Contains("回到", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a line begins the id column, in either language.
    /// </summary>
    private static bool LooksLikeIdLine(string text) =>
        IdPrefix().IsMatch(text) || (NameMatcher.ContainsCjk(text) && TryStripCjkId(Compact(text), out _, out _));

    /// <summary>
    /// Strips a Chinese id from a compacted line, in either the intact or the damaged-token form.
    /// </summary>
    /// <remarks>
    /// Returns every digit-count strip, longest first, rather than only the greedy one. The reader
    /// sometimes loses the id's digits while keeping the token -- under a tighter crop
    /// "撤离点03 0号门" reads as "撤离点 2号门" -- and a rule that always consumes up to two
    /// digit-like characters then eats the name's own leading digit ("号门"). Offering the shorter
    /// strips as fallback readings lets the match recover the name without weakening the id case.
    /// </remarks>
    private static bool TryStripCjkId(string compact, out PanelRowKind kind, out IReadOnlyList<string> rests)
    {
        kind = PanelRowKind.Unknown;
        rests = [];

        if (CjkIdPrefix().Match(compact) is { Success: true } full)
        {
            kind = CjkKind(full.Groups["kind"].Value);
            rests = StripsAfter(compact, full.Groups["digits"]);
            return true;
        }

        if (CjkIdLoose().Match(compact) is { Success: true } loose)
        {
            kind = CjkKind(loose.Groups["kind"].Value);
            rests = StripsAfter(compact, loose.Groups["digits"]);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Every name that results from keeping 0..n of the id's captured digits, longest strip first.
    /// </summary>
    private static List<string> StripsAfter(string compact, Group digits)
    {
        var list = new List<string>(digits.Length + 1);

        for (var kept = digits.Length; kept >= 0; kept--)
            list.Add(compact[(digits.Index + kept)..]);

        return list;
    }

    /// <summary>
    /// Which section an id token belongs to. Transit is recognised by the surviving "移" (the reader
    /// misreads a damaged 转移点 as 车专移点 or 有专移点, but keeps the middle character), and by a
    /// lone leading 转 when the rest of the token was lost entirely.
    /// </summary>
    private static PanelRowKind CjkKind(string token) =>
        token.Contains('移') || token.StartsWith('转')
            ? PanelRowKind.Transit
            : PanelRowKind.Extract;

    /// <summary>Whether a line is a row's requirement sub-line rather than a row of its own.</summary>
    private static bool IsRequirementLine(string raw)
    {
        var normalized = NameMatcher.Normalize(raw);

        return normalized.StartsWith("需要", StringComparison.Ordinal)
            || normalized.StartsWith("requires", StringComparison.Ordinal)
            || normalized.StartsWith("needs", StringComparison.Ordinal);
    }

    /// <summary>
    /// Removes the reader's spaces from a line, so a Chinese id can be matched against its token.
    /// </summary>
    private static string Compact(string value)
    {
        if (!NameMatcher.ContainsCjk(value))
            return value;

        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
                builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string Clean(string value) =>
        TrailingCjkTimerWithSeparator().Replace(
            TrailingCjkTimer().Replace(TrailingTimer().Replace(value, ""), ""), "").Trim();
}

using System.Text;

namespace TarkovMapCompanion.Vision;

/// <summary>The best candidate for a read line, or the reason there wasn't one.</summary>
public sealed record NameMatch(string NormalizedName, double Score, double RunnerUp)
{
    public double Margin => Score - RunnerUp;
}

/// <summary>
/// Resolves a line of read text to one of a known set of names.
/// </summary>
/// <remarks>
/// <para>
/// This is a closed-set problem, not open-ended text recognition: the game can only be showing an
/// exit that exists on the current map, and we have that list. So the reader does not have to be
/// character-perfect, it only has to get close enough that one candidate stands out. "RIJAF
/// Roadblock" is 87% similar to "RUAF Roadblock" and 69% similar to the next best thing, which is
/// a comfortable decision.
/// </para>
/// <para>
/// The margin requirement is the important half. Customs has both "Warehouse 17" and "Warehouse 4",
/// and Reserve has "ZB-1011" and "ZB-1012" -- one character apart. When a misread lands between two
/// candidates, refusing to choose is correct; guessing would put a marker on the wrong side of the
/// map, which is worse than admitting we could not read it.
/// </para>
/// </remarks>
public static class NameMatcher
{
    /// <summary>Minimum similarity before a line is considered to name anything at all.</summary>
    public const double DefaultFloor = 0.78;

    /// <summary>How far the best candidate must beat the next distinct one.</summary>
    public const double DefaultMargin = 0.08;

    /// <summary>
    /// Minimum similarity for a read whose text contains a CJK ideograph.
    /// </summary>
    /// <remarks>
    /// The English floor cannot transfer. Chinese exit names are short -- 27% are four characters
    /// or fewer, 46% five or fewer -- and one wrong character in a four-character name scores 0.75,
    /// which the English floor rejects outright. The floor here is deliberately low enough to
    /// recover a single wrong character in a four-character name (0.75) while still rejecting the
    /// one-character substitution the safety rule is about (0.667).
    /// </remarks>
    public const double CjkFloor = 0.70;

    /// <summary>How far the best CJK candidate must beat the next distinct one.</summary>
    /// <remarks>
    /// Chinese names are dense: two different exits can share a long prefix ("Transit to ..." is
    /// "前往..." for every destination). Keeping the English margin value; the ambiguity guard in
    /// <see cref="Match"/> handles the digit-confusion case specifically.
    /// </remarks>
    public const double CjkMargin = 0.08;

    /// <summary>
    /// True when <paramref name="value"/> contains a CJK ideograph.
    /// </summary>
    /// <remarks>
    /// The distinction decides two things: whether the reader's spaces between individual CJK
    /// characters have to be dropped (they are not word separators), and which floor applies.
    /// </remarks>
    public static bool ContainsCjk(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var ch in value)
        {
            if ((ch >= 0x3400 && ch <= 0x4DBF)   // CJK Unified Ideographs Extension A
                || (ch >= 0x4E00 && ch <= 0x9FFF) // CJK Unified Ideographs
                || (ch >= 0xF900 && ch <= 0xFAFF)) // CJK Compatibility Ideographs
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Casefolds and strips punctuation so "Boiler Room Basement (Co-op)" and
    /// "boiler room basement co-op" compare equal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the value contains a CJK ideograph the reader's spaces between individual characters
    /// are dropped rather than treated as separators: the engine returns "铁 路 通 道 （ 信 号 弹 ）"
    /// and the candidate is "铁路通道（信号弹）", so the spaces have to go on both sides or nothing
    /// in a Chinese panel ever matches. Latin-only strings keep their spaces, so the English path is
    /// unchanged.
    /// </para>
    /// <para>
    /// This is the canonicalizer, and it deliberately rewrites no characters. Its output is stored
    /// -- it is the surface a candidate is keyed by and the English name <c>Includes</c> looks up --
    /// so folding a glyph here would silently corrupt any real name that contains it. The reader's
    /// glyph confusions are corrected at scoring time instead, in <see cref="CorrectGlyphs"/> and
    /// <see cref="FoldConfusables"/>, where they can only ever affect a comparison.
    /// </para>
    /// </remarks>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');

                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                // Punctuation and whitespace both collapse to a single separator, so a stray comma
                // the reader invents cannot change the shape of the string.
                pendingSpace = true;
            }
        }

        var result = builder.ToString();

        if (!ContainsCjk(result))
            return result;

        var compact = new StringBuilder(result.Length);

        foreach (var ch in result)
        {
            if (ch != ' ')
                compact.Append(ch);
        }

        return compact.ToString();
    }

    /// <summary>
    /// Rewrites the glyphs the reader substitutes for a digit, for comparison only.
    /// </summary>
    /// <remarks>
    /// The panel font draws 3 in a way the zh-Hans engine reports as 弓 -- captured four times out of
    /// four across the Factory frames, where the reader's only numbered exit is Gate 3. Correcting it
    /// here, symmetric on both sides, lets "弓号门" score as "3号门" without <see cref="Normalize"/>
    /// ever having to claim that no real name contains 弓. 弓 is an ordinary character that a future
    /// snapshot could legitimately use in a name.
    /// </remarks>
    private static string CorrectGlyphs(string value) => value.Replace('弓', '3');

    /// <summary>
    /// Collapses the digit glyphs the reader confuses with each other, so two candidates that differ
    /// only in such a glyph are seen as rivals rather than as a clear winner.
    /// </summary>
    /// <remarks>
    /// A scoring aid, separate from <see cref="CorrectGlyphs"/>: that one is a one-for-one glyph
    /// correction used for the deciding score, this one is the wider zero-family fold used only for
    /// the runner-up. It is what makes "0号门" and "2号门" -- the reader cannot tell the slashed zero
    /// from 2 -- tie rather than let one win. Like the correction, it never touches stored text.
    /// </remarks>
    private static string FoldConfusables(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                '0' or '2' or '6' or '匠' or '亿' or '@' or '弱' or '日' => '0',
                '3' or '弓' => '3',
                _ => ch,
            });
        }

        return builder.ToString();
    }

    /// <summary>Similarity in 0..1, where 1 is an exact match after normalization.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
            return 1.0;

        if (a.Length == 0 || b.Length == 0)
            return 0.0;

        var distance = Levenshtein(a, b);
        return 1.0 - ((double)distance / Math.Max(a.Length, b.Length));
    }

    /// <summary>
    /// Picks the candidate that <paramref name="text"/> names, or null when nothing is close enough
    /// or two candidates are too close to separate.
    /// </summary>
    /// <param name="candidates">Already-normalized names. Duplicates are harmless.</param>
    /// <remarks>
    /// <paramref name="floor"/> and <paramref name="margin"/> default to the CJK pair when the read
    /// contains a CJK ideograph and the English pair otherwise; passing values explicitly overrides
    /// that choice.
    /// </remarks>
    public static NameMatch? Match(
        string text,
        IEnumerable<string> candidates,
        double? floor = null,
        double? margin = null)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
            return null;

        var isCjk = ContainsCjk(normalized);
        var effectiveFloor = floor ?? (isCjk ? CjkFloor : DefaultFloor);
        var effectiveMargin = margin ?? (isCjk ? CjkMargin : DefaultMargin);

        var candidateList = candidates as IList<string> ?? candidates.ToArray();

        // The glyph correction mirrors the read into the form the candidates are written in. It is
        // applied to both sides, so it can only ever help them meet; it is never stored.
        var read = isCjk ? CorrectGlyphs(normalized) : normalized;

        // The best candidate is chosen on the plain similarity. Two POIs can share a display name
        // -- Customs lists both a PMC and a Scav "RUAF Roadblock" -- and that is not an ambiguity,
        // it is one place with two rules; only a genuinely different string is a rival.
        string? best = null;
        var bestScore = 0.0;

        foreach (var candidate in candidateList)
        {
            if (candidate.Length == 0)
                continue;

            var score = Similarity(read, isCjk ? CorrectGlyphs(candidate) : candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best is null)
            return null;

        var folded = isCjk ? FoldConfusables(read) : read;

        var runnerUp = 0.0;
        foreach (var candidate in candidateList)
        {
            if (candidate.Length == 0 || string.Equals(candidate, best, StringComparison.Ordinal))
                continue;

            var rival = isCjk
                ? Similarity(folded, FoldConfusables(candidate))
                : Similarity(read, candidate);

            // The reader reports Tarkov's slashed zero as 2 or 6 as well as the glyphs the id column
            // tolerates, so "0号门" and "2号门" are indistinguishable from a read of either. Folding
            // them together for the runner-up makes them rivals, and the margin requirement then
            // declines to choose -- dimming two exits is recoverable, lighting the wrong one is not.
            if (rival > runnerUp)
                runnerUp = rival;
        }

        if (bestScore < effectiveFloor || bestScore - runnerUp < effectiveMargin)
            return null;

        return new NameMatch(best, bestScore, runnerUp);
    }

    /// <summary>
    /// Standard edit distance, two rows rather than a full matrix. The strings here are short
    /// enough that the allocation matters more than the algorithm.
    /// </summary>
    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                var deletion = previous[j] + 1;
                var insertion = current[j - 1] + 1;

                current[j] = Math.Min(substitution, Math.Min(deletion, insertion));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}

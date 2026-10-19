using System.Globalization;
using System.Text;

namespace Zagrody.Core.Search;

/// <summary>
/// Subsequence matching in the spirit of editor "go to file" boxes: "wrd" finds "Word 2021".
/// Diacritics are ignored, so "zolw" finds "Żółw".
/// </summary>
public static class FuzzyMatcher
{
    /// <returns>A score where higher is better, or null when the query does not match.</returns>
    public static int? Score(string query, string text)
    {
        var q = Fold(query.Trim());
        if (q.Length == 0)
        {
            return 0;
        }
        var t = Fold(text);

        var index = t.IndexOf(q, StringComparison.Ordinal);
        if (index >= 0)
        {
            // Whole-substring hits always beat scattered ones.
            var score = 1000 - index;
            if (index == 0) score += 500;
            else if (IsWordStart(t, index)) score += 250;
            if (t.Length == q.Length) score += 200;
            return score;
        }

        var total = 0;
        var streak = 0;
        var ti = 0;
        foreach (var c in q)
        {
            if (c == ' ')
            {
                streak = 0;
                continue;
            }
            var found = t.IndexOf(c, ti);
            if (found < 0)
            {
                return null;
            }
            streak = found == ti ? streak + 1 : 0;
            total += 10 + streak * 5 + (IsWordStart(t, found) ? 15 : 0) - Math.Min(found - ti, 10);
            ti = found + 1;
        }
        return total;
    }

    private static bool IsWordStart(string text, int index) =>
        index == 0 || !char.IsLetterOrDigit(text[index - 1]);

    /// <summary>Lowercases and strips accents, including Polish 'ł' which has no Unicode decomposition.</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            sb.Append(c switch
            {
                'ł' or 'Ł' => 'l',
                _ => char.ToLowerInvariant(c),
            });
        }
        return sb.ToString();
    }
}

using System.Text.RegularExpressions;

namespace Nova.Core.Util;

public static class WildcardPattern
{
    /// <summary>Case-insensitive match supporting '*' and '?'.</summary>
    public static bool IsMatch(string text, string pattern)
    {
        int t = 0, p = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
            { t++; p++; }
            else if (p < pattern.Length && pattern[p] == '*')
            { star = p++; mark = t; }
            else if (star >= 0)
            { p = star + 1; t = ++mark; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    public static bool HasWildcards(string pattern) => pattern.IndexOfAny(new[] { '*', '?' }) >= 0;

    /// <summary>
    /// Flexible text filter used for window titles and the folder filter box:
    /// "re:..." = regex, wildcard pattern = whole-string glob, otherwise substring.
    /// </summary>
    public static bool MatchesFilter(string text, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        if (filter.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Regex.IsMatch(text, filter[3..], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(50));
            }
            catch (ArgumentException) { return false; }
            catch (RegexMatchTimeoutException) { return false; }
        }
        return HasWildcards(filter)
            ? IsMatch(text, filter)
            : text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}

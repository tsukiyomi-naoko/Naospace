namespace Nova.Core.Services;

/// <summary>
/// Subsequence fuzzy matcher for the quick-jump palette. Rewards matches at word
/// starts, consecutive runs and prefixes, so "cnl" finds "ComfyUI › custom_nodes › lora".
/// </summary>
public static class FuzzyMatcher
{
    public static int? Score(string query, string candidate)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        query = query.Trim();
        int score = 0, qi = 0, run = 0, firstMatch = -1;
        for (int ci = 0; ci < candidate.Length && qi < query.Length; ci++)
        {
            if (query[qi] == ' ') { qi++; run = 0; if (qi >= query.Length) break; }
            if (char.ToLowerInvariant(candidate[ci]) != char.ToLowerInvariant(query[qi])) { run = 0; continue; }

            if (firstMatch < 0) firstMatch = ci;
            int s = 1;
            bool wordStart = ci == 0 || !char.IsLetterOrDigit(candidate[ci - 1]) ||
                             (char.IsUpper(candidate[ci]) && char.IsLower(candidate[ci - 1]));
            if (wordStart) s += 8;
            if (run > 0) s += 4 + run;
            if (candidate[ci] == query[qi]) s += 1;
            score += s;
            run++;
            qi++;
        }
        if (qi < query.Length) return null;
        score -= Math.Min(firstMatch, 20);
        score -= candidate.Length / 16;
        return score;
    }

    /// <summary>Scores candidates and returns the best matches, highest score first.</summary>
    public static IReadOnlyList<T> Rank<T>(string query, IEnumerable<T> items, Func<T, string> text, int take = 50, Func<T, int>? bonus = null)
    {
        return items
            .Select(i => (Item: i, Score: Score(query, text(i))))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score!.Value + (bonus?.Invoke(x.Item) ?? 0))
            .Take(take)
            .Select(x => x.Item)
            .ToList();
    }
}

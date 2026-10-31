namespace Nova.Core.Util;

/// <summary>
/// "Explorer-style" ordering: digit runs compare numerically, so "img2" sorts before "img10".
/// Managed so behaviour is deterministic and testable (StrCmpLogicalW is Windows-only).
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0, tieBreak = 0;
        while (i < x.Length && j < y.Length)
        {
            char cx = x[i], cy = y[j];
            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int si = i, sj = j;
                while (si < x.Length && x[si] == '0') si++;
                while (sj < y.Length && y[sj] == '0') sj++;
                int ei = si, ej = sj;
                while (ei < x.Length && char.IsDigit(x[ei])) ei++;
                while (ej < y.Length && char.IsDigit(y[ej])) ej++;
                int lenX = ei - si, lenY = ej - sj;
                if (lenX != lenY) return lenX.CompareTo(lenY);
                for (int k = 0; k < lenX; k++)
                    if (x[si + k] != y[sj + k]) return x[si + k].CompareTo(y[sj + k]);
                // Equal value: remember "fewer leading zeros first" as a tie-breaker.
                if (tieBreak == 0) tieBreak = (si - i).CompareTo(sj - j);
                i = ei; j = ej;
                continue;
            }
            int c = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
            if (c != 0) return c;
            i++; j++;
        }
        int rest = (x.Length - i).CompareTo(y.Length - j);
        if (rest != 0) return rest;
        return tieBreak != 0 ? tieBreak : string.CompareOrdinal(x, y);
    }
}

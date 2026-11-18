using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PCIsland.Core;

public static class Text
{
    public static string Fold(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            char c = ch switch
            {
                'I' => 'i', 'İ' => 'i', 'ı' => 'i',
                'Ş' or 'ş' => 's', 'Ğ' or 'ğ' => 'g', 'Ç' or 'ç' => 'c',
                'Ö' or 'ö' => 'o', 'Ü' or 'ü' => 'u', 'Â' or 'â' => 'a', 'Î' or 'î' => 'i', 'Û' or 'û' => 'u',
                '’' or '‘' or '`' or '´' => '\'',
                _ => char.ToLowerInvariant(ch)
            };
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Clean(string s)
    {
        var f = Fold(s);
        var sb = new StringBuilder(f.Length);
        bool space = false;
        foreach (var c in f)
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); space = false; }
            else if (!space && sb.Length > 0) { sb.Append(' '); space = true; }
        }
        return sb.ToString().Trim();
    }

    public static string Compact(string s) => Clean(s).Replace(" ", "");

    public static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        var prev2 = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    cur[j] = Math.Min(cur[j], prev2[j - 2] + 1);
            }
            (prev2, prev, cur) = (prev, cur, prev2);
        }
        return prev[b.Length];
    }

    static readonly string[] Suffixes =
    {
        "larini", "lerini", "sini", "sunu", "nini", "nunu", "unu", "ini",
        "yu", "yi", "nu", "ni", "u", "i", "ya", "ye", "a", "e", "da", "de", "ta", "te", "yla", "yle", "la", "le"
    };

    public static IEnumerable<string> SuffixVariants(string word)
    {
        yield return word;
        foreach (var suf in Suffixes)
            if (word.Length - suf.Length >= 3 && word.EndsWith(suf))
                yield return word[..^suf.Length];
    }

    public static string StripApostropheSuffix(string word)
    {
        int i = word.LastIndexOf('\'');
        if (i > 0 && word.Length - i - 1 <= 5 && word[(i + 1)..].All(char.IsLetter)) return word[..i];
        return word;
    }
}

public static class Matcher
{
    static int ScoreClean(string q, string c)
    {
        if (q.Length == 0 || c.Length == 0) return 0;
        if (q == c) return 100;
        string qc = q.Replace(" ", ""), cc = c.Replace(" ", "");
        if (qc == cc) return 98;
        if (cc.StartsWith(qc)) return Math.Max(80, 92 - (cc.Length - qc.Length) / 2);
        var words = c.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w == qc)) return 88;
        if (qc.Length >= 2 && words.Any(w => w.StartsWith(qc))) return 84;
        if (qc.Length >= 2 && words.Length >= 2 && string.Concat(words.Select(w => w[0])).StartsWith(qc)) return 80;
        if (qc.Length >= 3 && cc.Contains(qc)) return 74;

        double best = 0;
        var targets = new List<string> { cc };
        targets.AddRange(words);
        if (cc.Length > qc.Length) targets.Add(cc[..qc.Length]);
        foreach (var t in targets)
        {
            if (t.Length < 2) continue;
            int d = Text.Levenshtein(qc, t);
            double sim = 1.0 - (double)d / Math.Max(qc.Length, t.Length);
            if (t != cc) sim -= 0.04;
            best = Math.Max(best, sim);
        }
        if (qc.Length <= 3) return best >= 0.99 ? 70 : 0;
        if (best >= 0.72) return (int)(50 + best * 25);
        return 0;
    }

    public static int Score(string query, string candidate)
    {
        var q = Text.Clean(query);
        var c = Text.Clean(candidate);
        int best = ScoreClean(q, c);
        if (best >= 98) return best;
        var qw = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (qw.Length > 0)
        {
            var last = qw[^1];
            foreach (var v in Text.SuffixVariants(last).Skip(1))
            {
                var alt = string.Join(' ', qw.Take(qw.Length - 1).Append(v));
                best = Math.Max(best, ScoreClean(alt, c) - 3);
            }
        }
        return best;
    }

    public static (T item, int score) Best<T>(IEnumerable<T> items, string query, Func<T, IEnumerable<string>> names)
    {
        T bestItem = default;
        int best = 0, bestLen = int.MaxValue;
        foreach (var it in items)
            foreach (var n in names(it))
            {
                if (string.IsNullOrEmpty(n)) continue;
                int s = Score(query, n);
                if (s > best || (s == best && s > 0 && n.Length < bestLen)) { best = s; bestItem = it; bestLen = n.Length; }
            }
        return (bestItem, best);
    }
}

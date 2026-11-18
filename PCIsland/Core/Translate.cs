using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PCIsland.Core;

public record TranslateRequest(string Text, string Language, string Code);

public static class TranslateParser
{
    static readonly (string name, string code, string[] words)[] Langs =
    {
        ("Turkish", "tr", new[] { "turkish", "turkce", "turkceye", "turkcesi", "turkcesini", "tr" }),
        ("English", "en", new[] { "english", "ingilizce", "ingilizceye", "ingilizcesi", "ingilizcesini", "en", "eng" }),
        ("German", "de", new[] { "german", "almanca", "almancaya", "almancasi", "almancasini", "deutsch" }),
        ("French", "fr", new[] { "french", "fransizca", "fransizcaya", "fransizcasi", "fransizcasini" }),
        ("Spanish", "es", new[] { "spanish", "ispanyolca", "ispanyolcaya", "ispanyolcasi", "ispanyolcasini" }),
        ("Italian", "it", new[] { "italian", "italyanca", "italyancaya", "italyancasi" }),
        ("Russian", "ru", new[] { "russian", "rusca", "ruscaya", "ruscasi" }),
        ("Arabic", "ar", new[] { "arabic", "arapca", "arapcaya", "arapcasi" }),
        ("Japanese", "ja", new[] { "japanese", "japonca", "japoncaya", "japoncasi" }),
        ("Korean", "ko", new[] { "korean", "korece", "koreceye", "koreccesi", "korecesi" }),
        ("Chinese", "zh-CN", new[] { "chinese", "cince", "cinceye", "cincesi" }),
        ("Portuguese", "pt", new[] { "portuguese", "portekizce", "portekizceye", "portekizcesi" }),
        ("Dutch", "nl", new[] { "dutch", "felemenkce", "hollandaca" }),
        ("Azerbaijani", "az", new[] { "azerbaijani", "azerice", "azericeye", "azericesi" }),
    };

    static (string name, string code)? Lang(string folded)
    {
        foreach (var l in Langs) if (l.words.Contains(folded)) return (l.name, l.code);
        return null;
    }

    static readonly string[] CaseEndings = { "nun", "nün", "nın", "nin", "yu", "yü", "yı", "yi", "nu", "nü", "nı", "ni", "un", "ün", "ın", "in", "u", "ü", "ı", "i" };

    static string StripObjectSuffix(string s, string targetCode = null)
    {
        s = s.Trim().Trim('"', '\'', '“', '”');
        int i = s.LastIndexOfAny(new[] { '\'', '’' });
        if (i > 0 && s.Length - i - 1 <= 3) return s[..i].Trim().Trim('"', '“', '”');

        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count >= 2 && CaseEndings.Contains(parts[^1].ToLowerInvariant()))
        {
            parts.RemoveAt(parts.Count - 1);
            return string.Join(' ', parts);
        }

        if (targetCode == "tr" && parts.Count == 1)
        {
            var w = parts[0];
            foreach (var e in CaseEndings)
                if (w.Length - e.Length >= 3 && w.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                {
                    var stem = w[..^e.Length];
                    if (Ai.IsTurkish(stem)) continue;
                    bool vowelEnd = "aeıioöuü".Contains(char.ToLowerInvariant(stem[^1]));
                    bool needsY = e.StartsWith("y") || e.StartsWith("n");
                    if (needsY == vowelEnd) return stem;
                }
        }
        return s.Trim('"', '“', '”');
    }

    public static TranslateRequest Parse(string input)
    {
        var raw = input.Trim().TrimEnd('?', '.', '!').Trim();
        var f = Text.Fold(raw);

        var colon = Regex.Match(raw, @"^(?:translate\s+(?:to|into)\s+(\S+)|(?:bunu\s+)?(\S+?)(?:ye|ya|e|a)?\s+(?:çevir|cevir))\s*:\s*(.+)$", RegexOptions.IgnoreCase);
        if (colon.Success)
        {
            var lw = Text.Fold(colon.Groups[1].Success ? colon.Groups[1].Value : colon.Groups[2].Value);
            var lc = Lang(lw) ?? Lang(lw + "ye") ?? Lang(lw + "ya");
            if (lc != null) return new(StripObjectSuffix(colon.Groups[3].Value, colon.Groups[2].Success ? lc.Value.code : null), lc.Value.name, lc.Value.code);
        }

        var m = Regex.Match(raw, @"^(?:translate|cevir|çevir|tercume et|tercüme et)\s+(.+?)\s+(?:to|into|in)\s+(\S+)$", RegexOptions.IgnoreCase);
        if (m.Success && Lang(Text.Fold(m.Groups[2].Value)) is { } l1) return new(StripObjectSuffix(m.Groups[1].Value), l1.name, l1.code);

        m = Regex.Match(raw, @"^(.+?)\s+(\S+?)(?:ye|ya|e|a)?\s+(?:çevir|cevir|tercüme et|tercume et)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var lw = Text.Fold(m.Groups[2].Value);
            var l2 = Lang(lw) ?? Lang(lw + "ye") ?? Lang(lw + "ya");
            if (l2 != null) return new(StripObjectSuffix(m.Groups[1].Value, l2.Value.code), l2.Value.name, l2.Value.code);
        }

        m = Regex.Match(raw, @"^(.+?)\s+(\S+(?:cesi|çesi|casi|çası|cası|cesini|casini))(?:\s+(?:ne|nedir|ne demek|ne olur))?$", RegexOptions.IgnoreCase);
        if (m.Success && Lang(Text.Fold(m.Groups[2].Value)) is { } l3) return new(StripObjectSuffix(m.Groups[1].Value, l3.code), l3.name, l3.code);

        m = Regex.Match(raw, @"^(?:how do you say|how to say)\s+(.+?)\s+in\s+(\S+)$", RegexOptions.IgnoreCase);
        if (m.Success && Lang(Text.Fold(m.Groups[2].Value)) is { } l4) return new(StripObjectSuffix(m.Groups[1].Value), l4.name, l4.code);

        m = Regex.Match(raw, @"^(?:translate|çevir|cevir)\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var text = StripObjectSuffix(m.Groups[1].Value);
            return Ai.IsTurkish(text) ? new(text, "English", "en") : new(text, "auto", "tr");
        }
        return null;
    }
}

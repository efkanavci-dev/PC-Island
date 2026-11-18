using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PCIsland.Core;

public record RobloxGame(string Name, long PlaceId, long Players);

public static class Roblox
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
    static readonly Dictionary<string, RobloxGame> session = new(StringComparer.OrdinalIgnoreCase);

    public static string CleanName(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Normalize())
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '\'' || ch == '-' || ch == ':' || ch == '!' || ch == '.') sb.Append(ch);
            else if (char.IsWhiteSpace(ch)) sb.Append(' ');
        }
        var s = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\[[^\]]*\]|\([^)]*\)", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim(' ', '-', ':', '!');
        if (s.Length >= 4 && s.ToUpperInvariant() == s && s.Any(char.IsLetter))
            s = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        return s.Length == 0 ? name.Trim() : s;
    }

    public static string Initials(string name)
    {
        var words = Text.Clean(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Select(w => w[0]));
    }

    public static bool Matches(string query, string gameName)
    {
        var q = Text.Compact(query);
        if (q.Length == 0) return false;
        if (Initials(gameName) == q) return true;
        return Matcher.Score(query, gameName) >= 80;
    }

    public static async Task<RobloxGame> Search(string query)
    {
        query = query.Trim();
        lock (session) if (session.TryGetValue(query, out var hit)) return hit;
        RobloxGame best = null;
        try
        {
            var url = $"https://apis.roblox.com/search-api/omni-search?searchQuery={Uri.EscapeDataString(query)}&sessionId={Guid.NewGuid()}&pageType=all";
            using var resp = await http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                foreach (var group in doc.RootElement.GetProperty("searchResults").EnumerateArray())
                {
                    if (group.TryGetProperty("contentGroupType", out var t) && t.GetString() != "Game") continue;
                    foreach (var c in group.GetProperty("contents").EnumerateArray())
                    {
                        if (!c.TryGetProperty("rootPlaceId", out var pid) || pid.ValueKind != JsonValueKind.Number) continue;
                        var name = c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        long players = c.TryGetProperty("playerCount", out var pc) && pc.ValueKind == JsonValueKind.Number ? pc.GetInt64() : 0;
                        best = new RobloxGame(CleanName(name), pid.GetInt64(), players);
                        break;
                    }
                    if (best != null) break;
                }
            }
        }
        catch { best = null; }
        lock (session) session[query] = best;
        return best;
    }

    public static string LaunchUrl(long placeId) => $"roblox://experiences/start?placeId={placeId}";

    public static Game Remember(RobloxGame g, string asked)
    {
        var url = LaunchUrl(g.PlaceId);
        var existing = Settings.Current.Games.FirstOrDefault(x => x.Url == url);
        var words = new[] { asked, Text.Clean(g.Name), Initials(g.Name) }.Where(w => w.Length >= 2).Select(w => w.Trim().ToLowerInvariant());
        if (existing != null)
        {
            foreach (var w in words) if (!existing.Words.Contains(w)) existing.Words.Add(w);
        }
        else
        {
            existing = new Game { Name = g.Name, Url = url, Words = words.Distinct().ToList() };
            Settings.Current.Games.Add(existing);
        }
        Settings.Save();
        return existing;
    }
}

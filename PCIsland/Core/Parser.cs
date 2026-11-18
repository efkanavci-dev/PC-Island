using System;
using System.Collections.Generic;
using System.Linq;

namespace PCIsland.Core;

public enum Kind
{
    Open, Close, Delete, Search, Volume, Mute, Unmute, Brightness,
    Shutdown, Restart, Sleep, Lock, Theme, Settings, Help, Empty, Tabs
}

public enum Scope { Any, Folder, File }

public record Intent(Kind Kind, string Target = "", string RawTarget = "", int? Amount = null, int Step = 0, Scope Scope = Scope.Any)
{
    public override string ToString() =>
        $"{Kind}|{Target}|{Amount?.ToString() ?? ""}|{Step}|{Scope}";
}

public static class Parser
{
    static readonly HashSet<string> OpenVerbs = new() { "open", "launch", "start", "run", "ac", "baslat", "calistir", "acar", "acsana", "go" };
    static readonly HashSet<string> CloseVerbs = new() { "close", "kill", "quit", "exit", "kapat", "kapa", "sonlandir", "stop", "kapatsana" };
    static readonly HashSet<string> DeleteVerbs = new() { "delete", "remove", "del", "erase", "sil", "silsene" };
    static readonly HashSet<string> SearchVerbs = new() { "search", "google", "ara", "arat", "lookup" };
    static readonly HashSet<string> PcWords = new() { "pc", "computer", "bilgisayar", "bilgisayari", "pcyi", "laptop", "laptopu", "windows" };
    static readonly HashSet<string> VolumeWords = new() { "volume", "ses", "sesi", "sound", "vol", "sesini" };
    static readonly HashSet<string> BrightWords = new() { "brightness", "parlaklik", "parlakligi", "parlakligini", "bright" };
    static readonly HashSet<string> UpWords = new() { "up", "higher", "louder", "more", "increase", "arttir", "artir", "yukselt", "ac", "yukari", "brighter", "+" };
    static readonly HashSet<string> DownWords = new() { "down", "lower", "quieter", "less", "decrease", "kis", "azalt", "dusur", "asagi", "dimmer", "-" };
    static readonly HashSet<string> Filler = new() { "the", "app", "application", "program", "please", "lutfen", "uygulamasi", "uygulamasini", "uygulama", "my", "for", "a", "an", "to" };
    static readonly HashSet<string> FolderWords = new() { "folder", "klasor", "klasoru", "klasorunu", "klasorum", "klasorumu", "directory", "dir" };
    static readonly HashSet<string> FileWords = new() { "file", "dosya", "dosyasi", "dosyasini", "dosyayi", "dosyami" };

    public static Intent Parse(string input)
    {
        input = (input ?? "").Trim();
        if (input.Length == 0) return new Intent(Kind.Empty);

        var raw = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var folded = raw.Select(Text.Fold).ToArray();
        var words = folded.Select(w => w.Trim(',', '!', '?', ';')).ToArray();
        var bare = words.Select(w => Text.StripApostropheSuffix(w).Replace("'", "")).ToArray();
        string first = bare[0], last = bare[^1];
        string all = string.Join(' ', bare);

        if (first is "theme" or "tema" or "temayi" or "color" or "renk")
            return new Intent(Kind.Theme, string.Join(' ', bare.Skip(1)));
        if (bare.Length >= 2 && last is "tema" or "temasi" or "temaya" && bare.Length <= 3)
            return new Intent(Kind.Theme, string.Join(' ', bare.Take(bare.Length - 1)));
        if (bare.Length <= 2 && bare.Any(w => w is "settings" or "ayarlar" or "ayar" or "options" or "secenekler" or "ayarlari"))
            if (!bare.Any(w => w is "windows")) return new Intent(Kind.Settings);
        if (bare.Length == 1 && first is "help" or "yardim" or "?" or "commands" or "komutlar")
            return new Intent(Kind.Help);

        if (all is "mute" or "sessiz" or "sessize al" or "sesi kapat" or "sesi kes" or "mute sound" or "mute volume" or "sessiz mod")
            return new Intent(Kind.Mute);
        if (all is "unmute" or "sesi geri ac" or "sessizden cik" or "unmute sound" or "sessizi kapat")
            return new Intent(Kind.Unmute);

        int? number = null;
        foreach (var w in bare)
        {
            var t = w.TrimEnd('%').TrimStart('%');
            if (int.TryParse(t, out var n)) { number = Math.Clamp(n, 0, 100); break; }
        }

        if (bare.Any(VolumeWords.Contains) && bare.Length <= 4)
        {
            if (bare.Any(w => w is "kapat" or "off" or "mute")) return new Intent(Kind.Mute);
            if (number != null) return new Intent(Kind.Volume, Amount: number);
            if (bare.Any(DownWords.Contains)) return new Intent(Kind.Volume, Step: -10);
            if (bare.Any(UpWords.Contains)) return new Intent(Kind.Volume, Step: +10);
            return new Intent(Kind.Volume, Step: 0);
        }
        if (bare.Any(BrightWords.Contains) && bare.Length <= 4)
        {
            if (number != null) return new Intent(Kind.Brightness, Amount: number);
            if (bare.Any(DownWords.Contains)) return new Intent(Kind.Brightness, Step: -10);
            if (bare.Any(UpWords.Contains)) return new Intent(Kind.Brightness, Step: +10);
            return new Intent(Kind.Brightness, Step: 0);
        }

        if (all is "shutdown" or "shut down" or "power off" or "turn off" or "turn off pc" or "turn off computer" or "shut down pc" or "shutdown pc" or "shut down computer" or "kapan")
            return new Intent(Kind.Shutdown);
        if (bare.Length <= 3 && bare.Any(PcWords.Contains) && bare.Any(w => CloseVerbs.Contains(w) || w is "shutdown" or "off"))
            return new Intent(Kind.Shutdown);
        if (all is "restart" or "reboot" or "yeniden baslat" or "restart pc" or "restart computer" or "reboot pc" or "bilgisayari yeniden baslat" or "pc yeniden baslat" or "pcyi yeniden baslat" or "reset pc")
            return new Intent(Kind.Restart);
        if (all is "sleep" or "uyku" or "uyu" or "uyut" or "uyku modu" or "sleep mode" or "go to sleep" or "pc sleep" or "bilgisayari uyut" or "pcyi uyut" or "sleep pc")
            return new Intent(Kind.Sleep);
        if (all is "lock" or "kilitle" or "lock pc" or "lock screen" or "ekrani kilitle" or "bilgisayari kilitle" or "pcyi kilitle" or "lock computer")
            return new Intent(Kind.Lock);

        var tabWord = bare.FirstOrDefault(w => w.StartsWith("tab") && w.Length <= 4 || w.StartsWith("sekme"));
        if (tabWord != null && bare.Any(w => CloseVerbs.Contains(w)))
        {
            bool plural = tabWord is "tabs" || tabWord.StartsWith("sekmeler");
            bool every = bare.Any(w => w is "all" or "every" or "tum" or "butun" or "hepsini" or "hepsi");
            int count = number is > 0 ? number.Value : (plural || every ? -1 : 1);
            var browserWords = bare.Where(w => !CloseVerbs.Contains(w) && w != tabWord
                    && !(w is "all" or "every" or "tum" or "butun" or "hepsini" or "hepsi" or "on" or "in" or "of" or "the" or "my" or "from")
                    && !int.TryParse(w, out _))
                .Select(w => StripLocative(w));
            return new Intent(Kind.Tabs, string.Join(' ', browserWords), Amount: count);
        }

        Kind kind = Kind.Open;
        int start = 0, end = bare.Length;
        bool verbFound = false;

        (HashSet<string> set, Kind k)[] verbSets =
        {
            (SearchVerbs, Kind.Search), (DeleteVerbs, Kind.Delete), (CloseVerbs, Kind.Close), (OpenVerbs, Kind.Open)
        };
        foreach (var (set, k) in verbSets)
        {
            if (set.Contains(first) && bare.Length > 1) { kind = k; start = 1; verbFound = true; break; }
            if (set.Contains(last) && bare.Length > 1) { kind = k; end = bare.Length - 1; verbFound = true; break; }
        }
        if (!verbFound && bare.Length >= 2 && bare[^2] is "look" && last is "up") { kind = Kind.Search; end -= 2; }
        if (kind == Kind.Search && start == 1 && start < end && bare[start] == "for") start++;

        if (kind == Kind.Search)
        {
            var q = string.Join(' ', raw.Skip(start).Take(end - start));
            return new Intent(Kind.Search, Text.Clean(q), q);
        }

        var scope = Scope.Any;
        var keepIdx = new List<int>();
        for (int i = start; i < end; i++)
        {
            var w = bare[i];
            if (FolderWords.Contains(w)) { scope = Scope.Folder; continue; }
            if (FileWords.Contains(w)) { scope = Scope.File; continue; }
            if (Filler.Contains(w) && end - start > 1) continue;
            keepIdx.Add(i);
        }
        if (keepIdx.Count == 0) keepIdx.AddRange(Enumerable.Range(start, end - start));

        string target = string.Join(' ', keepIdx.Select(i => bare[i]));
        string rawTarget = string.Join(' ', keepIdx.Select(i => RawWord(raw[i])));
        return new Intent(kind, target, rawTarget, Scope: scope);
    }

    static string StripLocative(string w)
    {
        foreach (var s in new[] { "daki", "deki", "taki", "teki", "nin", "nun", "nin", "in", "un", "da", "de" })
            if (w.Length - s.Length >= 4 && w.EndsWith(s)) return w[..^s.Length];
        return w;
    }

    static string RawWord(string w)
    {
        w = w.Trim(',', '!', '?', ';');
        int i = w.LastIndexOfAny(new[] { '\'', '’' });
        if (i > 0 && w.Length - i - 1 <= 5 && w[(i + 1)..].All(char.IsLetter)) return w[..i];
        return w;
    }
}

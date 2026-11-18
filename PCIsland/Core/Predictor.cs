using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCIsland.Core;

public static class Predictor
{
    static readonly string[] Commands =
    {
        "open", "close", "delete", "search", "volume", "brightness", "mute", "unmute", "theme", "settings",
        "shutdown", "restart", "sleep", "lock", "help", "google",
        "tema", "ayarlar", "ses", "sesi aç", "sesi kıs", "sessiz", "parlaklık", "kilitle", "uyku modu",
        "yeniden başlat", "bilgisayarı kapat", "yardım",
        "pause", "next", "previous", "sonraki şarkı", "önceki şarkı", "durdur", "now playing",
        "timer", "stop timer", "sayaç", "remind me", "reminders", "battery", "ram", "cpu",
        "wifi off", "wifi on", "bluetooth off", "bluetooth on", "dark mode", "light mode",
        "screenshot", "screenshot area", "ekran görüntüsü", "note", "notes", "notlar", "clipboard",
        "minimize all", "switch to", "boost", "play brookhaven", "close tabs", "close tab", "guide", "kılavuz", "translate", "disk"
    };

    static readonly string[] OpenVerbs = { "open", "start", "launch", "run" };
    static readonly string[] CloseVerbs = { "close", "kill", "quit" };
    static readonly string[] ThemeVerbs = { "theme", "tema" };

    public static List<string> History { get; private set; } = new();
    public static IReadOnlyList<string> RunningNames = Array.Empty<string>();
    static List<string> folded = new();

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCIsland", "history.txt");

    public static void LoadHistory()
    {
        try { if (File.Exists(FilePath)) History = File.ReadAllLines(FilePath).Where(l => l.Trim().Length > 0).ToList(); } catch { }
    }

    public static void Remember(string cmd)
    {
        cmd = cmd.Trim();
        if (cmd.Length == 0) return;
        History.Add(cmd);
        folded = new();
        if (History.Count > 300) History.RemoveRange(0, History.Count - 300);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, History);
        }
        catch { }
    }

    static readonly string[] TakesArgs = { "open", "close", "delete", "search", "volume", "brightness", "theme", "tema", "google", "ses", "parlaklık", "start", "launch", "run", "kill", "quit", "timer", "remind me", "note", "switch to", "sayaç", "translate" };

    public static bool WantsSpace(string p) => TakesArgs.Contains(p.Trim().ToLowerInvariant());

    static bool Starts(string full, string prefix) =>
        full.Length > prefix.Length && Text.Fold(full).StartsWith(Text.Fold(prefix));

    static int Uses(string name)
    {
        if (folded.Count != History.Count) folded = History.Select(Text.Fold).ToList();
        var f = Text.Fold(name);
        int n = 0;
        foreach (var h in folded) if (h.Contains(f)) n++;
        return n;
    }

    static string BestName(IEnumerable<string> names, string prefix)
    {
        if (prefix.Length == 0) return null;
        return names
            .Where(n => !string.IsNullOrWhiteSpace(n) && Starts(n, prefix))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(Uses)
            .ThenBy(n => n.Length)
            .FirstOrDefault();
    }

    static IEnumerable<string> OpenTargets(IEnumerable<AppEntry> apps) =>
        Settings.Current.Shortcuts.Select(s => s.Name)
            .Concat(Learner.Aliases.Keys)
            .Concat(apps.SelectMany(a => a.Names))
            .Concat(Places.Sites.SelectMany(s => s.Words.Prepend(s.Name)))
            .Concat(Places.Folders.SelectMany(f => f.Words.Prepend(f.Name)));

    public static string Predict(string typed, IEnumerable<AppEntry> apps = null)
    {
        if (string.IsNullOrEmpty(typed) || typed.EndsWith("  ")) return null;
        apps ??= AppIndex.All;

        var fromHistory = Enumerable.Reverse(History).FirstOrDefault(h => Starts(h, typed));
        if (fromHistory != null) return typed + fromHistory[typed.Length..];

        int sp = typed.IndexOf(' ');
        if (sp > 0)
        {
            var verb = Text.Fold(typed[..sp]);
            var rest = typed[(sp + 1)..];
            if (rest.Length == 0) return null;
            IEnumerable<string> pool = null;
            if (OpenVerbs.Contains(verb))
            {
                var sc = BestName(Settings.Current.Shortcuts.Select(s => s.Name), rest);
                if (sc != null) return typed[..(sp + 1)] + sc;
                pool = OpenTargets(apps);
            }
            else if (CloseVerbs.Contains(verb)) pool = RunningNames.Concat(apps.SelectMany(a => a.Names));
            else if (ThemeVerbs.Contains(verb)) pool = Themes.All.SelectMany(t => t.Words.Prepend(t.Name));
            if (pool != null)
            {
                var name = BestName(pool, rest);
                if (name != null) return typed[..(sp + 1)] + name;
            }
            var cmd = BestName(Commands, typed);
            return cmd == null ? null : typed + cmd[typed.Length..];
        }

        var c = BestName(Commands, typed);
        if (c != null && typed.Length >= 2) return typed + c[typed.Length..];
        var target = BestName(Settings.Current.Shortcuts.Select(s => s.Name).Concat(Learner.Aliases.Keys), typed)
                     ?? BestName(apps.SelectMany(a => a.Names), typed);
        if (target != null) return target;
        return c == null ? null : typed + c[typed.Length..];
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PCIsland.Core;

public record AppEntry(string Name, string Launch, bool IsShellApp, string[] Aliases)
{
    public IEnumerable<string> Names => Aliases.Prepend(Name);
}

public static class AppIndex
{
    static List<AppEntry> apps = new();
    static readonly object gate = new();
    public static int Count { get { lock (gate) return apps.Count; } }
    public static List<AppEntry> All { get { lock (gate) return apps.ToList(); } }

    static readonly Dictionary<string, string[]> KnownAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google Chrome"] = new[] { "chrome", "krom" },
        ["Visual Studio Code"] = new[] { "vs code", "vscode", "code" },
        ["File Explorer"] = new[] { "explorer", "dosya gezgini", "files", "dosyalar" },
        ["Task Manager"] = new[] { "gorev yoneticisi", "taskmgr" },
        ["Calculator"] = new[] { "hesap makinesi", "calc" },
        ["Notepad"] = new[] { "not defteri" },
        ["Command Prompt"] = new[] { "cmd" },
        ["Terminal"] = new[] { "windows terminal" },
        ["Microsoft Edge"] = new[] { "edge" },
        ["Opera GX Browser"] = new[] { "opera", "opera gx", "gx" },
        ["Mozilla Firefox"] = new[] { "firefox" },
        ["Snipping Tool"] = new[] { "ekran alintisi", "screenshot" },
        ["Microsoft Store"] = new[] { "store", "magaza" },
        ["Control Panel"] = new[] { "denetim masasi" },
        ["Paint"] = new[] { "mspaint" },
        ["Camera"] = new[] { "kamera" },
        ["Photos"] = new[] { "fotograflar" },
        ["Clock"] = new[] { "saat", "alarm" },
        ["Settings"] = new[] { "windows settings", "windows ayarlari" },
        ["SteelSeries GG"] = new[] { "steelseries", "sonar", "gg" },
        ["OMEN Gaming Hub"] = new[] { "omen" },
        ["Riot Client"] = new[] { "riot", "valorant launcher" },
        ["Roblox Player"] = new[] { "roblox" },
        ["Notepad++"] = new[] { "notepad plus", "npp" },
    };

    static readonly string[] Junk = { "uninstall", "kaldir", "readme", "read me", "help", "documentation", "release notes", "website", "license", "manual", "support", "web site" };

    public static void Load()
    {
        var list = new List<AppEntry>();
        try { list.AddRange(ReadAppsFolder()); } catch (Exception ex) { Debug.WriteLine(ex); }
        try { list.AddRange(ReadFolders()); } catch (Exception ex) { Debug.WriteLine(ex); }

        var dedup = list
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Where(a => !Junk.Any(j => Text.Fold(a.Name).Contains(j)))
            .GroupBy(a => Text.Fold(a.Name).Trim())
            .Select(g => g.OrderByDescending(a => a.IsShellApp ? 0 : 1).First())
            .Select(a => a with { Aliases = KnownAliases.TryGetValue(a.Name, out var al) ? al : a.Aliases })
            .ToList();
        lock (gate) apps = dedup;
    }

    static List<AppEntry> ReadAppsFolder()
    {
        var result = new List<AppEntry>();
        var t = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = Activator.CreateInstance(type);
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (dynamic item in folder.Items())
                {
                    string name = item.Name;
                    string path = item.Path;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                        result.Add(new AppEntry(name, path, true, Array.Empty<string>()));
                }
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(15));
        return result;
    }

    public static IEnumerable<string> ToolFolders()
    {
        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return desk;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        foreach (var extra in Settings.Current.ExtraAppFolders ?? new List<string>())
            if (!string.IsNullOrWhiteSpace(extra)) yield return Environment.ExpandEnvironmentVariables(extra);
    }

    static bool IsExtra(string dir) =>
        (Settings.Current.ExtraAppFolders ?? new List<string>()).Any(e => string.Equals(Environment.ExpandEnvironmentVariables(e).TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

    static IEnumerable<AppEntry> ReadFolders()
    {
        var exts = new[] { ".lnk", ".exe", ".url", ".bat", ".cmd", ".pyw", ".py", ".html", ".appref-ms" };
        foreach (var dir in ToolFolders().Distinct())
        {
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir).ToList(); } catch { continue; }
            foreach (var f in files)
            {
                if (!exts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                yield return new AppEntry(Path.GetFileNameWithoutExtension(f).Trim(), f, false, Array.Empty<string>());
            }
            if (!IsExtra(dir)) continue;
            List<string> subs;
            try { subs = Directory.EnumerateDirectories(dir).ToList(); } catch { continue; }
            foreach (var sub in subs)
            {
                List<string> inner;
                try { inner = Directory.EnumerateFiles(sub).Where(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".pyw", StringComparison.OrdinalIgnoreCase)).ToList(); } catch { continue; }
                foreach (var x in inner)
                    yield return new AppEntry(Path.GetFileNameWithoutExtension(x).Trim(), x, false, new[] { Path.GetFileName(sub) });
            }
        }
    }

    public static (AppEntry app, int score) Find(string query)
    {
        AppEntry best = null;
        int bestScore = 0, bestRank = 0, bestLen = int.MaxValue;
        foreach (var a in All)
        {
            int s = 0, len = int.MaxValue;
            foreach (var n in a.Names)
            {
                int sc = Matcher.Score(query, n);
                if (sc > s || sc == s && n.Length < len) { s = sc; len = n.Length; }
            }
            if (s == 0) continue;
            int rank = s >= 70 ? Math.Min(100, s + Learner.Bonus(a.Name)) : s;
            if (rank > bestRank || rank == bestRank && len < bestLen) { best = a; bestScore = s; bestRank = rank; bestLen = len; }
        }
        return (best, bestScore);
    }

    public static void Launch(AppEntry app)
    {
        if (app.IsShellApp)
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + app.Launch) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo(app.Launch) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(app.Launch) });
    }
}

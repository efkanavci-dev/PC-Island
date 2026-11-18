using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace PCIsland.Core;

public static class Riot
{
    static readonly (string product, string name, string[] words)[] Products =
    {
        ("valorant", "VALORANT", new[] { "valorant", "val", "valo" }),
        ("league_of_legends", "League of Legends", new[] { "league of legends", "league", "lol", "teamfight tactics", "tft" }),
        ("bacon", "Legends of Runeterra", new[] { "legends of runeterra", "runeterra", "lor" }),
    };

    public static (string product, string name)? Find(string query)
    {
        var q = Text.Clean(query);
        foreach (var p in Products)
            if (p.words.Contains(q)) return (p.product, p.name);
        return null;
    }

    public static string ClientPath()
    {
        var (app, score) = AppIndex.Find("riot client");
        if (app != null && score >= 98 && !app.IsShellApp && app.Launch.EndsWith("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(app.Launch))
            return app.Launch;
        if (app != null && score >= 98 && app.IsShellApp && app.Launch.EndsWith("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(app.Launch))
            return app.Launch;

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var un = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (un == null) continue;
                foreach (var sub in un.GetSubKeyNames().Where(n => n.StartsWith("Riot Game", StringComparison.OrdinalIgnoreCase)))
                {
                    using var k = un.OpenSubKey(sub);
                    var s = k?.GetValue("UninstallString") as string;
                    if (string.IsNullOrEmpty(s)) continue;
                    int i = s.IndexOf("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    var path = s[..(i + "RiotClientServices.exe".Length)].Trim('"', ' ');
                    if (File.Exists(path)) return path;
                }
            }
            catch { }
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var p = Path.Combine(drive.RootDirectory.FullName, "Riot Games", "Riot Client", "RiotClientServices.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static Reply Launch(string product, string name)
    {
        var client = ClientPath();
        if (client == null) return null;
        try
        {
            Process.Start(new ProcessStartInfo(client, $"--launch-product={product} --launch-patchline=live")
            { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(client) });
            var (app, s) = AppIndex.Find(name);
            Brain.LastIcon = app != null && s >= 98 ? (app.IsShellApp ? "shell:AppsFolder\\" + app.Launch : app.Launch) : client;
            return new Message($"Starting {name}");
        }
        catch (Exception ex) { return new Message($"Couldn't start {name}: {ex.Message}", false); }
    }
}

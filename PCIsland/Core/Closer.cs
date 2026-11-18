using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PCIsland.Core;

public record RunningApp(string Display, string ProcessName, List<int> Ids, bool HasWindow);

public static class Closer
{
    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "winlogon", "wininit", "services", "lsass", "svchost", "smss", "fontdrvhost",
        "ApplicationFrameHost", "TextInputHost", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost",
        "LockApp", "sihost", "taskhostw", "ctfmon", "RuntimeBroker", "SystemSettingsBroker", "conhost", "OpenConsole",
        "claude", "vgtray", "vgc", "System", "Idle", "Registry", "MsMpEng", "audiodg",
        "SecurityHealthSystray", "nvcontainer", "WidgetService", "Widgets", "PCIsland", "IslandPreview"
    };

    public static List<RunningApp> List()
    {
        int me = Environment.ProcessId;
        int session = Process.GetCurrentProcess().SessionId;
        var map = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == me || p.SessionId != session || Protected.Contains(p.ProcessName)) continue;
                string path = null;
                try { path = p.MainModule?.FileName; } catch { }
                if (path == null) continue;
                bool hasWindow = p.MainWindowHandle != IntPtr.Zero;
                if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) && !hasWindow) continue;

                string display = null;
                try { display = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch { }
                if (string.IsNullOrWhiteSpace(display)) display = p.ProcessName;
                display = display.Trim();
                if (display.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) display = display[..^4];

                if (!map.TryGetValue(p.ProcessName, out var ra))
                {
                    ra = new RunningApp(display.Trim(), p.ProcessName, new List<int>(), false);
                    map[p.ProcessName] = ra;
                }
                ra.Ids.Add(p.Id);
                if (hasWindow) map[p.ProcessName] = ra with { HasWindow = true, Display = ra.HasWindow ? ra.Display : Pick(display, p.MainWindowTitle) };
            }
            catch { }
            finally { p.Dispose(); }
        }
        return map.Values.ToList();
    }

    static string Pick(string desc, string title) => string.IsNullOrWhiteSpace(desc) ? title : desc;

    public static (RunningApp app, int score) Find(string query)
    {
        var list = List();
        var (app, score) = Matcher.Best(list, query, a => new[] { a.Display, a.ProcessName });
        if (score < 70)
        {
            var (entry, s2) = AppIndex.Find(query);
            if (entry != null && s2 >= 80)
            {
                var (a2, s3) = Matcher.Best(list, entry.Name, a => new[] { a.Display, a.ProcessName });
                if (s3 >= 80 && s3 > score) return (a2, Math.Min(s2, s3));
            }
        }
        return (app, score);
    }

    public static async Task<string> Close(RunningApp app)
    {
        var procs = app.Ids.Select(id => { try { return Process.GetProcessById(id); } catch { return null; } })
                           .Where(p => p != null).ToList();
        foreach (var p in procs)
            try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { }

        for (int i = 0; i < 20 && procs.Any(Alive); i++) await Task.Delay(100);

        var stuck = procs.Where(Alive).ToList();
        bool asking = stuck.Any(p => { try { p.Refresh(); return p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0 && HasVisibleDialog(p); } catch { return false; } });
        if (asking) return $"{app.Display} is asking you something";

        int denied = 0;
        foreach (var p in stuck)
            try { p.Kill(true); } catch { denied++; }
        await Task.Delay(300);
        if (procs.Any(Alive)) return denied > 0 ? $"Couldn't close {app.Display} (needs admin)" : $"{app.Display} is still running";
        return null;
    }

    static bool Alive(Process p) { try { p.Refresh(); return !p.HasExited; } catch { return false; } }

    static bool HasVisibleDialog(Process p)
    {
        try { return p.MainWindowTitle.Length > 0 && Native.IsWindowVisible(p.MainWindowHandle) && !Native.IsWindowEnabled(p.MainWindowHandle); }
        catch { return false; }
    }
}

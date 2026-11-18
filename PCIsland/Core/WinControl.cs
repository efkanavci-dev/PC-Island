using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PCIsland.Core;

public static class WinControl
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);

    public record Win(IntPtr Handle, string Title, string Process, string Display);

    public static List<Win> Windows()
    {
        var names = new Dictionary<uint, (string proc, string display)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string display = p.ProcessName;
                try { var d = p.MainModule?.FileVersionInfo.FileDescription; if (!string.IsNullOrWhiteSpace(d)) display = d.Trim(); } catch { }
                if (display.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) display = display[..^4];
                names[(uint)p.Id] = (p.ProcessName, display);
            }
            catch { }
            p.Dispose();
        }
        var list = new List<Win>();
        EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;
            if ((GetWindowLong(h, -20) & 0x80) != 0) return true;
            int len = GetWindowTextLength(h);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == Environment.ProcessId) return true;
            if (!names.TryGetValue(pid, out var n)) return true;
            if (n.proc is "TextInputHost" or "ShellExperienceHost" or "SearchHost" or "StartMenuExperienceHost") return true;
            if (n.proc == "explorer" && sb.ToString() == "Program Manager") return true;
            list.Add(new Win(h, sb.ToString(), n.proc, n.display));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static (Win win, int score) Find(string query)
    {
        var wins = Windows();
        if (Learner.TryAlias(query, out var alias) && !alias.Contains('\\')) query = alias;
        var (w, s) = Matcher.Best(wins, query, x => new[] { x.Display, x.Process, x.Title });
        return (w, s);
    }

    public static void Focus(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, 9);
        Native.ForceForeground(h);
    }

    public static void Minimize(IntPtr h) => ShowWindow(h, 6);
    public static void Maximize(IntPtr h) { Focus(h); ShowWindow(h, 3); }

    public static void MinimizeAll()
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            shell.MinimizeAll();
        }
        catch
        {
            keybd_event(0x5B, 0, 0, UIntPtr.Zero); keybd_event(0x44, 0, 0, UIntPtr.Zero);
            keybd_event(0x44, 0, 2, UIntPtr.Zero); keybd_event(0x5B, 0, 2, UIntPtr.Zero);
        }
    }

    public static async Task Snap(IntPtr h, bool left)
    {
        Focus(h);
        await Task.Delay(250);
        if (Native.GetForegroundWindow() != h) return;
        if (IsIconic(h)) ShowWindow(h, 9);
        ShowWindow(h, 1);
        await Task.Delay(80);
        byte arrow = (byte)(left ? 0x25 : 0x27);
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(arrow, 0, 1, UIntPtr.Zero);
        keybd_event(arrow, 0, 3, UIntPtr.Zero);
        keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }
}

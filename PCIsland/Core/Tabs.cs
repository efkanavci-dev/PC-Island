using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PCIsland.Core;

public static class Tabs
{
    public static readonly Dictionary<string, string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["opera"] = "Opera", ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox",
        ["brave"] = "Brave", ["vivaldi"] = "Vivaldi"
    };

    public static IntPtr LastForeground;

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }

    const uint KEYUP = 0x2, UNICODE = 0x4;
    const ushort VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12, VK_RETURN = 0x0D, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    static INPUT Key(ushort vk, bool up) => new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYUP : 0 } } };
    static INPUT Uni(char c, bool up) => new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = UNICODE | (up ? KEYUP : 0) } } };

    static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    static void ReleaseModifiers() =>
        Send(Key(VK_CONTROL, true), Key(VK_SHIFT, true), Key(VK_MENU, true), Key(VK_LWIN, true), Key(VK_RWIN, true));

    static IntPtr target;

    class FocusLost : Exception { }

    static void Ctrl(ushort vk)
    {
        if (target != IntPtr.Zero && Native.GetForegroundWindow() != target) throw new FocusLost();
        Send(Key(VK_CONTROL, false), Key(vk, false), Key(vk, true), Key(VK_CONTROL, true));
    }

    static async Task<(int, string)> Guard(Func<Task<(int, string)>> body, BrowserWindow w)
    {
        target = w.Handle;
        try { return await body(); }
        catch (FocusLost) { return (0, $"Stopped: {w.Display} lost focus"); }
        finally { target = IntPtr.Zero; }
    }

    static void Type(string s)
    {
        var list = new List<INPUT>();
        foreach (var c in s) { list.Add(Uni(c, false)); list.Add(Uni(c, true)); }
        Send(list.ToArray());
    }

    static string Title(IntPtr h)
    {
        int n = GetWindowTextLength(h);
        var sb = new StringBuilder(n + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public record BrowserWindow(IntPtr Handle, string ProcessName, string Display);

    public static List<BrowserWindow> FindWindows()
    {
        var pidName = new Dictionary<uint, string>();
        foreach (var p in Process.GetProcesses())
        {
            try { if (Browsers.ContainsKey(p.ProcessName)) pidName[(uint)p.Id] = p.ProcessName; } catch { }
            p.Dispose();
        }
        var list = new List<BrowserWindow>();
        EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (!pidName.TryGetValue(pid, out var name)) return true;
            if (GetWindowTextLength(h) == 0) return true;
            var cls = new StringBuilder(64);
            Native.GetClassName(h, cls, 64);
            if (!cls.ToString().StartsWith("Chrome_WidgetWin") && cls.ToString() != "MozillaWindowClass") return true;
            list.Add(new BrowserWindow(h, name, Browsers[name]));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static BrowserWindow Pick(string browser)
    {
        var wins = FindWindows();
        if (wins.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(browser))
        {
            var (w, score) = Matcher.Best(wins, browser, x => new[] { x.Display, x.ProcessName });
            if (w == null || score < 75) return null;
            var same = wins.Where(x => x.ProcessName == w.ProcessName).ToList();
            return same.FirstOrDefault(x => x.Handle == LastForeground) ?? same[0];
        }
        return wins.FirstOrDefault(x => x.Handle == LastForeground) ?? wins[0];
    }

    static async Task Focus(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, 9);
        Native.ForceForeground(h);
        await Task.Delay(250);
        ReleaseModifiers();
        await Task.Delay(50);
    }

    public static async Task<string> CloseOne(BrowserWindow w, int count)
    {
        var (_, err) = await Guard(async () =>
        {
            await Focus(w.Handle);
            for (int i = 0; i < count && IsWindow(w.Handle); i++)
            {
                Ctrl(0x57);
                await Task.Delay(180);
            }
            return (0, null);
        }, w);
        return err;
    }

    const string Marker = "PCIsland-keep";

    public static Task<(int closed, string error)> CloseAll(BrowserWindow w) => Guard(() => CloseAllInner(w), w);

    static async Task<(int closed, string error)> CloseAllInner(BrowserWindow w)
    {
        var h = w.Handle;
        await Focus(h);
        Ctrl(0x54);
        await Task.Delay(450);
        Ctrl(0x4C);
        await Task.Delay(120);
        if (Native.GetForegroundWindow() != h) throw new FocusLost();
        Type($"data:text/html,<title>{Marker}</title>");
        await Task.Delay(60);
        if (Native.GetForegroundWindow() != h) throw new FocusLost();
        Send(Key(VK_RETURN, false), Key(VK_RETURN, true));

        var sw = Stopwatch.StartNew();
        while (!Title(h).Contains(Marker))
        {
            if (sw.ElapsedMilliseconds > 3000 || !IsWindow(h)) return (0, $"{w.Display} didn't open the helper tab, so nothing was closed");
            await Task.Delay(80);
        }

        int closed = 0, slot = 1, sameCount = 0, skipped = 0;
        string lastTitle = null;
        for (int i = 0; i < 300; i++)
        {
            if (!IsWindow(h)) return (closed, $"{w.Display} window closed unexpectedly");
            if (Native.GetForegroundWindow() != h) await Focus(h);
            Ctrl((ushort)(0x30 + slot));
            await Task.Delay(200);
            var t = Title(h);
            if (t.Contains(Marker))
            {
                await Task.Delay(150);
                if (Title(h).Contains(Marker)) break;
                t = Title(h);
            }
            if (t == lastTitle && ++sameCount >= 3)
            {
                slot++; skipped++; sameCount = 0; lastTitle = null; closed -= 3;
                if (slot > 8) return (Math.Max(closed, 0), "Some tabs wouldn't close");
                continue;
            }
            if (t != lastTitle) sameCount = 0;
            lastTitle = t;
            Ctrl(0x57);
            closed++;
            await Task.Delay(140);
        }
        closed = Math.Max(closed, 0);

        if (!IsWindow(h) || !Title(h).Contains(Marker)) return (closed, "Stopped early to be safe");

        Ctrl(0x54);
        await Task.Delay(450);
        Ctrl(0x21);
        await Task.Delay(250);
        if (Title(h).Contains(Marker)) Ctrl(0x57);
        await Task.Delay(150);
        return (closed, null);
    }
}

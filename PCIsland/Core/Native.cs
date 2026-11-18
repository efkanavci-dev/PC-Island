using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace PCIsland.Core;

public static class Native
{
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
    [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }
    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }

    static readonly System.Collections.Generic.Dictionary<uint, bool> gameCache = new();

    static readonly string[] KnownGames =
    {
        "RobloxPlayerBeta", "VALORANT-Win64-Shipping", "javaw", "Minecraft.Windows", "FortniteClient-Win64-Shipping",
        "cs2", "csgo", "GTA5", "RocketLeague", "r5apex", "League of Legends", "Overwatch", "destiny2", "eldenring",
        "Cyberpunk2077", "RainbowSix", "TslGame", "bf2042", "Palworld-Win64-Shipping", "MarvelRivals_Launcher", "Marvel-Win64-Shipping"
    };

    static readonly string[] NotGames =
    {
        "opera", "chrome", "msedge", "firefox", "brave", "vivaldi", "vlc", "mpc-hc64", "mpc-hc", "PotPlayerMini64", "Video.UI",
        "Microsoft.Media.Player", "explorer", "ApplicationFrameHost", "Discord", "Spotify", "POWERPNT", "WINWORD", "EXCEL",
        "Code", "WindowsTerminal", "devenv", "obs64", "Photos", "mspaint", "Netflix", "Teams", "ms-teams", "Zoom"
    };

    static bool IsGameProcess(uint pid)
    {
        if (gameCache.TryGetValue(pid, out var known)) return known;
        bool game = false;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = p.ProcessName;
            if (KnownGames.Any(g => g.Equals(name, StringComparison.OrdinalIgnoreCase))) game = true;
            else if (NotGames.Any(g => g.Equals(name, StringComparison.OrdinalIgnoreCase))) game = false;
            else
            {
                string path = "";
                try { path = p.MainModule?.FileName ?? ""; } catch { }
                var lower = path.ToLowerInvariant();
                if (lower.Contains("\\steamapps\\") || lower.Contains("\\epic games\\") || lower.Contains("\\riot games\\") ||
                    lower.Contains("\\roblox\\") || lower.Contains("\\xboxgames\\") || lower.Contains("\\gog galaxy\\games\\") || lower.Contains("\\ea games\\"))
                    game = true;
                else
                {
                    try
                    {
                        foreach (System.Diagnostics.ProcessModule m in p.Modules)
                        {
                            var mn = m.ModuleName.ToLowerInvariant();
                            if (mn is "unityplayer.dll" or "gameassembly.dll" or "vulkan-1.dll" or "d3d12.dll" or "steam_api64.dll" or "steam_api.dll" or "eossdk-win64-shipping.dll")
                            { game = true; break; }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
        if (gameCache.Count > 200) gameCache.Clear();
        gameCache[pid] = game;
        return game;
    }

    public static bool IsFullscreenAppRunning()
    {
        try
        {
            if (SHQueryUserNotificationState(out int st) == 0 && st == 3) return true;
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
            var cls = new System.Text.StringBuilder(64);
            GetClassName(fg, cls, 64);
            var c = cls.ToString();
            if (c is "WorkerW" or "Progman" or "Shell_TrayWnd") return false;
            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == Environment.ProcessId) return false;
            if (!GetWindowRect(fg, out var r)) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(fg, 2), ref mi)) return false;
            var m = mi.rcMonitor;
            bool full = r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
            return full && IsGameProcess(pid);
        }
        catch { return false; }
    }

    public static void ForceForeground(IntPtr hwnd)
    {
        var fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
        if (fgThread != me && fgThread != 0) AttachThreadInput(me, fgThread, true);
        SetForegroundWindow(hwnd);
        if (fgThread != me && fgThread != 0) AttachThreadInput(me, fgThread, false);
        if (GetForegroundWindow() != hwnd)
        {
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
        }
    }

    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}

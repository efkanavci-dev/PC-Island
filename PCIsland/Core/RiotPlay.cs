using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PCIsland.Core;

public static class RiotPlay
{
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Native.RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    static bool IsPlayRed(Color c) => c.R >= 200 && c.G <= 120 && c.B <= 130 && c.R - c.G >= 110;

    /// <summary>Finds the solid red Play button in a picture of the Riot Client window. Returns its center (window coordinates).</summary>
    public static Point? FindPlay(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height, step = 3;
        int x0 = 0, x1 = (int)(w * 0.36), y0 = (int)(h * 0.62), y1 = h;
        int gw = (x1 - x0) / step + 1, gh = (y1 - y0) / step + 1;
        var mask = new bool[gw, gh];
        for (int gx = 0; gx < gw; gx++)
            for (int gy = 0; gy < gh; gy++)
            {
                int x = x0 + gx * step, y = y0 + gy * step;
                if (x < w && y < h) mask[gx, gy] = IsPlayRed(bmp.GetPixel(x, y));
            }

        var seen = new bool[gw, gh];
        Point? best = null; int bestCount = 0;
        var stack = new Stack<(int, int)>();
        for (int gx = 0; gx < gw; gx++)
            for (int gy = 0; gy < gh; gy++)
            {
                if (!mask[gx, gy] || seen[gx, gy]) continue;
                int count = 0, minX = gx, maxX = gx, minY = gy, maxY = gy;
                stack.Push((gx, gy)); seen[gx, gy] = true;
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    count++;
                    minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx); minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                    foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                        if (nx >= 0 && ny >= 0 && nx < gw && ny < gh && mask[nx, ny] && !seen[nx, ny]) { seen[nx, ny] = true; stack.Push((nx, ny)); }
                }
                int bw = (maxX - minX + 1) * step, bh = (maxY - minY + 1) * step;
                double fill = (double)count / ((maxX - minX + 1) * (maxY - minY + 1));
                if (bw >= 70 && bw <= 420 && bh >= 28 && bh <= 130 && fill >= 0.55 && count > bestCount)
                {
                    bestCount = count;
                    best = new Point(x0 + (minX + maxX) / 2 * step, y0 + (minY + maxY) / 2 * step);
                }
            }
        return best;
    }

    static IntPtr RiotWindow()
    {
        foreach (var p in Process.GetProcessesByName("Riot Client"))
        {
            try { if (p.MainWindowHandle != IntPtr.Zero && Native.IsWindowVisible(p.MainWindowHandle)) return p.MainWindowHandle; }
            catch { }
        }
        return IntPtr.Zero;
    }

    static Bitmap Capture(IntPtr h, out Native.RECT r)
    {
        GetWindowRect(h, out r);
        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        if (w < 200 || ht < 200) return null;
        var bmp = new Bitmap(w, ht);
        using var g = Graphics.FromImage(bmp);
        var hdc = g.GetHdc();
        try { PrintWindow(h, hdc, 2); } finally { g.ReleaseHdc(hdc); }
        return bmp;
    }

    public static bool GameRunning(string product) => product switch
    {
        "valorant" => Process.GetProcesses().Any(p => p.ProcessName.StartsWith("VALORANT", StringComparison.OrdinalIgnoreCase)),
        "league_of_legends" => Process.GetProcessesByName("League of Legends").Length > 0 || Process.GetProcessesByName("LeagueClient").Length > 0,
        "bacon" => Process.GetProcessesByName("LoR").Length > 0,
        _ => false
    };

    /// <summary>Waits for the Riot Client to show Play, clicks it, and reports what happened.</summary>
    public static async Task<string> ClickPlayWhenReady(string product, int timeoutSec = 45)
    {
        var sw = Stopwatch.StartNew();
        Point? last = null;
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            await Task.Delay(1000);
            if (GameRunning(product)) return null;
            var h = RiotWindow();
            if (h == IntPtr.Zero) continue;
            if (IsIconic(h)) ShowWindow(h, 9);
            using var bmp = Capture(h, out var r);
            if (bmp == null) continue;
            var p = FindPlay(bmp);
            if (p == null) { last = null; continue; }
            if (last == null || Math.Abs(last.Value.X - p.Value.X) > 6 || Math.Abs(last.Value.Y - p.Value.Y) > 6) { last = p; continue; }

            Native.ForceForeground(h);
            await Task.Delay(350);
            if (Native.GetForegroundWindow() != h) { last = null; continue; }
            Native.GetCursorPos(out var old);
            SetCursorPos(r.Left + p.Value.X, r.Top + p.Value.Y);
            await Task.Delay(60);
            mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(40);
            mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(120);
            SetCursorPos(old.X, old.Y);

            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(1000);
                if (GameRunning(product)) return null;
            }
            return "Pressed Play, but the game hasn't started yet. Check the Riot Client";
        }
        return "Riot Client didn't show a Play button. It may need a login or an update";
    }
}

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PCIsland.Core;

public record VcRequest(string Channel, string Server, bool Leave = false);

public static class DiscordVc
{
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }

    static INPUT K(ushort vk, bool up) => new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0 } } };
    static INPUT U(char c, bool up) => new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = 4u | (up ? 2u : 0) } } };
    static void Send(params INPUT[] i) => SendInput((uint)i.Length, i, Marshal.SizeOf<INPUT>());

    static IntPtr target;
    class Lost : Exception { }
    static void Check() { if (Native.GetForegroundWindow() != target) throw new Lost(); }
    static void Ctrl(ushort vk) { Check(); Send(K(0x11, false), K(vk, false), K(vk, true), K(0x11, true)); }
    static void Key(ushort vk) { Check(); Send(K(vk, false), K(vk, true)); }
    static void Type(string s) { Check(); Send(s.SelectMany(c => new[] { U(c, false), U(c, true) }).ToArray()); }

    static readonly Regex Join = new(
        @"\b(?:join|connect to|go to|hop in|get in|enter)\s+(?:the\s+)?(.+?)\s+(?:vc|voice chat|voice channel|voice|channel)(?:\s+(?:in|on|at|from)\s+(?:the\s+)?(.+?))?(?:\s+server)?$",
        RegexOptions.IgnoreCase);
    static readonly Regex JoinTr = new(
        @"^(?:discord(?:da|'da|u ac ve|'u ac ve)?\s+)?(?:(.+?)\s+(?:sunucusundaki|sunucusunda|serverindaki|serverinda|sunucudaki)\s+)?(.+?)\s+(?:ses kanalina|sesli kanala|sesliye|sese|vc ye|vcye|kanalina)\s+(?:gir|katil|baglan)$",
        RegexOptions.IgnoreCase);

    public static VcRequest Parse(string input)
    {
        var f = Text.Fold(input.Trim()).Replace("'", "").Replace("’", "");
        if (Regex.IsMatch(f, @"^(leave|disconnect from|exit|quit)\s+(the\s+)?(vc|voice chat|voice|voice channel)$|^(sesten|sesliden|ses kanalindan|vcden)\s+(cik|ayril)$"))
            return new VcRequest("", "", Leave: true);
        var m = Join.Match(f);
        if (m.Success)
        {
            var ch = Regex.Replace(m.Groups[1].Value, @"^(open discord and|discord and|discord)\s+", "").Trim();
            return new VcRequest(ch, m.Groups[2].Success ? m.Groups[2].Value.Trim() : "");
        }
        m = JoinTr.Match(f);
        if (m.Success) return new VcRequest(m.Groups[2].Value.Trim(), m.Groups[1].Success ? m.Groups[1].Value.Trim() : "");
        return null;
    }

    static IntPtr FindDiscordWindow()
    {
        var pids = Process.GetProcessesByName("Discord").Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count == 0) return IntPtr.Zero;
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (!pids.Contains(pid)) return true;
            var cls = new StringBuilder(64); Native.GetClassName(h, cls, 64);
            if (!cls.ToString().StartsWith("Chrome_WidgetWin")) return true;
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            var title = t.ToString();
            if (title.Length == 0 || title.Contains("Updater", StringComparison.OrdinalIgnoreCase)) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    static string Title(IntPtr h) { var t = new StringBuilder(256); GetWindowText(h, t, 256); return t.ToString(); }

    public static async Task<Reply> Run(VcRequest req)
    {
        if (req.Leave) return new Message("Discord has no shortcut for leaving. Click the red phone icon at the bottom left", false);
        var h = FindDiscordWindow();
        if (h == IntPtr.Zero)
        {
            var (app, s) = AppIndex.Find("discord");
            if (app == null || s < 90) return new Message("Discord isn't installed", false);
            AppIndex.Launch(app);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 40000)
            {
                await Task.Delay(500);
                h = FindDiscordWindow();
                if (h != IntPtr.Zero && Native.IsWindowVisible(h)) break;
            }
            if (h == IntPtr.Zero) return new Message("Discord took too long to open. Try again in a moment", false);
            await Task.Delay(4000);
        }

        target = h;
        try
        {
            if (IsIconic(h)) ShowWindow(h, 9);
            ShowWindow(h, 5);
            Native.ForceForeground(h);
            await Task.Delay(400);
            Send(K(0x11, true), K(0x10, true), K(0x12, true));
            await Task.Delay(80);

            if (req.Server.Length > 0)
            {
                Ctrl(0x4B);
                await Task.Delay(450);
                Type("*" + req.Server);
                await Task.Delay(900);
                Key(0x0D);
                await Task.Delay(1300);
            }
            Ctrl(0x4B);
            await Task.Delay(450);
            Type("!" + req.Channel);
            await Task.Delay(900);
            Key(0x0D);
            await Task.Delay(800);
            var where = req.Server.Length > 0 ? $"{req.Channel} in {req.Server}" : req.Channel;
            return new Message($"Joining {where}");
        }
        catch (Lost) { return new Message("Stopped: Discord lost focus", false); }
        finally { target = IntPtr.Zero; }
    }
}

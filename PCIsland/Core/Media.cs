using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace PCIsland.Core;

public record NowPlaying(string Title, string Artist, string App, bool Playing);

public static class Media
{
    static GlobalSystemMediaTransportControlsSessionManager manager;

    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    static async Task<GlobalSystemMediaTransportControlsSessionManager> Manager()
    {
        if (manager == null) manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return manager;
    }

    public static event Action Changed;
    static readonly System.Collections.Generic.HashSet<string> hooked = new();

    public static async Task Watch()
    {
        try
        {
            var m = await Manager();
            m.SessionsChanged += (_, _) => { HookSessions(m); Changed?.Invoke(); };
            m.CurrentSessionChanged += (_, _) => Changed?.Invoke();
            HookSessions(m);
        }
        catch { }
    }

    static void HookSessions(GlobalSystemMediaTransportControlsSessionManager m)
    {
        try
        {
            foreach (var s in m.GetSessions())
            {
                var id = s.SourceAppUserModelId + "#" + s.GetHashCode();
                lock (hooked) { if (!hooked.Add(id)) continue; }
                s.MediaPropertiesChanged += (_, _) => Changed?.Invoke();
                s.PlaybackInfoChanged += (_, _) => Changed?.Invoke();
            }
        }
        catch { }
    }

    static async Task<GlobalSystemMediaTransportControlsSession> Session()
    {
        try
        {
            var m = await Manager();
            var sessions = m.GetSessions();
            var playing = sessions.FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            return playing ?? m.GetCurrentSession() ?? sessions.FirstOrDefault();
        }
        catch { return null; }
    }

    static string AppName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var s = id.ToLowerInvariant();
        if (s.Contains("spotify")) return "Spotify";
        if (s.Contains("opera")) return "Opera";
        if (s.Contains("chrome")) return "Chrome";
        if (s.Contains("msedge")) return "Edge";
        if (s.Contains("firefox")) return "Firefox";
        if (s.Contains("zune") || s.Contains("media")) return "Media Player";
        var n = id.Split('!', '\\', '_')[0];
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }

    public static async Task<NowPlaying> Current()
    {
        try
        {
            var s = await Session();
            if (s == null) return null;
            var info = await s.TryGetMediaPropertiesAsync();
            var pb = s.GetPlaybackInfo();
            if (info == null || string.IsNullOrWhiteSpace(info.Title)) return null;
            bool playing = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return new NowPlaying(info.Title, info.Artist ?? "", AppName(s.SourceAppUserModelId), playing);
        }
        catch { return null; }
    }

    static void Key(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); }

    public static async Task<bool> PlayPause()
    {
        var s = await Session();
        if (s != null) try { return await s.TryTogglePlayPauseAsync(); } catch { }
        Key(0xB3); return true;
    }

    public static async Task<bool> Play()
    {
        var s = await Session();
        if (s != null) try { return await s.TryPlayAsync(); } catch { }
        Key(0xB3); return true;
    }

    public static async Task<bool> Pause()
    {
        var s = await Session();
        if (s != null) try { return await s.TryPauseAsync(); } catch { }
        Key(0xB3); return true;
    }

    public static async Task<bool> Next()
    {
        var s = await Session();
        if (s != null) try { return await s.TrySkipNextAsync(); } catch { }
        Key(0xB0); return true;
    }

    public static async Task<bool> Previous()
    {
        var s = await Session();
        if (s != null) try { return await s.TrySkipPreviousAsync(); } catch { }
        Key(0xB1); return true;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace PCIsland.Core;

public static partial class Brain
{
    static bool Is(string s, params string[] options) => options.Contains(s);

    static async Task<Reply> TryExtras(string input, bool insideShortcut)
    {
        var raw = input.Trim();
        var f = Text.Fold(raw).Replace("'", "").Replace("’", "");
        var c = Text.Clean(raw);
        var words = c.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;

        if (Is(c, "guide", "kilavuz", "kullanim kilavuzu", "how to use", "tutorial", "nasil kullanilir", "user guide", "the guide", "guide me", "manual"))
            return new OpenGuide();

        // ---------- feedback ----------
        if (Is(c, "feedback", "send feedback", "give feedback", "geri bildirim", "geri bildirim gonder", "geribildirim"))
        { Places.Open(Feedback.Url()); return new Message("Opening the feedback form"); }
        if (Is(c, "bug", "report bug", "report a bug", "bug report", "hata bildir", "hata", "sorun bildir"))
        { Places.Open(Feedback.Url(Feedback.Bug)); return new Message("Opening the bug report form"); }
        if (Is(c, "idea", "suggest", "suggestion", "suggest an idea", "send idea", "oneri", "oneri gonder", "fikir"))
        { Places.Open(Feedback.Url(Feedback.Idea)); return new Message("Opening the idea form"); }

        // ---------- discord voice ----------
        var vc = DiscordVc.Parse(raw);
        if (vc != null && (vc.Leave || vc.Channel.Length > 0))
        {
            Status?.Invoke("Opening Discord");
            var (dapp, ds) = AppIndex.Find("discord");
            var r = await DiscordVc.Run(vc);
            if (r is Message dm && dm.Ok && dapp != null && ds >= 90) LastIcon = dapp.IsShellApp ? "shell:AppsFolder\\" + dapp.Launch : dapp.Launch;
            return r;
        }

        // ---------- stream mode ----------
        if (Is(c, "stream mode", "yayin modu", "streamer mode", "stream mode on", "stream mode off", "yayin modu ac", "yayin modu kapat", "toggle stream mode"))
        {
            bool sm = c.EndsWith(" on") || c.EndsWith(" ac") ? true : c.EndsWith("off") || c.EndsWith("kapat") ? false : !Settings.Current.StreamMode;
            Settings.Current.StreamMode = sm;
            Settings.Save();
            StreamModeChanged?.Invoke();
            return new Message(sm ? "Stream mode on: the island shows in screen shares (no blur)" : "Stream mode off: blur is back, hidden from screen shares");
        }

        // ---------- translate ----------
        var tr = TranslateParser.Parse(raw);
        if (tr != null && tr.Text.Length > 0)
        {
            bool gameNow = Native.IsFullscreenAppRunning();
            if (Ai.Ready && !(gameNow && Settings.Current.AiGameMode == "off"))
            {
                bool gpu = AiModels.HasGraphicsCard && !(gameNow && Settings.Current.AiGameMode == "cpu");
                return new Deferred(async () =>
                {
                    Status?.Invoke("Translating");
                    var result = await Ai.Translate(tr.Text, tr.Language, gpu);
                    if (string.IsNullOrWhiteSpace(result)) return new Message("Couldn't translate that", false);
                    return new Answer($"{tr.Text} → {result}", result);
                });
            }
            Places.Open($"https://translate.google.com/?sl=auto&tl={tr.Code}&text={Uri.EscapeDataString(tr.Text)}&op=translate");
            return new Message(tr.Language == "auto" ? "Opening Google Translate" : $"Translating to {tr.Language} in Google Translate");
        }

        // ---------- calculator ----------
        if (Calc.LooksLikeMath(raw) && Calc.TryEval(raw, out var val))
        {
            var res = Calc.Format(val);
            return new Answer($"{raw.TrimEnd('=', ' ')} = {res}", res.Replace(" ", ""));
        }

        // ---------- media ----------
        if (Is(c, "pause", "durdur", "duraklat", "muzigi durdur", "sarkiyi durdur", "stop music", "pause music", "muzik durdur", "pause song"))
            return await MediaDo(Media.Pause, "Paused");
        if (Is(c, "play", "resume", "devam", "devam et", "oynat", "cal", "muzigi devam ettir", "play music", "resume music", "sarkiyi devam ettir"))
            return await MediaDo(Media.Play, "Playing");
        if (Is(c, "play pause", "toggle music", "playpause"))
            return await MediaDo(Media.PlayPause, null);
        if (Is(c, "next", "skip", "next song", "sonraki", "sonraki sarki", "gec", "sarkiyi gec", "atla", "skip song", "next track", "siradaki"))
            return await MediaDo(Media.Next, "Next");
        if (Is(c, "previous", "prev", "back", "previous song", "onceki", "onceki sarki", "geri", "last song", "previous track"))
            return await MediaDo(Media.Previous, "Previous");
        if (Is(c, "whats playing", "what s playing", "what is playing", "now playing", "ne caliyor", "hangi sarki", "music", "song", "sarki"))
            return new ShowMusic();

        // ---------- timer ----------
        bool timerWord = words.Any(w => w is "timer" or "sayac" or "sayaci" or "countdown" or "alarm");
        if (timerWord && words.Any(w => w is "stop" or "cancel" or "durdur" or "iptal" or "kapat" or "sil" or "off"))
        {
            if (Timers.TimerEnd == null) return new Message("No timer is running", false);
            Timers.TimerEnd = null;
            return new Message("Timer stopped");
        }
        var dur = Timers.ParseDuration(f);
        if (timerWord && dur != null) return Timers.StartTimer(dur.Value);
        if (timerWord && words.Length == 1)
            return Timers.TimerEnd == null ? new Message("No timer. Try \"timer 10 min\"", false) : new Message($"Timer: {Timers.Remaining()} left");

        // ---------- reminders ----------
        if (Is(c, "reminders", "my reminders", "hatirlaticilar", "hatirlaticilarim"))
        {
            if (Timers.Reminders.Count == 0) return new Message("No reminders");
            return new Pick("Reminders (Delete key removes)", Timers.Reminders.OrderBy(r => r.At).Take(6).Select(r =>
                new Choice(r.Text, r.At.ToString("ddd h:mm tt", CultureInfo.InvariantCulture), () => Task.FromResult<Reply>(new Silent()),
                    () => { Timers.Remove(r); return Task.FromResult<Reply>(new Message("Reminder removed")); })).ToList());
        }
        if (words.Any(w => w.StartsWith("remind") || w.StartsWith("hatirlat")))
        {
            DateTime? at = null;
            var d = Timers.ParseDuration(f);
            if (d != null && (f.Contains(" in ") || f.Contains("sonra") || f.Contains("icinde") || f.StartsWith("in ") || !Regex.IsMatch(f, @"\d[:.]\d\d|\b(am|pm)\b|\bat\s+\d|\bsaat\s+\d")))
                at = DateTime.Now + d.Value;
            at ??= Timers.ParseClock(f);
            if (at == null) return new Message("When? Try \"remind me at 9 pm to study\" or \"remind me in 20 min to eat\"", false);
            var text = Timers.ReminderText(raw);
            return Timers.AddReminder(at.Value, text);
        }

        // ---------- battery / ram / cpu ----------
        if (Is(c, "battery", "pil", "sarj", "batarya", "battery level", "pil durumu", "sarj durumu", "how much battery"))
        {
            var b = SystemTools.GetBattery();
            if (!b.HasBattery) return new Message("No battery found", false);
            string label = b.Charging ? "Charging" : b.MinutesLeft > 0 ? $"{b.MinutesLeft / 60}h {b.MinutesLeft % 60}m left" : "Battery";
            return new Level(label, b.Percent, "battery");
        }
        if (Is(c, "ram", "memory", "how much ram", "bellek", "ram usage", "ram kullanimi"))
        {
            var (p, used, total) = SystemTools.Ram();
            return new Level($"RAM {used:0.0}/{total:0} GB", p, "ram");
        }
        if (Is(c, "disk", "disk space", "storage", "depolama", "how much space", "free space", "bos alan", "disk alani"))
        {
            var drives = System.IO.DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed)
                .Select(d => $"{d.Name.TrimEnd('\\')} {d.AvailableFreeSpace / 1024.0 / 1024 / 1024:0} GB free of {d.TotalSize / 1024.0 / 1024 / 1024:0}");
            return new Message(string.Join(" · ", drives));
        }
        if (Is(c, "cpu", "islemci", "cpu usage", "processor", "islemci kullanimi"))
            return new Level("CPU", await SystemTools.Cpu(), "cpu");

        // ---------- wifi / bluetooth ----------
        bool wifi = words.Any(w => w is "wifi" or "wi" or "wlan" or "wifiyi" or "internet") && (c.Contains("wifi") || c.Contains("wi fi") || c.Contains("wlan"));
        bool bt = words.Any(w => w.StartsWith("bluetooth") || w is "bt");
        bool off = words.Any(w => w is "off" or "kapat" or "disable" or "kapa");
        bool on = words.Any(w => w is "on" or "ac" or "enable" or "turn");
        if ((wifi || bt) && (off || on) && !words.Any(w => w is "settings" or "ayarlari" or "ayarlar"))
        {
            bool turnOn = !off;
            var err = await SystemTools.SetRadio(wifi ? RadioKind.WiFi : RadioKind.Bluetooth, turnOn);
            string what = wifi ? "Wi-Fi" : "Bluetooth";
            return err == null ? new Message($"{what} {(turnOn ? "on" : "off")}") : new Message(err, false);
        }

        // ---------- dark mode ----------
        if (Is(c, "dark mode", "karanlik mod", "dark mode on", "karanlik modu ac", "dark theme", "koyu mod", "koyu tema"))
        { SystemTools.SetDarkMode(true); return new Message("Dark mode on"); }
        if (Is(c, "light mode", "aydinlik mod", "dark mode off", "karanlik modu kapat", "light theme", "acik mod", "acik tema"))
        { SystemTools.SetDarkMode(false); return new Message("Light mode on"); }
        if (Is(c, "toggle dark mode", "switch theme mode"))
        { bool d2 = !SystemTools.IsDarkMode(); SystemTools.SetDarkMode(d2); return new Message(d2 ? "Dark mode on" : "Light mode on"); }

        // ---------- screenshot ----------
        if (Is(c, "screenshot area", "ss area", "area screenshot", "snip", "ekran alintisi", "bolge ss", "alan ss", "ekran goruntusu alan", "screenshot region"))
            return new Shot(true);
        if (Is(c, "screenshot", "ss", "ss al", "ekran goruntusu", "ekran goruntusu al", "take a screenshot", "take screenshot", "print screen", "screen shot"))
            return new Shot(false);

        // ---------- notes ----------
        var noteMatch = Regex.Match(raw, @"^(?:note|not al|not:|note:|nota)\s*:?\s+(.+)$", RegexOptions.IgnoreCase);
        if (!noteMatch.Success) noteMatch = Regex.Match(raw, @"^(.+?)\s+(?:diye\s+)?(?:not al|not et)$", RegexOptions.IgnoreCase);
        if (noteMatch.Success && !Text.Fold(raw).StartsWith("not defteri"))
        {
            Notes.Add(noteMatch.Groups[1].Value);
            return new Message($"Noted: {noteMatch.Groups[1].Value.Trim()}");
        }
        if (Is(c, "notes", "notlar", "notlarim", "my notes", "show notes"))
        {
            if (Notes.All.Count == 0) return new Message("No notes yet. Try \"note buy milk\"");
            return new Pick("Notes (Enter copies, Delete removes)", Notes.All.AsEnumerable().Reverse().Take(6).Select(n =>
                new Choice(n.Text, n.At.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture),
                    () => Task.FromResult<Reply>(new Answer($"Copied: {n.Text}", n.Text) { }),
                    () => { Notes.Remove(n); return Task.FromResult<Reply>(new Message("Note removed")); })).ToList());
        }
        if (Is(c, "clear notes", "delete notes", "notlari sil", "tum notlari sil"))
            return new Confirm($"Delete all {Notes.All.Count} notes?", "This can't be undone.", "Delete", true, () =>
            {
                Notes.All.Clear(); Notes.Save();
                return Task.FromResult<Reply>(new Message("Notes cleared"));
            });

        // ---------- clipboard ----------
        if (Is(c, "clipboard", "pano", "clipboard history", "kopyalananlar", "panodakiler", "copied"))
        {
            if (ClipHistory.Items.Count == 0) return new Message("Nothing copied yet");
            return new Pick("Clipboard (Enter copies again)", ClipHistory.Items.Take(6).Select(t =>
            {
                var one = Regex.Replace(t, @"\s+", " ").Trim();
                return new Choice(one.Length > 60 ? one[..60] + "…" : one, $"{t.Length} characters", () => Task.FromResult<Reply>(new Answer("Copied", t)));
            }).ToList());
        }

        // ---------- windows ----------
        if (Is(c, "minimize all", "show desktop", "hepsini kucult", "masaustunu goster", "minimize everything", "hide all", "tumunu kucult"))
        { WinControl.MinimizeAll(); return new Message("Showing desktop"); }

        var sw = Regex.Match(c, @"^(?:switch to|go to|focus|show)\s+(.+)$");
        if (!sw.Success) sw = Regex.Match(c, @"^(.+?)\s+(?:e|a|ye|ya)?\s*gec$");
        if (sw.Success && !Is(c, "gec"))
        {
            var (w, s) = WinControl.Find(sw.Groups[1].Value);
            if (w != null && s >= 70) { WinControl.Focus(w.Handle); return new Message($"Switched to {w.Display}"); }
            if (c.StartsWith("switch to") || c.StartsWith("go to") || c.StartsWith("focus")) return new Message($"No {sw.Groups[1].Value} window open", false);
        }

        var snap = Regex.Match(c, @"^(?:snap|put|move)?\s*(.+?)\s+(?:to the |on the |to |on )?(left|right|sol|sola|sag|saga|sol tarafa|sag tarafa|solda|sagda)(?: side| tarafa)?$");
        if (snap.Success)
        {
            var (w, s) = WinControl.Find(snap.Groups[1].Value);
            if (w != null && s >= 75)
            {
                bool left = snap.Groups[2].Value.StartsWith("l") || snap.Groups[2].Value.StartsWith("sol");
                await WinControl.Snap(w.Handle, left);
                return new Message($"{w.Display} on the {(left ? "left" : "right")}");
            }
        }

        var mm = Regex.Match(c, @"^(maximize|minimize|buyut|kucult)\s+(.+)$|^(.+?)\s+(maximize|minimize|buyut|kucult)$");
        if (mm.Success)
        {
            string verb = mm.Groups[1].Success ? mm.Groups[1].Value : mm.Groups[4].Value;
            string target = mm.Groups[1].Success ? mm.Groups[2].Value : mm.Groups[3].Value;
            target = Text.SuffixVariants(target).Last();
            var (w, s) = WinControl.Find(target);
            if (w != null && s >= 70)
            {
                if (verb is "maximize" or "buyut") { WinControl.Maximize(w.Handle); return new Message($"Maximized {w.Display}"); }
                WinControl.Minimize(w.Handle); return new Message($"Minimized {w.Display}");
            }
        }

        // ---------- games ----------
        var playGame = Regex.Match(c, @"^(?:play|oyna|launch roblox|roblox)\s+(.+)$|^(.+?)\s+(?:oyna|roblox|robloxta oyna|on roblox)$");
        if (playGame.Success && !Is(c, "play music", "play pause"))
        {
            var name = (playGame.Groups[1].Success ? playGame.Groups[1].Value : playGame.Groups[2].Value).Trim();
            name = Regex.Replace(name, @"\b(on roblox|roblox|in roblox|robloxta|oyununu|oyunu)\b", "").Trim();
            if (name.Length > 0)
            {
                var (g, _) = Games.Find(name);
                if (g != null) return LaunchGame(g);
                Status?.Invoke("Finding the game");
                var rg = await Roblox.Search(name);
                if (rg != null && Roblox.Matches(name, rg.Name)) return LaunchGame(Roblox.Remember(rg, name));
                if (rg != null)
                    return new Confirm($"Did you mean {rg.Name}?", $"Roblox game · {rg.Players:N0} playing now", "Start", false,
                        () => Task.FromResult(LaunchGame(Roblox.Remember(rg, name))));
                return new Message($"Couldn't find a Roblox game called \"{name}\"", false);
            }
        }
        if (Is(c, "boost", "game boost", "oyun modu", "boost mode", "performance mode", "performans modu", "game mode"))
        {
            var targets = Games.BoostTargets();
            int boosted = Games.BoostRunningGames();
            string pr = boosted > 0 ? " Roblox set to high priority." : "";
            if (targets.Count == 0) return new Message("Nothing to close." + pr);
            var names = string.Join(", ", targets.Select(t => t.Display).Take(5)) + (targets.Count > 5 ? "..." : "");
            return new Confirm($"Close {targets.Count} background app{(targets.Count == 1 ? "" : "s")}?", names, "Boost", false, async () =>
            {
                int ok = 0;
                foreach (var t in targets) if (await Closer.Close(t) == null) ok++;
                Games.BoostRunningGames();
                return new Message($"Boost done: closed {ok}.{pr}");
            });
        }

        return null;
    }

    static async Task<Reply> MediaDo(Func<Task<bool>> act, string label)
    {
        await act();
        await Task.Delay(450);
        var np = await Media.Current();
        if (np == null) return new Message(label ?? "Done");
        string state = label ?? (np.Playing ? "Playing" : "Paused");
        return new Message($"{state} · {np.Title}{(np.Artist.Length > 0 ? " – " + np.Artist : "")}");
    }
}

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PCIsland.Core;

namespace PCIsland;

public partial class MainWindow
{
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr h);

    readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    NowPlaying music;
    DateTime musicCheckedAt = DateTime.MinValue;
    bool pendingAlert;
    string copyText;
    int lastBatteryPercent = -1;
    bool? lastCharging;
    bool warned20, warned10;
    int tick;
    System.Media.SoundPlayer alarm;
    string liveKey = "";

    public void Notify(string text, bool ok)
    {
        if (state is State.Idle or State.Hidden or State.Msg) ShowMsg(text, ok);
    }

    void InitLive()
    {
        Brain.StreamModeChanged += () => Dispatcher.BeginInvoke(RefreshStreamMode);
        Brain.Status += s => Dispatcher.BeginInvoke(() => { if (state == State.Busy) ShowBusy(s == "Thinking" ? "Thinking with AI" : s == "Translating" ? "Translating with AI" : s); });
        Timers.Load();
        Notes.Load();
        Timers.Fired += (title, text) => Dispatcher.BeginInvoke(() => ShowAlert(title, text));
        liveTimer.Tick += async (_, _) => await LiveTick();
        Media.Changed += () => Dispatcher.BeginInvoke(async () =>
        {
            if (!Settings.Current.ShowNowPlaying) return;
            await Task.Delay(120);
            await RefreshMusic();
            if (state == State.Idle) ShowIdleContent(force: false);
            if (state == State.Music) RenderMusic();
        });
        _ = Media.Watch();
        Loaded += (_, _) => liveTimer.Start();

        PrevBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = MusicAction(Media.Previous); };
        PlayBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = MusicAction(Media.PlayPause); };
        NextBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = MusicAction(Media.Next); };
        foreach (var b in new[] { PrevBtn, NextBtn })
        {
            var bb = b;
            bb.MouseEnter += (_, _) => bb.Background = Brush(theme.Soft);
            bb.MouseLeave += (_, _) => bb.Background = Brushes.Transparent;
        }

    }

    bool barsRunning;

    void SetBars(bool run)
    {
        if (run == barsRunning) return;
        barsRunning = run;
        var bars = new[] { Bar1, Bar2, Bar3 };
        var rnd = new[] { 0.42, 0.55, 0.36 };
        for (int i = 0; i < bars.Length; i++)
        {
            if (!run) { bars[i].BeginAnimation(HeightProperty, null); continue; }
            var anim = new DoubleAnimation(3, 12, TimeSpan.FromSeconds(rnd[i])) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            Timeline.SetDesiredFrameRate(anim, 30);
            bars[i].BeginAnimation(HeightProperty, anim);
        }
    }

    // ---------- periodic ----------

    async Task LiveTick()
    {
        tick++;
        Timers.Check();
        if (tick % 5 == 0) await Task.Run(() => { try { Games.BoostRunningGames(); } catch { } });
        if (tick % 10 == 0) CheckBattery();
        if (Settings.Current.ShowNowPlaying && tick % 5 == 0) await RefreshMusic();
        if (!Settings.Current.ShowNowPlaying) music = null;
        if (state == State.Idle) ShowIdleContent(force: false);
        if (state == State.Music) RenderMusic();
    }

    async Task<bool> RefreshMusic()
    {
        try { music = await Media.Current(); } catch { music = null; }
        musicCheckedAt = DateTime.Now;
        return music != null;
    }

    bool MusicLive() => Settings.Current.ShowNowPlaying && music != null && music.Playing;

    // ---------- idle pill ----------

    void ShowIdleContent(bool force)
    {
        if (state != State.Idle && state != State.Hidden) return;
        string key;
        if (Timers.TimerEnd != null) key = "timer";
        else if (MusicLive()) key = "music:" + music.Title;
        else key = "dot";

        if (key == "timer")
        {
            LiveIcon.Text = "\uE916";
            LiveIcon.Visibility = Visibility.Visible;
            Bars.Visibility = Visibility.Collapsed;
            LiveIcon.Foreground = Brush(theme.Acc);
            LiveText.Foreground = Brush(theme.Fg);
            LiveText.Text = Timers.Remaining();
            LiveText.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            LiveText.FontWeight = FontWeights.SemiBold;
        }
        else if (key.StartsWith("music"))
        {
            LiveIcon.Visibility = Visibility.Collapsed;
            Bars.Visibility = Visibility.Visible;
            Bar1.Fill = Bar2.Fill = Bar3.Fill = Brush(theme.Acc);
            LiveText.Foreground = Brush(theme.Fg);
            LiveText.FontWeight = FontWeights.Normal;
            LiveText.Inlines.Clear();
            LiveText.Inlines.Add(new System.Windows.Documents.Run(music.Title));
            if (!string.IsNullOrWhiteSpace(music.Artist))
                LiveText.Inlines.Add(new System.Windows.Documents.Run("  ·  " + music.Artist) { Foreground = Brush(theme.Dim) });
            key += "|" + music.Artist;
        }

        SetBars(key.StartsWith("music") && state == State.Idle);
        if (!force && key == liveKey) return;
        liveKey = key;
        if (key == "dot") { Resize(126, 34); Show(IdleDot); }
        else if (key == "timer") { Resize(150, 34); Show(LivePanel); }
        else { Resize(MusicPillWidth(), 34); Show(LivePanel); }
    }

    double MusicPillWidth()
    {
        var text = music.Title + (string.IsNullOrWhiteSpace(music.Artist) ? "" : "  ·  " + music.Artist);
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(LiveText.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), LiveText.FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return Math.Clamp(ft.Width + 32 + 20 + 6, 180, 380);
    }

    // ---------- music card ----------

    void RenderMusic()
    {
        if (music == null) return;
        MusicTitle.Text = music.Title;
        MusicArtist.Text = string.IsNullOrEmpty(music.Artist) ? music.App : $"{music.Artist} · {music.App}";
        MusicTitle.Foreground = PrevIcon.Foreground = NextIcon.Foreground = Brush(theme.Fg);
        MusicArtist.Foreground = Brush(theme.Dim);
        PlayBtn.Background = Brush(theme.Acc);
        PlayIcon.Foreground = IsLight(theme.Acc) ? Brushes.Black : Brushes.White;
        PlayIcon.Text = music.Playing ? "\uE769" : "\uE768";
    }

    void ShowMusicCard()
    {
        state = State.Music;
        backTimer.Stop();
        farSince = DateTime.MaxValue;
        RenderMusic();
        Resize(380, 64);
        Show(MusicPanel);
        TakeFocus();
    }

    async Task MusicAction(Func<Task<bool>> act)
    {
        try { await act(); } catch { }
        await Task.Delay(400);
        await RefreshMusic();
        if (state == State.Music) RenderMusic();
    }

    // ---------- alerts (timer + reminders) ----------

    void ShowAlert(string title, string text)
    {
        if (state == State.Hidden) IslandVisible(true);
        state = State.Alert;
        pendingAlert = true;
        backTimer.Stop();
        ConfTitle.Text = title;
        ConfSub.Text = text;
        NoBtn.Visibility = Visibility.Collapsed;
        YesText.Text = "OK";
        YesBtn.Background = Brush(theme.Acc);
        YesText.Foreground = IsLight(theme.Acc) ? Brushes.Black : Brushes.White;
        double w = Math.Clamp(60 + Math.Max(title.Length * 8.4, text.Length * 6.6), 300, 460);
        Resize(w, 118);
        Show(ConfirmPanel);
        if (!fullscreen) TakeFocus();
        try
        {
            alarm ??= new System.Media.SoundPlayer(@"C:\Windows\Media\Alarm01.wav");
            alarm.PlayLooping();
        }
        catch { System.Media.SystemSounds.Exclamation.Play(); }
        var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        stop.Tick += (_, _) => { stop.Stop(); try { alarm?.Stop(); } catch { } };
        stop.Start();
    }

    void DismissAlert()
    {
        try { alarm?.Stop(); } catch { }
        NoBtn.Visibility = Visibility.Visible;
        GoIdle();
        RestoreFocus();
    }

    // ---------- answers / copy ----------

    void ShowAnswer(Answer a)
    {
        ShowMsg(a.Text + "   (Enter copies)", true);
        copyText = a.Copy;
        backTimer.Stop();
        backTimer.Interval = TimeSpan.FromSeconds(Math.Max(8, a.Text.Length * 0.07));
        backTimer.Start();
        if (a.Text.StartsWith("Copied")) CopyAnswer();
    }

    void CopyAnswer()
    {
        if (copyText == null) return;
        try { Clipboard.SetText(copyText); } catch { }
        var shown = copyText.Length > 40 ? copyText[..40] + "…" : copyText;
        copyText = null;
        ShowMsg($"Copied {shown}", true);
    }

    // ---------- screenshot ----------

    async Task TakeShot(bool area)
    {
        if (area)
        {
            GoIdle();
            try { Process("ms-screenclip:"); } catch { }
            return;
        }
        GoIdle();
        Opacity = 0;
        await Task.Delay(450);
        string path = null, err = null;
        try { path = await Task.Run(SystemTools.Screenshot); } catch (Exception ex) { err = ex.Message; }
        Opacity = 1;
        ShowMsg(err == null ? $"Saved {System.IO.Path.GetFileName(path)} to Desktop" : "Screenshot failed: " + err, err == null);
    }

    static void Process(string target) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });

    // ---------- pick delete ----------

    async Task DeleteChoice(int i)
    {
        var ch = pendingChoices[i];
        try { await ch.Delete(); } catch { }
        pendingChoices.RemoveAt(i);
        if (pendingChoices.Count == 0) { ShowMsg("All gone", true); return; }
        pickIndex = Math.Min(pickIndex, pendingChoices.Count - 1);
        RenderPick();
        Resize(440, 58 + 46 * pendingChoices.Count);
    }

    // ---------- battery ----------

    void CheckBattery()
    {
        if (!Settings.Current.BatteryAlerts) return;
        SystemTools.Battery b;
        try { b = SystemTools.GetBattery(); } catch { return; }
        if (!b.HasBattery) return;
        bool first = lastCharging == null;
        if (!first && b.Charging != lastCharging && state == State.Idle)
            ShowLevel(new Level(b.Charging ? "Charging" : "On battery", b.Percent, "battery"), focus: false);
        if (b.Charging) { warned20 = warned10 = false; }
        else
        {
            if (b.Percent <= 10 && !warned10) { warned10 = warned20 = true; if (state is State.Idle or State.Hidden) ShowMsg($"Battery at {b.Percent}%. Plug in soon", false); }
            else if (b.Percent <= 20 && !warned20) { warned20 = true; if (state is State.Idle or State.Hidden) ShowMsg($"Battery at {b.Percent}%", false); }
        }
        lastCharging = b.Charging;
        lastBatteryPercent = b.Percent;
    }

    // ---------- clipboard ----------

    void OnClipboardChanged()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (Clipboard.ContainsText()) ClipHistory.Add(Clipboard.GetText());
                    return;
                }
                catch { await Task.Delay(80); }
            }
        });
    }
}

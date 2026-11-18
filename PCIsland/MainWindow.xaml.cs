using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PCIsland.Core;

namespace PCIsland;

public partial class MainWindow : Window
{
    enum State { Idle, Type, Busy, Msg, Confirm, Pick, Hidden, Level, Music, Alert }

    State state = State.Idle;
    Theme theme;
    readonly DispatcherTimer backTimer = new();
    readonly DispatcherTimer nearTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer fullTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    bool openedByHover, hoverArmed = true, fullscreen;
    IntPtr previousWindow;
    DateTime nearSince = DateTime.MaxValue, farSince = DateTime.MaxValue;
    const double EnterDistance = 45, LeaveDistance = 90;
    const int HOTKEY_ID = 9001;
    IntPtr hwnd;
    Confirm pendingConfirm;
    List<Choice> pendingChoices = new();
    int pickIndex;
    static List<string> history => Predictor.History;
    int historyPos = -1;

    public event Action SettingsRequested;
    public event Action GuideRequested;

    public void FillInput(string text)
    {
        previousWindow = IntPtr.Zero;
        openedByHover = false;
        OpenInput();
        Input.Text = text;
        Input.CaretIndex = text.Length;
    }

    public MainWindow()
    {
        InitializeComponent();
        PlaceOnScreen();
        SystemParameters.StaticPropertyChanged += (_, e) => { if (e.PropertyName == "WorkArea") PlaceOnScreen(); };
        Island.SizeChanged += (_, _) => SyncShape();
        Island.MouseWheel += (_, e) => { if (state == State.Level) { _ = NudgeLevel(e.Delta > 0 ? 5 : -5); e.Handled = true; } };
        Input.TextChanged += (_, _) => { Hint.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; UpdateGhost(); };
        Input.SelectionChanged += (_, _) => { if (Input.CaretIndex != Input.Text.Length) ClearGhost(); };
        backTimer.Tick += (_, _) => { backTimer.Stop(); GoIdle(); };
        nearTimer.Tick += (_, _) => CheckNear();
        fullTimer.Tick += (_, _) => CheckFullscreen();
        BuildMenu();
        ApplyTheme(Themes.ByName(Settings.Current.Theme), save: false);
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            Native.SetWindowLong(hwnd, GWL_EXSTYLE, Native.GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
            _ = WS_EX_NOACTIVATE;
            RegisterHotkey();
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            SetGlass(theme.Glass);
            AddClipboardFormatListener(hwnd);
        };
        InitLive();
        Loaded += (_, _) => { nearTimer.Start(); fullTimer.Start(); Dispatcher.BeginInvoke(UpdateRegion, DispatcherPriority.Loaded); };
        Closed += (_, _) => Native.UnregisterHotKey(hwnd, HOTKEY_ID);
        Deactivated += (_, _) => { if (state == State.Type && Input.Text.Length == 0) GoIdle(); };
    }

    void PlaceOnScreen()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = 0;
    }

    public void PauseHotkey() { if (hwnd != IntPtr.Zero) Native.UnregisterHotKey(hwnd, HOTKEY_ID); }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    bool glass;

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    readonly DispatcherTimer glassTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    DateTime lastGlass = DateTime.MinValue;

    public void RefreshStreamMode() => SetGlass(theme.Glass);

    void SetGlass(bool on)
    {
        if (Settings.Current.StreamMode && on)
        {
            glass = false;
            if (hwnd == IntPtr.Zero) return;
            SetWindowDisplayAffinity(hwnd, 0u);
            glassTimer.Stop();
            Tint.Background = null;
            var c = (Color)ColorConverter.ConvertFromString(theme.Bg);
            byte a = theme.Name == "Frosted Glass" ? (byte)0xC8 : (byte)Math.Max((int)c.A, 0xD8);
            Island.Background = new SolidColorBrush(Color.FromArgb(a, theme.Name == "Frosted Glass" ? (byte)0x2A : c.R, theme.Name == "Frosted Glass" ? (byte)0x2A : c.G, theme.Name == "Frosted Glass" ? (byte)0x32 : c.B));
            Shadow.Visibility = Island.Visibility;
            return;
        }
        glass = on;
        if (hwnd == IntPtr.Zero) return;
        Shadow.Visibility = Island.Visibility != Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        SetWindowDisplayAffinity(hwnd, on ? 0x11u : 0u);
        if (on)
        {
            Tint.Background = Brush(theme.Bg);
            if (glassTimer.Tag == null) { glassTimer.Tick += (_, _) => RefreshGlass(false); glassTimer.Tag = true; }
            glassTimer.Start();
            RefreshGlass(true);
        }
        else
        {
            glassTimer.Stop();
            Tint.Background = null;
            Island.Background = Brush(theme.Bg);
        }
    }

    void UpdateRegion() { if (glass) RefreshGlass(true); }

    void RefreshGlass(bool force)
    {
        if (!glass || hwnd == IntPtr.Zero || !IsLoaded || Island.Visibility != Visibility.Visible) return;
        bool busy = state != State.Idle;
        if (!force && !busy && (DateTime.Now - lastGlass).TotalMilliseconds < 700) return;
        lastGlass = DateTime.Now;
        try
        {
            var src = PresentationSource.FromVisual(this);
            if (src == null) return;
            double s = src.CompositionTarget.TransformToDevice.M11;
            var p = Island.PointToScreen(new Point(0, 0));
            int w = Math.Max(8, (int)Math.Round(Island.ActualWidth * s)), h = Math.Max(8, (int)Math.Round(Island.ActualHeight * s));
            using var shot = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(shot))
                g.CopyFromScreen((int)Math.Round(p.X), (int)Math.Round(p.Y), 0, 0, new System.Drawing.Size(w, h));
            int sw = Math.Max(2, w / 14), sh = Math.Max(2, h / 14);
            using var small = new System.Drawing.Bitmap(sw, sh);
            using (var g = System.Drawing.Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(shot, 0, 0, sw, sh);
            }
            var hb = small.GetHbitmap();
            try
            {
                var bs = Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                var brush = new ImageBrush(bs) { Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                brush.Freeze();
                Island.Background = brush;
            }
            finally { DeleteObject(hb); }
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

    public bool RegisterHotkey()
    {
        if (hwnd == IntPtr.Zero) return false;
        Native.UnregisterHotKey(hwnd, HOTKEY_ID);
        return Native.RegisterHotKey(hwnd, HOTKEY_ID, Settings.Current.HotkeyMods | 0x4000, Settings.Current.HotkeyKey);
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x0312 && w.ToInt32() == HOTKEY_ID)
        {
            if (state == State.Type) { GoIdle(); RestoreFocus(); }
            else { previousWindow = Native.GetForegroundWindow(); openedByHover = false; OpenInput(); }
            handled = true;
        }
        if (msg == 0x031D) OnClipboardChanged();
        return IntPtr.Zero;
    }

    public void Summon()
    {
        previousWindow = Native.GetForegroundWindow();
        openedByHover = false;
        OpenInput();
    }

    void RestoreFocus()
    {
        if (previousWindow != IntPtr.Zero && previousWindow != hwnd) Native.SetForegroundWindow(previousWindow);
    }

    // ---------- hover + fullscreen ----------

    double DistanceToIsland()
    {
        if (!IsVisible || PresentationSource.FromVisual(this) == null) return double.MaxValue;
        Native.GetCursorPos(out var p);
        var a = Island.PointToScreen(new Point(0, 0));
        var b = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
        double dx = Math.Max(Math.Max(a.X - p.X, 0), p.X - b.X);
        double dy = Math.Max(Math.Max(a.Y - p.Y, 0), p.Y - b.Y);
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1;
        return Math.Sqrt(dx * dx + dy * dy) / scale;
    }

    void CheckNear()
    {
        if (!Settings.Current.HoverOpen || fullscreen || state == State.Hidden) { SetPoll(400); return; }
        var d = DistanceToIsland();
        SetPoll(d > 250 ? 200 : 40);
        var now = DateTime.Now;
        if (state == State.Idle)
        {
            if (d > EnterDistance) hoverArmed = true;
            if (d <= EnterDistance && hoverArmed)
            {
                if (nearSince == DateTime.MaxValue) nearSince = now;
                if ((now - nearSince).TotalMilliseconds >= 120)
                {
                    previousWindow = Native.GetForegroundWindow();
                    openedByHover = true;
                    if (MusicLive()) ShowMusicCard(); else OpenInput();
                }
            }
            else nearSince = DateTime.MaxValue;
        }
        else if (state == State.Music && openedByHover)
        {
            if (d > LeaveDistance)
            {
                if (farSince == DateTime.MaxValue) farSince = now;
                if ((now - farSince).TotalMilliseconds >= 300) { GoIdle(); RestoreFocus(); }
            }
            else farSince = DateTime.MaxValue;
        }
        else if (state == State.Type && openedByHover && Input.Text.Length == 0)
        {
            if (d > LeaveDistance)
            {
                if (farSince == DateTime.MaxValue) farSince = now;
                if ((now - farSince).TotalMilliseconds >= 300) { GoIdle(); RestoreFocus(); }
            }
            else farSince = DateTime.MaxValue;
        }
    }

    void SetPoll(int ms)
    {
        if ((int)nearTimer.Interval.TotalMilliseconds != ms) nearTimer.Interval = TimeSpan.FromMilliseconds(ms);
    }

    void CheckFullscreen()
    {
        bool fs = Native.IsFullscreenAppRunning();
        if (fs == fullscreen) return;
        fullscreen = fs;
        if (fullscreen && Settings.Current.HideInFullscreen && state == State.Idle) { state = State.Hidden; IslandVisible(false); }
        if (!fullscreen && state == State.Hidden) { IslandVisible(true); state = State.Idle; }
    }

    void IslandVisible(bool on)
    {
        Island.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Shadow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        UpdateRegion();
    }

    // ---------- theme ----------

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public Theme CurrentTheme => theme;

    public void ApplyTheme(Theme t, bool save = true)
    {
        theme = t;
        Island.Background = Brush(t.Bg);
        Island.BorderBrush = Brush(t.Edge);
        IdleDot.Fill = TypeDot.Fill = Brush(t.Acc);
        Input.Foreground = MsgText.Foreground = ConfTitle.Foreground = NoText.Foreground = PickTitle.Foreground = Brush(t.Fg);
        Input.CaretBrush = Brush(t.Acc);
        Input.SelectionBrush = Brush(t.Acc);
        Hint.Foreground = ConfSub.Foreground = Brush(t.Dim);
        NoBtn.Background = Brush(t.Soft);
        SetGlass(t.Glass);
        ApplyLook(t);
        ThemeApplied?.Invoke(t);
        if (state == State.Pick) RenderPick();
        if (save) { Settings.Current.Theme = t.Name; Settings.Save(); BuildMenu(); }
    }

    public event Action<Theme> ThemeApplied;

    void BuildMenu()
    {
        Menu.Items.Clear();
        foreach (var t in Themes.All)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = Brush(t.Acc), Stroke = Brush(t.Bg.Replace("#66", "#FF")), StrokeThickness = 3 };
            var item = new MenuItem { Header = t.Name, Icon = dot, IsCheckable = true, IsChecked = t.Name == Settings.Current.Theme };
            item.Click += (_, _) => ApplyTheme(t);
            Menu.Items.Add(item);
        }
        Menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        Menu.Items.Add(settings);
        var guide = new MenuItem { Header = "Guide" };
        guide.Click += (_, _) => GuideRequested?.Invoke();
        Menu.Items.Add(guide);
        var quit = new MenuItem { Header = "Quit PC Island" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        Menu.Items.Add(quit);
    }

    public void ShowMenu()
    {
        Menu.PlacementTarget = Island;
        Menu.IsOpen = true;
    }

    // ---------- shape + animation ----------

    void SyncShape()
    {
        Shadow.Width = Island.ActualWidth;
        Shadow.Height = Island.ActualHeight;
        var r = new CornerRadius(Math.Min(Island.ActualHeight / 2, 26));
        Island.CornerRadius = r;
        Shadow.CornerRadius = r;
        Tint.CornerRadius = new CornerRadius(Math.Max(0, r.TopLeft - 1));
        SyncLook();
        UpdateRegion();
    }

    void Resize(double w, double h)
    {
        var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(420);
        Island.BeginAnimation(WidthProperty, new DoubleAnimation(w, d) { EasingFunction = ease });
        Island.BeginAnimation(HeightProperty, new DoubleAnimation(h, d) { EasingFunction = ease });
    }

    void Show(UIElement panel)
    {
        if (panel != LivePanel) SetBars(false);
        SetShimmer(false);
        if (panel == IdleDot) StartBreathing(); else IdleDot.BeginAnimation(OpacityProperty, null);
        if (panel != IdleDot) MorphIn();
        foreach (var p in new UIElement[] { IdleDot, TypePanel, MsgPanel, ConfirmPanel, PickPanel, LevelPanel, LivePanel, MusicPanel })
            p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
        Body.Opacity = 0;
        Body.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(110) });
    }

    void TakeFocus()
    {
        Native.ForceForeground(hwnd);
        Activate();
        Input.Focus();
        Keyboard.Focus(Input);
    }

    // ---------- states ----------

    void GoIdle()
    {
        if (fullscreen && Settings.Current.HideInFullscreen)
        {
            state = State.Hidden;
            IslandVisible(false);
        }
        else state = State.Idle;
        openedByHover = false;
        hoverArmed = false;
        nearSince = DateTime.MaxValue;
        backTimer.Stop();
        pendingConfirm = null;
        Input.Text = "";
        historyPos = -1;
        pendingAlert = false;
        ShowIdleContent(force: true);
    }

    void OpenInput()
    {
        SetBars(false);
        IslandVisible(true);
        state = State.Type;
        if (previousWindow != hwnd) Tabs.LastForeground = previousWindow;
        backTimer.Stop();
        farSince = DateTime.MaxValue;
        Resize(400, 50);
        Show(TypePanel);
        ClearGhost();
        TakeFocus();
        System.Threading.Tasks.Task.Run(() =>
        {
            try { Predictor.RunningNames = Closer.List().Select(a => a.Display).ToList(); } catch { }
        });
    }

    void ShowMsg(string text, bool ok)
    {
        state = State.Msg;
        copyText = null;
        SetMessageText(text);
        SetMessageIcon(ok);
        var col = ok ? Brush(theme.Acc) : Brush(theme.Danger);
        MsgDot.Fill = col;
        MsgMark.Text = ok ? "✓" : "!";
        MsgMark.Foreground = col;
        var (w, h) = MessageSize(text);
        Resize(w, h);
        Show(MsgPanel);
        backTimer.Interval = TimeSpan.FromSeconds((ok ? 1.6 : 3.2) + Math.Max(0, text.Length - 40) * 0.05);
        backTimer.Start();
    }

    (double w, double h) MessageSize(string text)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(MsgText.FontFamily, MsgText.FontStyle, MsgText.FontWeight, MsgText.FontStretch), MsgText.FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        const double chrome = 32 + 18 + 26 + 4;
        double w = Math.Clamp(ft.Width + chrome, 260, 460);
        ft.MaxTextWidth = w - chrome;
        ft.LineHeight = 21;
        double h = Math.Max(50, ft.Height + 26);
        return (w, h);
    }

    Level currentLevel;

    void ShowLevel(Level lv, bool focus = true)
    {
        bool wasLevel = state == State.Level;
        state = State.Level;
        currentLevel = lv;
        LevelIcon.Text = lv.Kind switch
        {
            "brightness" => "",
            "battery" => lv.Label == "Charging" ? "" : "",
            "ram" => "",
            "cpu" => "",
            _ => lv.Label == "Muted" || lv.Value == 0 ? "" : lv.Value < 34 ? "" : lv.Value < 67 ? "" : ""
        };
        LevelIcon.Foreground = LevelLabel.Foreground = LevelValue.Foreground = Brush(theme.Fg);
        LevelLabel.Text = lv.Label;
        LevelValue.Text = lv.Label == "Muted" ? "" : $"{lv.Value}%";
        LevelTrack.Background = Brush(theme.Soft);
        LevelFill.Background = Brush(theme.Acc);
        if (!wasLevel)
        {
            Resize(lv.Label.Length > 10 ? 400 : 340, 50);
            Show(LevelPanel);
            if (focus) TakeFocus();
        }
        Dispatcher.BeginInvoke(() =>
        {
            var full = ((FrameworkElement)LevelTrack.Parent).ActualWidth;
            if (full <= 0) full = 160;
            LevelFill.BeginAnimation(WidthProperty, new DoubleAnimation(full * lv.Value / 100.0, TimeSpan.FromMilliseconds(wasLevel ? 120 : 380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }, DispatcherPriority.Loaded);
        backTimer.Stop();
        backTimer.Interval = TimeSpan.FromSeconds(2.2);
        backTimer.Start();
    }

    async Task NudgeLevel(int delta)
    {
        if (currentLevel == null || currentLevel.Kind is not ("volume" or "brightness")) return;
        var cmd = currentLevel.Kind == "brightness" ? $"brightness {Math.Clamp(currentLevel.Value + delta, 0, 100)}" : $"volume {Math.Clamp(currentLevel.Value + delta, 0, 100)}";
        var r = await Brain.Handle(cmd);
        if (r is Level lv) ShowLevel(lv);
    }

    void ShowBusy(string text)
    {
        state = State.Busy;
        MsgText.Text = text;
        MsgDot.Fill = Brush(theme.Dim);
        MsgMark.Text = "…";
        MsgMark.Foreground = Brush(theme.Dim);
        MsgIcon.Visibility = Visibility.Collapsed;
        MsgDot.Visibility = Visibility.Visible;
        Resize(MessageSize(text).w, 50);
        if (MsgPanel.Visibility != Visibility.Visible) Show(MsgPanel);
        SetShimmer(true);
    }

    void ShowConfirm(Confirm c)
    {
        state = State.Confirm;
        pendingConfirm = c;
        ConfTitle.Text = c.Title;
        ConfSub.Text = c.Sub;
        NoText.Text = "Cancel  (Esc)";
        YesText.Text = c.Danger ? $"{c.YesLabel}  (Y)" : $"{c.YesLabel}  (Enter)";
        YesBtn.Background = c.Danger ? Brush(theme.Danger) : Brush(theme.Acc);
        YesText.Foreground = c.Danger || !IsLight(theme.Acc) ? Brushes.White : Brushes.Black;
        double w = Math.Clamp(60 + Math.Max(c.Title.Length * 8.4, c.Sub.Length * 6.6), 340, 460);
        Resize(w, 118);
        Show(ConfirmPanel);
        TakeFocus();
    }

    static bool IsLight(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 170;
    }

    void ShowPick(Pick p)
    {
        state = State.Pick;
        pendingChoices = p.Choices;
        pickIndex = 0;
        PickTitle.Text = p.Title;
        RenderPick();
        Resize(440, 58 + 46 * p.Choices.Count);
        Show(PickPanel);
        TakeFocus();
    }

    void RenderPick()
    {
        PickList.Children.Clear();
        for (int i = 0; i < pendingChoices.Count; i++)
        {
            var ch = pendingChoices[i];
            int idx = i;
            var row = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 4),
                Background = i == pickIndex ? Brush(theme.Soft) : Brushes.Transparent, Cursor = Cursors.Hand
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = $"{i + 1}.  {ch.Label}", Foreground = Brush(theme.Fg), FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = ch.Sub, Foreground = Brush(theme.Dim), FontSize = 11.5, Margin = new Thickness(18, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            row.Child = sp;
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = Choose(idx); };
            PickList.Children.Add(row);
        }
    }

    async Task Choose(int i)
    {
        if (i < 0 || i >= pendingChoices.Count) return;
        var ch = pendingChoices[i];
        ShowBusy("Working");
        Reply r;
        try { r = await ch.Run(); } catch (Exception ex) { r = new Message(ex.Message, false); }
        Present(r);
    }

    // ---------- input ----------

    void Island_Click(object sender, MouseButtonEventArgs e)
    {
        Squish();
        if (state is State.Idle or State.Hidden) { previousWindow = Native.GetForegroundWindow(); openedByHover = false; OpenInput(); }
        else if (state == State.Msg) GoIdle();
    }

    void No_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; GoIdle(); }
    void Yes_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; if (state == State.Alert) DismissAlert(); else _ = AcceptConfirm(); }

    async Task AcceptConfirm()
    {
        var c = pendingConfirm;
        if (c == null) return;
        pendingConfirm = null;
        ShowBusy("Working");
        Reply r;
        try { r = await c.OnYes(); } catch (Exception ex) { r = new Message(ex.Message, false); }
        Present(r);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (state == State.Confirm)
        {
            if (e.Key == Key.Escape || e.Key == Key.N) { GoIdle(); RestoreFocus(); e.Handled = true; }
            else if (e.Key == Key.Y || e.Key == Key.E && pendingConfirm?.Danger == true) { _ = AcceptConfirm(); e.Handled = true; }
            else if (e.Key == Key.Enter && pendingConfirm?.Danger == false) { _ = AcceptConfirm(); e.Handled = true; }
            return;
        }
        if (state == State.Pick)
        {
            if (e.Key == Key.Escape) { GoIdle(); RestoreFocus(); }
            else if (e.Key == Key.Down) { pickIndex = (pickIndex + 1) % pendingChoices.Count; RenderPick(); }
            else if (e.Key == Key.Up) { pickIndex = (pickIndex - 1 + pendingChoices.Count) % pendingChoices.Count; RenderPick(); }
            else if (e.Key == Key.Enter) _ = Choose(pickIndex);
            else if (e.Key == Key.Delete && pendingChoices.Count > 0 && pendingChoices[pickIndex].Delete != null) _ = DeleteChoice(pickIndex);
            else if (e.Key >= Key.D1 && e.Key <= Key.D9) _ = Choose(e.Key - Key.D1);
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9) _ = Choose(e.Key - Key.NumPad1);
            e.Handled = true;
            return;
        }
        if (state == State.Msg && e.Key == Key.Enter && copyText != null) { CopyAnswer(); e.Handled = true; return; }
        if (state == State.Msg && e.Key == Key.Escape) { GoIdle(); RestoreFocus(); e.Handled = true; return; }
        if (state == State.Alert && e.Key is Key.Escape or Key.Enter or Key.Space) { DismissAlert(); e.Handled = true; return; }
        if (state == State.Music)
        {
            if (e.Key == Key.Escape) { GoIdle(); RestoreFocus(); e.Handled = true; return; }
            if (e.Key == Key.Space) { _ = MusicAction(Media.PlayPause); e.Handled = true; return; }
            if (e.Key == Key.Right) { _ = MusicAction(Media.Next); e.Handled = true; return; }
            if (e.Key == Key.Left) { _ = MusicAction(Media.Previous); e.Handled = true; return; }
            if (e.Key is >= Key.A and <= Key.Z || e.Key is >= Key.D0 and <= Key.D9) { openedByHover = false; OpenInput(); return; }
        }
        if (state == State.Level)
        {
            if (e.Key is Key.Up or Key.Right) _ = NudgeLevel(5);
            else if (e.Key is Key.Down or Key.Left) _ = NudgeLevel(-5);
            else if (e.Key is Key.Escape or Key.Enter) { GoIdle(); RestoreFocus(); }
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    string prediction;
    bool suppressGhost;

    void ClearGhost()
    {
        prediction = null;
        GhostTyped.Text = GhostRest.Text = "";
    }

    void UpdateGhost()
    {
        if (suppressGhost || Input.CaretIndex != Input.Text.Length) { ClearGhost(); return; }
        string p = null;
        try { p = Predictor.Predict(Input.Text); } catch { }
        if (p == null || p.Length <= Input.Text.Length) { ClearGhost(); return; }
        prediction = p;
        GhostTyped.Text = Input.Text;
        GhostRest.Text = p[Input.Text.Length..];
        GhostRest.Foreground = Brush(theme.Dim);
    }

    void AcceptGhost()
    {
        var p = prediction;
        if (p == null) return;
        if (!p.EndsWith(" ") && Predictor.WantsSpace(p)) p += " ";
        Input.Text = p;
        Input.CaretIndex = p.Length;
    }

    async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { GoIdle(); RestoreFocus(); e.Handled = true; return; }
        if ((e.Key == Key.Tab || e.Key == Key.Right && Input.CaretIndex == Input.Text.Length) && prediction != null)
        {
            AcceptGhost(); e.Handled = true; return;
        }
        if (e.Key == Key.Tab) { e.Handled = true; return; }
        if (e.Key == Key.Back) { suppressGhost = true; Dispatcher.BeginInvoke(() => { suppressGhost = false; ClearGhost(); }, DispatcherPriority.Input); }
        if (e.Key == Key.Up && history.Count > 0)
        {
            historyPos = historyPos < 0 ? history.Count - 1 : Math.Max(0, historyPos - 1);
            Input.Text = history[historyPos]; Input.CaretIndex = Input.Text.Length; e.Handled = true; return;
        }
        if (e.Key == Key.Down && historyPos >= 0)
        {
            historyPos++;
            Input.Text = historyPos < history.Count ? history[historyPos] : "";
            if (historyPos >= history.Count) historyPos = -1;
            Input.CaretIndex = Input.Text.Length; e.Handled = true; return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var text = Input.Text.Trim();
        if (text.Length == 0) { GoIdle(); RestoreFocus(); return; }
        Predictor.Remember(text);

        Squish();
        Brain.LastIcon = null;
        ShowBusy("Thinking");
        Reply r;
        try { r = await Brain.Handle(text); }
        catch (Exception ex) { r = new Message(ex.Message, false); }
        Present(r);
    }

    async void Present(Reply r)
    {
        switch (r)
        {
            case Message m when m.Text.Length == 0: GoIdle(); break;
            case Message m:
                ShowMsg(m.Text, m.Ok);
                if (m.Ok) RestoreFocus();
                break;
            case Confirm c: ShowConfirm(c); break;
            case Level lv: ShowLevel(lv); break;
            case Answer a: ShowAnswer(a); break;
            case Shot s: await TakeShot(s.Area); break;
            case ShowMusic: if (MusicLive() || await RefreshMusic()) ShowMusicCard(); else ShowMsg("Nothing is playing", false); break;
            case Pick p: ShowPick(p); break;
            case Deferred d:
                ShowBusy("Working");
                Reply inner;
                try { inner = await d.Run(); } catch (Exception ex) { inner = new Message(ex.Message, false); }
                Present(inner);
                break;
            case ThemeChanged t:
                ApplyTheme(t.Theme);
                ShowMsg($"Theme: {t.Theme.Name}", true);
                break;
            case OpenSettings:
                GoIdle();
                SettingsRequested?.Invoke();
                break;
            case OpenGuide:
                GoIdle();
                GuideRequested?.Invoke();
                break;
            default: GoIdle(); RestoreFocus(); break;
        }
    }
}

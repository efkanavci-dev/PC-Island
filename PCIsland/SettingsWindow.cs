using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PCIsland.Core;

namespace PCIsland;

public class SettingsWindow : Window
{
    readonly MainWindow island;
    Theme t;
    Button hotkeyBtn;
    bool capturing;

    public event Action<Theme> ThemePicked;
    public event Action QuitRequested;
    public event Action AiPickerRequested;

    public SettingsWindow(MainWindow island)
    {
        this.island = island;
        Title = "PC Island settings";
        Width = 446; Height = 660;
        Chrome.Prepare(this);
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true; Topmost = true;
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => { if (capturing) StopCapture(false); };
        Build();
        WindowAnim.Attach(this);
    }

    static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    static string Solid(string hex) => hex.Length == 9 ? "#FF" + hex[3..] : hex;

    string PanelBg => t.Name == "Light" ? "#14000000" : "#38000000";
    string CardBg => t.Name == "Light" ? "#1A000000" : "#24FFFFFF";

    public void Build()
    {
        t = Themes.ByName(Settings.Current.Theme);
        Chrome.Apply(this, t);
        var root = new Border { Background = B(Themes.WithAlpha(t.Bg, t.Name == "Light" ? (byte)0x50 : (byte)0x30)) };

        var outer = new DockPanel();
        var header = new Grid { Margin = new Thickness(22, 16, 14, 8), Background = Brushes.Transparent };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        header.Children.Add(Label("PC Island", 20, FontWeights.SemiBold));
        var close = new Button { Content = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), HorizontalAlignment = HorizontalAlignment.Right };
        StyleButton(close, false); close.Width = 34; close.Padding = new Thickness(0, 6, 0, 6);
        close.Content = ""; close.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"); close.FontWeight = FontWeights.Normal; close.FontSize = 12;
        close.Click += (_, _) => Close();
        header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top);
        outer.Children.Add(header);

        var stack = new StackPanel { Margin = new Thickness(22, 0, 22, 22) };
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        outer.Children.Add(scroll);
        root.Child = outer;
        Content = root;

        Section(stack, "Theme");
        var dots = new System.Windows.Controls.Primitives.UniformGrid { Columns = Themes.All.Length, Rows = 1 };
        foreach (var th in Themes.All)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = th.Name };
            var g = new Grid { Width = 40, Height = 40 };
            bool sel = th.Name == t.Name;
            g.Children.Add(new Ellipse { Stroke = sel ? B(t.Acc) : Brushes.Transparent, StrokeThickness = 2.5 });
            g.Children.Add(new Ellipse { Width = 32, Height = 32, Fill = B(Themes.Solid(th)), Stroke = B("#40FFFFFF"), StrokeThickness = 1 });
            g.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = B(th.Acc) });
            cell.Children.Add(g);
            var name = Label(th.Name.Split(' ').Last(), 11.5, sel ? FontWeights.SemiBold : FontWeights.Normal, dim: !sel);
            name.Margin = new Thickness(0, 4, 0, 0);
            name.HorizontalAlignment = HorizontalAlignment.Center; name.TextAlignment = TextAlignment.Center; name.TextWrapping = TextWrapping.Wrap;
            cell.Children.Add(name);
            var pick = th;
            cell.MouseLeftButtonUp += (_, _) => { ThemePicked?.Invoke(pick); Build(); };
            dots.Children.Add(cell);
        }
        stack.Children.Add(dots);

        stack.Children.Add(Toggle("Stream mode (show in screen shares, no blur)", Settings.Current.StreamMode, v =>
        {
            Settings.Current.StreamMode = v;
            island.RefreshStreamMode();
        }));

        Section(stack, "Hotkey");
        var hk = new DockPanel();
        hk.Children.Add(Label("Opens the island from anywhere", 13, FontWeights.Normal, dim: true));
        hotkeyBtn = new Button { Content = HotkeyText(Settings.Current.HotkeyMods, Settings.Current.HotkeyKey), HorizontalAlignment = HorizontalAlignment.Right };
        StyleButton(hotkeyBtn, false);
        hotkeyBtn.Click += (_, _) => StartCapture();
        DockPanel.SetDock(hotkeyBtn, Dock.Right);
        hk.Children.Insert(0, hotkeyBtn);
        stack.Children.Add(hk);

        Section(stack, "Behavior");
        stack.Children.Add(Toggle("Start with Windows", Settings.Current.StartWithWindows, v => { Settings.Current.StartWithWindows = v; Core.Startup.Apply(v); }));
        stack.Children.Add(Toggle("Open when the mouse comes near", Settings.Current.HoverOpen, v => Settings.Current.HoverOpen = v));
        stack.Children.Add(Toggle("Hide during fullscreen games", Settings.Current.HideInFullscreen, v => Settings.Current.HideInFullscreen = v));
        stack.Children.Add(Toggle("Show the song that's playing", Settings.Current.ShowNowPlaying, v => Settings.Current.ShowNowPlaying = v));
        stack.Children.Add(Toggle("Battery alerts", Settings.Current.BatteryAlerts, v => Settings.Current.BatteryAlerts = v));

        Section(stack, "AI");
        var cur = Ai.Current;
        bool hasModel = cur != null && AiModels.IsDownloaded(cur);
        stack.Children.Add(Toggle("Use AI when the island doesn't understand", Settings.Current.AiEnabled && hasModel, v =>
        {
            Settings.Current.AiEnabled = v;
            if (v && !hasModel) Dispatcher.BeginInvoke(() => AiPickerRequested?.Invoke());
        }));
        var aiRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var choose = new Button { Content = hasModel ? "Change AI" : "Choose AI" };
        StyleButton(choose, !hasModel);
        choose.Click += (_, _) => AiPickerRequested?.Invoke();
        DockPanel.SetDock(choose, Dock.Right);
        aiRow.Children.Add(choose);
        var curText = Label(hasModel ? $"Using: {cur.Name} ({cur.Params})" : "No AI downloaded yet", 13, FontWeights.Normal, dim: !hasModel);
        curText.VerticalAlignment = VerticalAlignment.Center;
        aiRow.Children.Add(curText);
        stack.Children.Add(aiRow);
        stack.Children.Add(Note("During fullscreen games:"));
        var modes = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (id, label) in new[] { ("off", "Don't use AI"), ("gpu", "Use it anyway"), ("cpu", "Use processor only") })
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 8, 6) };
            bool sel = Settings.Current.AiGameMode == id;
            StyleButton(b, sel);
            var mode = id;
            b.Click += (_, _) => { Settings.Current.AiGameMode = mode; Settings.Save(); Build(); };
            modes.Children.Add(b);
        }
        stack.Children.Add(modes);
        var forgetAi = new Button { Content = "Forget AI answers", HorizontalAlignment = HorizontalAlignment.Left };
        StyleButton(forgetAi, false);
        forgetAi.Click += (_, _) => { Ai.ForgetAll(); forgetAi.Content = "Forgotten"; };
        stack.Children.Add(forgetAi);

        Section(stack, "Extra app folders");
        stack.Children.Add(Note("Folders with your own tools (.exe, shortcuts, scripts). The island can open what's inside, one folder per line."));
        var extra = Field(string.Join(Environment.NewLine, Settings.Current.ExtraAppFolders ?? new()), true);
        extra.TextChanged += (_, _) =>
        {
            Settings.Current.ExtraAppFolders = extra.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            Settings.Save();
        };
        extra.LostFocus += (_, _) => System.Threading.Tasks.Task.Run(AppIndex.Load);
        stack.Children.Add(extra);

        Section(stack, "Games");
        stack.Children.Add(Toggle("Put Roblox on high priority automatically", Settings.Current.RobloxHighPriority, v => Settings.Current.RobloxHighPriority = v));
        stack.Children.Add(Note("\"boost\" closes these background apps (one per line, app or process name):"));
        var boost = Field(string.Join(Environment.NewLine, Settings.Current.BoostClose), true);
        boost.TextChanged += (_, _) =>
        {
            Settings.Current.BoostClose = boost.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            Settings.Save();
        };
        stack.Children.Add(boost);
        var games = string.Join(", ", Settings.Current.Games.Select(g => g.Name));
        var gl = Note($"Games that open straight into the game: {games}. Ask Claude to add more.");
        gl.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(gl);

        Section(stack, "Shortcuts");
        stack.Children.Add(Note("Type the name in the island to run every command in it, top to bottom. One command per line."));
        foreach (var sc in Settings.Current.Shortcuts.ToList()) stack.Children.Add(ShortcutCard(sc));
        var add = new Button { Content = "+ Add shortcut", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        StyleButton(add, true);
        add.Click += (_, _) =>
        {
            Settings.Current.Shortcuts.Add(new Shortcut { Name = "new shortcut", Commands = new() { "open " } });
            Settings.Save(); Build();
            scroll.Dispatcher.BeginInvoke(() => ((ScrollViewer)((DockPanel)((Border)Content).Child).Children[1]).ScrollToEnd());
        };
        stack.Children.Add(add);

        Section(stack, "Learned words");
        var learned = Learner.Aliases;
        if (learned.Count == 0) stack.Children.Add(Note("Nothing yet. Teach it with \"müzik means spotify\", or open the right thing after it can't find something."));
        foreach (var (k, v) in learned.OrderBy(x => x.Key))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var x = new Button { Content = "Forget" };
            StyleButton(x, false); x.Padding = new Thickness(10, 3, 10, 3); x.FontSize = 12;
            var key = k;
            x.Click += (_, _) => { Learner.Forget(key); Build(); };
            DockPanel.SetDock(x, Dock.Right);
            row.Children.Add(x);
            var shown = System.IO.Path.GetFileName(v.TrimEnd('\\'));
            if (string.IsNullOrEmpty(shown) || v.Contains("://")) shown = v;
            var txt = Label($"{k}  →  {shown}", 13, FontWeights.Normal);
            txt.VerticalAlignment = VerticalAlignment.Center; txt.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(txt);
            stack.Children.Add(row);
        }

        var quit = new Button { Content = "Quit PC Island", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 26, 0, 0) };
        StyleButton(quit, false);
        quit.Foreground = B(t.Danger);
        quit.Click += (_, _) => QuitRequested?.Invoke();
        stack.Children.Add(quit);
    }

    TextBlock Label(string text, double size, FontWeight weight, bool dim = false) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = B(dim ? t.Dim : t.Fg),
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI")
    };

    TextBlock Note(string text)
    {
        var n = Label(text, 12, FontWeights.Normal, dim: true);
        n.TextWrapping = TextWrapping.Wrap; n.Margin = new Thickness(0, 0, 0, 8);
        return n;
    }

    void Section(Panel p, string title)
    {
        var l = Label(title.ToUpperInvariant(), 11, FontWeights.SemiBold, dim: true);
        l.Margin = new Thickness(0, 18, 0, 8);
        p.Children.Add(l);
    }

    void StyleButton(Button b, bool accent)
    {
        var bg = accent ? B(t.Acc) : B(CardBg);
        var fg = accent ? (IsLight(t.Acc) ? Brushes.Black : Brushes.White) : B(t.Fg);
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        tpl.VisualTree = bd;
        b.Template = tpl;
        b.Background = bg; b.Foreground = fg; b.Padding = new Thickness(14, 7, 14, 7);
        b.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); b.FontSize = 13; b.FontWeight = FontWeights.SemiBold;
        b.Cursor = Cursors.Hand; b.BorderThickness = new Thickness(0);
        b.MouseEnter += (_, _) => b.Opacity = 0.85;
        b.MouseLeave += (_, _) => b.Opacity = 1;
    }

    static bool IsLight(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 170;
    }

    UIElement Toggle(string text, bool value, Action<bool> set)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 8), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11) };
        var knob = new Ellipse { Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
        track.Child = knob;
        void Paint(bool v)
        {
            track.Background = v ? B(t.Acc) : B(CardBg);
            knob.Margin = new Thickness(v ? 21 : 3, 0, 0, 0);
            knob.Fill = v && IsLight(t.Acc) ? Brushes.Black : Brushes.White;
        }
        Paint(value);
        DockPanel.SetDock(track, Dock.Right);
        row.Children.Add(track);
        var l = Label(text, 13.5, FontWeights.Normal); l.VerticalAlignment = VerticalAlignment.Center;
        l.TextWrapping = TextWrapping.Wrap; l.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(l);
        bool cur = value;
        row.MouseLeftButtonUp += (_, _) => { cur = !cur; Paint(cur); set(cur); Settings.Save(); };
        return row;
    }

    TextBox Field(string text, bool multi)
    {
        var tb = new TextBox
        {
            Text = text, Background = B(PanelBg), Foreground = B(t.Fg), CaretBrush = B(t.Acc), SelectionBrush = B(t.Acc),
            BorderThickness = new Thickness(0), Padding = new Thickness(8, 6, 8, 6), FontSize = 13,
            FontFamily = new FontFamily(multi ? "Cascadia Mono, Consolas" : "Segoe UI Variable Text, Segoe UI"),
            AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.NoWrap : TextWrapping.NoWrap
        };
        if (multi) { tb.MinHeight = 64; tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
        return tb;
    }

    UIElement ShortcutCard(Shortcut sc)
    {
        var card = new Border { Background = B(CardBg), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8) };
        var sp = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var del = new Button { Content = "Delete" };
        StyleButton(del, false); del.Background = B(PanelBg); del.Foreground = B(t.Danger); del.Padding = new Thickness(10, 4, 10, 4); del.FontSize = 12;
        del.Click += (_, _) => { Settings.Current.Shortcuts.Remove(sc); Settings.Save(); Build(); };
        DockPanel.SetDock(del, Dock.Right);
        top.Children.Add(del);
        var name = Field(sc.Name, false);
        name.Margin = new Thickness(0, 0, 8, 0); name.FontWeight = FontWeights.SemiBold;
        name.TextChanged += (_, _) => { sc.Name = name.Text.Trim(); Settings.Save(); };
        top.Children.Add(name);
        sp.Children.Add(top);
        var cmds = Field(string.Join(Environment.NewLine, sc.Commands), true);
        cmds.TextChanged += (_, _) =>
        {
            sc.Commands = cmds.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            Settings.Save();
        };
        sp.Children.Add(cmds);
        card.Child = sp;
        return card;
    }

    // ---------- hotkey capture ----------

    void StartCapture()
    {
        capturing = true;
        island.PauseHotkey();
        hotkeyBtn.Content = "Press new keys...";
    }

    void StopCapture(bool changed)
    {
        capturing = false;
        if (!island.RegisterHotkey() && changed)
        {
            hotkeyBtn.Content = "That one is taken";
            return;
        }
        hotkeyBtn.Content = HotkeyText(Settings.Current.HotkeyMods, Settings.Current.HotkeyKey);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (!capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { StopCapture(false); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= 1;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 2;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= 4;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= 8;
        if (mods == 0 && !(key >= Key.F1 && key <= Key.F24)) { hotkeyBtn.Content = "Add Ctrl, Alt, Shift or Win"; return; }
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        uint oldMods = Settings.Current.HotkeyMods, oldKey = Settings.Current.HotkeyKey;
        Settings.Current.HotkeyMods = mods; Settings.Current.HotkeyKey = vk;
        if (!island.RegisterHotkey())
        {
            Settings.Current.HotkeyMods = oldMods; Settings.Current.HotkeyKey = oldKey;
            island.RegisterHotkey();
            capturing = false;
            hotkeyBtn.Content = $"{HotkeyText(mods, vk)} is taken";
            return;
        }
        Settings.Save();
        capturing = false;
        hotkeyBtn.Content = HotkeyText(mods, vk);
    }

    public static string HotkeyText(uint mods, uint vk)
    {
        var parts = new List<string>();
        if ((mods & 2) != 0) parts.Add("Ctrl");
        if ((mods & 1) != 0) parts.Add("Alt");
        if ((mods & 4) != 0) parts.Add("Shift");
        if ((mods & 8) != 0) parts.Add("Win");
        var k = KeyInterop.KeyFromVirtualKey((int)vk);
        string name = k switch
        {
            Key.Space => "Space", Key.OemTilde => "`", Key.OemPeriod => ".", Key.OemComma => ",",
            >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
            _ => k.ToString()
        };
        parts.Add(name);
        return string.Join(" + ", parts);
    }
}

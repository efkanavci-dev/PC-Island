using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCIsland.Core;

namespace PCIsland;

public class AiPickerWindow : Window
{
    Theme t;
    StackPanel list;
    public event Action Changed;

    public AiPickerWindow()
    {
        Title = "Choose your AI";
        Width = 536; Height = 736;
        Chrome.Prepare(this);
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true; Topmost = true;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        AiModels.Progress += OnProgress;
        Closed += (_, _) => AiModels.Progress -= OnProgress;
        Build();
        WindowAnim.Attach(this);
    }

    void OnProgress(AiModels.Job job) => Dispatcher.BeginInvoke(() =>
    {
        if (job.Finished && job.Error == null && AiModels.IsDownloaded(job.Model) && Ai.Current == null)
        {
            Settings.Current.AiModel = job.Model.Id;
            Settings.Current.AiEnabled = true;
            Settings.Save();
            Changed?.Invoke();
        }
        Fill();
    });

    static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    static string Solid(string hex) => hex.Length == 9 ? "#FF" + hex[3..] : hex;
    string PanelBg => t.Name == "Light" ? "#14000000" : "#38000000";
    string CardBg => t.Name == "Light" ? "#1A000000" : "#24FFFFFF";
    string AccText => t.Acc == "#FFFFFFFF" ? t.Fg : t.Acc;

    TextBlock Label(string text, double size, FontWeight weight, bool dim = false, string color = null) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = B(color ?? (dim ? t.Dim : t.Fg)),
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), TextWrapping = TextWrapping.Wrap
    };

    Border Button(string text, bool accent, Action click, bool danger = false)
    {
        var bg = accent ? B(t.Acc) : B(PanelBg);
        var fg = accent ? (IsLight(t.Acc) ? Brushes.Black : Brushes.White) : danger ? B(t.Danger) : B(t.Fg);
        var b = new Border { CornerRadius = new CornerRadius(12), Background = bg, Padding = new Thickness(14, 7, 14, 7), Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0) };
        b.Child = new TextBlock { Text = text, Foreground = fg, FontWeight = FontWeights.SemiBold, FontSize = 13, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") };
        b.MouseEnter += (_, _) => b.Opacity = 0.85;
        b.MouseLeave += (_, _) => b.Opacity = 1;
        b.MouseLeftButtonDown += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    static bool IsLight(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 170;
    }

    void Build()
    {
        t = Themes.ByName(Settings.Current.Theme);
        Chrome.Apply(this, t);
        var root = new Border
        {
            Background = B(Themes.WithAlpha(t.Bg, t.Name == "Light" ? (byte)0x50 : (byte)0x30))
        };
        var outer = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(22, 16, 14, 4), Background = Brushes.Transparent };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var close = new Border { Width = 34, Height = 30, CornerRadius = new CornerRadius(10), Background = B(CardBg), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };
        close.Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Foreground = B(t.Fg), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        close.MouseLeftButtonDown += (_, e) => { e.Handled = true; Close(); };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var titles = new StackPanel();
        titles.Children.Add(Label("Choose your AI", 20, FontWeights.SemiBold));
        titles.Children.Add(Label("It only steps in when the island doesn't understand you. It runs on your PC, so nothing you type is sent anywhere, and it's free.", 12.5, FontWeights.Normal, dim: true));
        header.Children.Add(titles);
        DockPanel.SetDock(header, Dock.Top);
        outer.Children.Add(header);

        var (gb, gname) = AiModels.Gpu();
        var pc = Label(gb >= 1.9 ? $"Your graphics card: {gname} ({gb:0} GB)" : "No separate graphics card found, so the AI will use your processor.", 12.5, FontWeights.Normal, dim: true);
        pc.Margin = new Thickness(22, 6, 22, 4);
        DockPanel.SetDock(pc, Dock.Top);
        outer.Children.Add(pc);

        list = new StackPanel { Margin = new Thickness(22, 4, 22, 22) };
        outer.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
        root.Child = outer;
        Content = root;
        Fill();
    }

    void Fill()
    {
        list.Children.Clear();
        var best = AiModels.BestForThisPc();
        foreach (var m in AiModels.All)
        {
            var card = new Border { Background = B(CardBg), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 10, 0, 0) };
            bool current = Settings.Current.AiModel == m.Id && AiModels.IsDownloaded(m);
            if (current) { card.BorderBrush = B(t.Acc); card.BorderThickness = new Thickness(2); }
            var sp = new StackPanel();

            var top = new WrapPanel();
            top.Children.Add(Label(m.Name, 17, FontWeights.SemiBold));
            var par = Label($"  {m.Params}", 12.5, FontWeights.Normal, dim: true); par.VerticalAlignment = VerticalAlignment.Bottom; par.Margin = new Thickness(0, 0, 10, 2);
            top.Children.Add(par);
            if (m.Id == best.Id) top.Children.Add(Badge("Best for your PC", t.Acc));
            else if (AiModels.MaybeSlow(m)) top.Children.Add(Badge("May be slow on your PC", t.Danger));
            if (current) top.Children.Add(Badge("In use", t.Fg));
            sp.Children.Add(top);

            var d = Label(m.Description, 13.5, FontWeights.Normal); d.Margin = new Thickness(0, 6, 0, 6);
            sp.Children.Add(d);
            sp.Children.Add(Label("✓  " + m.Good, 12.5, FontWeights.Normal, color: AccText));
            sp.Children.Add(Label("–  " + m.Weak, 12.5, FontWeights.Normal, dim: true));
            var specs = Label($"Download {AiModels.Size(m.Bytes)} · needs about {m.VramGb:0.#} GB of graphics memory to be quick", 12, FontWeights.Normal, dim: true);
            specs.Margin = new Thickness(0, 6, 0, 10);
            sp.Children.Add(specs);

            var actions = new DockPanel { LastChildFill = false };
            AiModels.Jobs.TryGetValue(m.Id, out var job);
            if (job != null && !job.Finished)
            {
                var bar = new Grid { Height = 8, Width = 240, VerticalAlignment = VerticalAlignment.Center };
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = B(PanelBg) });
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = B(t.Acc), HorizontalAlignment = HorizontalAlignment.Left, Width = 240 * job.Percent / 100 });
                actions.Children.Add(bar);
                var pct = Label(job.Verifying ? "  Checking..." : $"  {job.Percent:0}%  ({AiModels.Size(job.Done)} of {AiModels.Size(job.Total)})", 12, FontWeights.Normal, dim: true);
                pct.VerticalAlignment = VerticalAlignment.Center;
                actions.Children.Add(pct);
                var stop = Button("Stop", false, () => job.Cancel.Cancel());
                DockPanel.SetDock(stop, Dock.Right);
                actions.Children.Add(stop);
            }
            else if (AiModels.IsDownloaded(m))
            {
                var rm = Button("Remove", false, () =>
                {
                    AiModels.Remove(m);
                    if (Settings.Current.AiModel == m.Id)
                    {
                        var other = AiModels.All.FirstOrDefault(AiModels.IsDownloaded);
                        Settings.Current.AiModel = other?.Id ?? "";
                        Settings.Save();
                    }
                    Changed?.Invoke();
                    Fill();
                }, danger: true);
                DockPanel.SetDock(rm, Dock.Right);
                actions.Children.Add(rm);
                if (!current)
                {
                    var use = Button("Use this", true, () =>
                    {
                        Settings.Current.AiModel = m.Id;
                        Settings.Current.AiEnabled = true;
                        Settings.Save();
                        Changed?.Invoke();
                        Fill();
                    });
                    DockPanel.SetDock(use, Dock.Right);
                    actions.Children.Add(use);
                }
            }
            else
            {
                if (job?.Error != null && job.Error != "Stopped")
                {
                    var err = Label(job.Error, 12, FontWeights.Normal, color: t.Danger);
                    err.VerticalAlignment = VerticalAlignment.Center; err.MaxWidth = 280;
                    actions.Children.Add(err);
                }
                var dl = Button(job?.Error != null ? $"Try again ({AiModels.Size(m.Bytes)})" : $"Download ({AiModels.Size(m.Bytes)})", true, () => { AiModels.Download(m); Fill(); });
                DockPanel.SetDock(dl, Dock.Right);
                actions.Children.Add(dl);
            }
            sp.Children.Add(actions);
            card.Child = sp;
            list.Children.Add(card);
        }

        var foot = Label("You can keep more than one and switch any time. Models are saved in your AppData folder. "
                       + "\"B\" means billions of parameters: bigger understands better but needs more memory.", 12, FontWeights.Normal, dim: true);
        foot.Margin = new Thickness(0, 14, 0, 0);
        list.Children.Add(foot);
    }

    Border Badge(string text, string color)
    {
        var b = new Border { CornerRadius = new CornerRadius(8), BorderBrush = B(color), BorderThickness = new Thickness(1), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(0, 0, 6, 2), VerticalAlignment = VerticalAlignment.Bottom };
        b.Child = new TextBlock { Text = text, FontSize = 11, Foreground = B(color == "#FFFFFFFF" ? t.Fg : color), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") };
        return b;
    }
}

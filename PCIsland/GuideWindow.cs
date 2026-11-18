using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCIsland.Core;

namespace PCIsland;

public class GuideWindow : Window
{
    record Item(string Command, string What);
    record Section(string Title, Item[] Items, string Tip = null);

    static readonly Section[] Sections =
    {
        new("Getting started", new Item[]
        {
            new("Ctrl + Space", "Open the island from anywhere (change it in Settings)"),
            new("Move the mouse near the pill", "Opens it too. Click the pill works as well"),
            new("Tab  or  →", "Accept the gray guess while typing"),
            new("↑  ↓", "Go back through commands you typed before"),
            new("Esc", "Close the island"),
            new("Y", "Confirm risky things (delete, close, shutdown). Enter confirms safe ones"),
            new("help", "Short list of commands inside the island"),
        }, "Turkish and English both work, and you can mix them. Typos are fine."),
        new("Apps and windows", new Item[]
        {
            new("open discord", "Open an app. Just \"discord\" works too"),
            new("discord'u aç", "Same in Turkish"),
            new("close chrome", "Close an app (asks first)"),
            new("chrome'u kapat", "Same in Turkish"),
            new("join general vc in gifs", "Opens Discord and joins that voice channel (gifs sunucusundaki general ses kanalına gir)"),
            new("switch to opera","Bring a window to the front (operaya geç)"),
            new("chrome left", "Snap a window to the left or right (chrome sola)"),
            new("maximize opera", "Make a window big, or minimize it"),
            new("minimize all", "Show the desktop (hepsini küçült)"),
        }),
        new("Browser tabs", new Item[]
        {
            new("close tab", "Close the tab you're on (sekmeyi kapat)"),
            new("close 3 tabs", "Close several"),
            new("close tabs on opera", "Close every tab but keep Opera open (operadaki sekmeleri kapat)"),
        }, "Ctrl+Shift+T in the browser brings closed tabs back."),
        new("Files, folders and web", new Item[]
        {
            new("open downloads", "Open a folder (indirilenleri aç)"),
            new("open school folder", "Find a folder or file by name"),
            new("delete old_video.mp4", "Delete forever. Always asks, and you press Y"),
            new("old_video.mp4'ü sil", "Same in Turkish"),
            new("open youtube", "Open a website, or type an address like roblox.com"),
            new("search how to fix wifi", "Google something (kedi videoları ara)"),
        }),
        new("Music and sound", new Item[]
        {
            new("pause", "Pause music (durdur). play / devam to continue"),
            new("next", "Next song (sonraki şarkı). previous for the one before"),
            new("now playing", "Show the song card with buttons"),
            new("volume 50", "Set the volume (ses 30, sesi aç, sesi kıs)"),
            new("mute", "Mute (sessiz). unmute to bring it back"),
        }, "While a song plays, the pill shows it. Hover over it for pause / next buttons. On a volume bar, ↑ ↓ or the mouse wheel change it."),
        new("Screen and PC", new Item[]
        {
            new("brightness 70", "Laptop screen brightness (parlaklık 50)"),
            new("battery", "Battery level and time left (pil)"),
            new("ram", "How much memory is used. cpu for the processor"),
            new("wifi off", "Wi-Fi on or off (wifi aç / wifi kapat)"),
            new("bluetooth on", "Bluetooth on or off"),
            new("dark mode", "Windows dark mode. light mode to switch back"),
            new("screenshot", "Save a screenshot to the Desktop (ekran görüntüsü)"),
            new("screenshot area", "Drag a box. It goes to your clipboard"),
            new("lock", "Lock the PC (kilitle). sleep, restart, shutdown ask first"),
        }),
        new("Time", new Item[]
        {
            new("timer 10 min", "Countdown in the pill, alarm at the end (10 dakika sayaç)"),
            new("stop timer", "Cancel it (sayacı durdur)"),
            new("remind me at 9 pm to study", "Pops up at that time"),
            new("remind me in 20 min to eat", "Or: 20 dakika sonra yemek ye hatırlat"),
            new("reminders", "See them. Delete key removes one"),
        }),
        new("Notes, clipboard and math", new Item[]
        {
            new("note buy milk", "Save a note (süt al diye not al)"),
            new("notes", "See notes. Enter copies one, Delete removes it"),
            new("clipboard", "Last 10 things you copied. Enter copies it again"),
            new("15*24", "Calculator. Enter copies the answer"),
            new("%18 of 250", "Percent (250'nin %18'i)"),
        }, "Clipboard history is only kept in memory and is gone when the island restarts."),
        new("Games", new Item[]
        {
            new("play blox fruits", "Start a Roblox game straight away (blox fruits oyna)"),
            new("open yba", "Short names work too: yba = Your Bizarre Adventure. Found games are remembered"),
            new("boost", "Close background apps before playing (list in Settings)"),
            new("gaming mode", "Your own shortcut: closes Chrome and Spotify, opens Discord"),
        }, "Roblox gets high priority on its own while it runs. The island hides during fullscreen games; Ctrl+Space still opens it."),
        new("Teach it", new Item[]
        {
            new("müzik means spotify", "From now on \"müzik\" opens Spotify (müzik demek spotify)"),
            new("forget müzik", "Remove a word it learned (müzik'i unut)"),
            new("forget everything", "Reset everything it learned (asks first)"),
        }, "It also learns by itself: if it can't find something and you open the right thing right after, it remembers. Apps you open most get picked first."),
        new("AI (optional)", new Item[]
        {
            new("open the thing I play music with", "With AI on, it figures out loose sentences like this"),
            new("open spotify and turn the volume down", "It can do a few things at once (it shows the list and asks first)"),
            new("arkadaşlarla konuştuğum uygulamayı aç", "Turkish sentences work too"),
            new("what does food mean", "Ask a question and it answers in a few sentences (yemek ne demek)"),
            new("why is the sky blue?", "Enter copies the answer. Small AIs can be wrong, so double-check important things"),
            new("translate food to turkish", "Translate words or sentences (food'u türkçeye çevir, kitap ingilizcesi)"),
            new("translate yemek", "No language given: Turkish goes to English, anything else to Turkish"),
            new("how do you say thank you in german", "Other languages work too. Without AI it opens Google Translate"),
        }, "Turn it on in Settings → AI and pick a model. It runs on your PC, is free, and only wakes up when the island doesn't understand. Answers it figured out once are remembered, so they're instant next time."),
        new("Look and settings", new Item[]
        {
            new("theme glass", "Change the color (tema mor). black, glass, midnight, violet, ember, light"),
            new("settings", "Open settings (ayarlar). Also: right-click the pill or the tray icon"),
            new("guide", "This guide (kılavuz)"),
        }, "In Settings you can make your own shortcuts: give it a name and write one command per line."),
    };

    readonly MainWindow island;
    Theme t;
    StackPanel list;
    TextBox search;

    public GuideWindow(MainWindow island)
    {
        this.island = island;
        Title = "PC Island guide";
        Width = 536; Height = 696;
        Chrome.Prepare(this);
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true; Topmost = true;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Build();
        WindowAnim.Attach(this);
    }

    static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    static string Solid(string hex) => hex.Length == 9 ? "#FF" + hex[3..] : hex;
    string PanelBg => t.Name == "Light" ? "#14000000" : "#38000000";
    string CardBg => t.Name == "Light" ? "#1A000000" : "#24FFFFFF";

    TextBlock Label(string text, double size, FontWeight weight, bool dim = false) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = B(dim ? t.Dim : t.Fg),
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), TextWrapping = TextWrapping.Wrap
    };

    public void Build()
    {
        t = Themes.ByName(Settings.Current.Theme);
        Chrome.Apply(this, t);
        var root = new Border
        {
            Background = B(Themes.WithAlpha(t.Bg, t.Name == "Light" ? (byte)0x50 : (byte)0x30))
        };
        var outer = new DockPanel();

        var header = new DockPanel { Margin = new Thickness(22, 16, 14, 6), Background = Brushes.Transparent };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var close = new Border { Width = 34, Height = 30, CornerRadius = new CornerRadius(10), Background = B(CardBg), Cursor = Cursors.Hand };
        close.Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Foreground = B(t.Fg), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        close.MouseLeftButtonDown += (_, e) => { e.Handled = true; Close(); };
        close.MouseEnter += (_, _) => close.Opacity = 0.8;
        close.MouseLeave += (_, _) => close.Opacity = 1;
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var titles = new StackPanel();
        titles.Children.Add(Label("PC Island guide", 20, FontWeights.SemiBold));
        titles.Children.Add(Label("Click any command to try it in the island.", 12.5, FontWeights.Normal, dim: true));
        header.Children.Add(titles);
        DockPanel.SetDock(header, Dock.Top);
        outer.Children.Add(header);

        var searchBox = new Border { CornerRadius = new CornerRadius(12), Background = B(CardBg), Margin = new Thickness(22, 8, 22, 6), Padding = new Thickness(12, 2, 12, 2) };
        var sg = new Grid();
        search = new TextBox
        {
            Background = Brushes.Transparent, Foreground = B(t.Fg), CaretBrush = B(t.Acc), BorderThickness = new Thickness(0),
            FontSize = 14, Padding = new Thickness(0, 7, 0, 7), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI")
        };
        var ph = Label("Search the guide... (volume, sekme, timer)", 14, FontWeights.Normal, dim: true);
        ph.IsHitTestVisible = false; ph.VerticalAlignment = VerticalAlignment.Center; ph.TextWrapping = TextWrapping.NoWrap;
        search.TextChanged += (_, _) => { ph.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Fill(); };
        sg.Children.Add(ph); sg.Children.Add(search);
        searchBox.Child = sg;
        DockPanel.SetDock(searchBox, Dock.Top);
        outer.Children.Add(searchBox);

        list = new StackPanel { Margin = new Thickness(22, 0, 22, 22) };
        outer.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
        root.Child = outer;
        Content = root;
        Fill();
        Loaded += (_, _) => search.Focus();
    }

    void Fill()
    {
        list.Children.Clear();
        var q = Text.Clean(search?.Text ?? "");
        int shown = 0;
        foreach (var s in Sections)
        {
            var items = q.Length == 0 ? s.Items : s.Items.Where(i => Text.Clean(i.Command + " " + i.What + " " + s.Title).Contains(q)).ToArray();
            if (items.Length == 0) continue;
            var h = Label(s.Title.ToUpperInvariant(), 11, FontWeights.SemiBold, dim: true);
            h.Margin = new Thickness(0, 16, 0, 8);
            list.Children.Add(h);
            foreach (var it in items)
            {
                shown++;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), Background = Brushes.Transparent };
                bool isKey = s.Title == "Getting started";
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(10), Background = B(CardBg), Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top, MinWidth = 150,
                    Cursor = isKey ? Cursors.Arrow : Cursors.Hand
                };
                var cmd = new TextBlock { Text = it.Command, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, Foreground = B(t.Acc == "#FFFFFFFF" ? t.Fg : t.Acc) };
                pill.Child = cmd;
                if (!isKey)
                {
                    var c = it.Command;
                    pill.MouseEnter += (_, _) => pill.Opacity = 0.8;
                    pill.MouseLeave += (_, _) => pill.Opacity = 1;
                    pill.MouseLeftButtonUp += (_, _) => island.FillInput(c);
                    pill.ToolTip = "Try it";
                }
                DockPanel.SetDock(pill, Dock.Left);
                row.Children.Add(pill);
                var what = Label(it.What, 13, FontWeights.Normal);
                what.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(what);
                list.Children.Add(row);
            }
            if (s.Tip != null && q.Length == 0)
            {
                var tip = Label(s.Tip, 12, FontWeights.Normal, dim: true);
                tip.Margin = new Thickness(0, 2, 0, 0);
                list.Children.Add(tip);
            }
        }
        if (shown == 0) list.Children.Add(new TextBlock { Text = "Nothing matches. Try another word.", Foreground = B(t.Dim), Margin = new Thickness(0, 16, 0, 0), FontSize = 13 });
    }
}

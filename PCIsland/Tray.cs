using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WF = System.Windows.Forms;
using PCIsland.Core;

namespace PCIsland;

public sealed class Tray : IDisposable
{
    readonly WF.NotifyIcon icon;
    public event Action OpenIsland, OpenSettings, OpenGuide, Quit;
    public event Action<Theme> ThemePicked;

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    public Tray()
    {
        icon = new WF.NotifyIcon { Text = "PC Island", Visible = true };
        SetTheme(Themes.ByName(Settings.Current.Theme));
        icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) OpenIsland?.Invoke(); };
        BuildMenu();
    }

    public void BuildMenu()
    {
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Open island", null, (_, _) => OpenIsland?.Invoke());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings?.Invoke());
        menu.Items.Add("Guide", null, (_, _) => OpenGuide?.Invoke());
        var themes = new WF.ToolStripMenuItem("Theme");
        foreach (var t in Themes.All)
        {
            var item = new WF.ToolStripMenuItem(t.Name, Dot(t, 16)) { Checked = t.Name == Settings.Current.Theme };
            var th = t;
            item.Click += (_, _) => ThemePicked?.Invoke(th);
            themes.DropDownItems.Add(item);
        }
        menu.Items.Add(themes);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Quit PC Island", null, (_, _) => Quit?.Invoke());
        var old = icon.ContextMenuStrip;
        icon.ContextMenuStrip = menu;
        old?.Dispose();
    }

    static Color C(string hex)
    {
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return Color.FromArgb(255, c.R, c.G, c.B);
    }

    static Bitmap Dot(Theme t, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var bg = new SolidBrush(C(Themes.Solid(t)));
        using var acc = new SolidBrush(C(t.Acc));
        g.FillEllipse(bg, 0, 0, size - 1, size - 1);
        g.FillEllipse(acc, size * 0.32f, size * 0.32f, size * 0.36f, size * 0.36f);
        return bmp;
    }

    public static Bitmap Pill(Theme t, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float h = size * 0.5f, w = size * 0.96f, x = (size - w) / 2, y = (size - h) / 2;
        using var path = new GraphicsPath();
        path.AddArc(x, y, h, h, 90, 180);
        path.AddArc(x + w - h, y, h, h, 270, 180);
        path.CloseFigure();
        var bgc = C(Themes.Solid(t));
        if (bgc.GetBrightness() < 0.08f) bgc = Color.FromArgb(255, 12, 12, 14);
        using var bg = new SolidBrush(bgc);
        g.FillPath(bg, path);
        using var edge = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1, size / 32f));
        if (t.Name != "Light") g.DrawPath(edge, path);
        else { using var dark = new Pen(Color.FromArgb(160, 0, 0, 0), Math.Max(1, size / 32f)); g.DrawPath(dark, path); }
        using var acc = new SolidBrush(C(t.Acc));
        float d = h * 0.38f;
        g.FillEllipse(acc, x + w - h * 0.5f - d * 1.2f, y + (h - d) / 2, d, d);
        return bmp;
    }

    public void SetTheme(Theme t)
    {
        using var bmp = Pill(t, 32);
        var hIcon = bmp.GetHicon();
        var old = icon.Icon;
        icon.Icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        old?.Dispose();
        BuildMenu();
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}

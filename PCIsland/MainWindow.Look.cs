using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using PCIsland.Core;

namespace PCIsland;

public partial class MainWindow
{
    static readonly string[] BoldAfter = { "Opening ", "Closed ", "Switched to ", "Starting ", "Minimized ", "Maximized ", "Deleted ", "Theme: ", "Playing · ", "Paused · ", "Next · ", "Previous · " };

    void SetMessageText(string text)
    {
        MsgText.Inlines.Clear();
        var prefix = BoldAfter.FirstOrDefault(p => text.StartsWith(p, StringComparison.Ordinal));
        if (prefix == null) { MsgText.Inlines.Add(new Run(text)); return; }
        var rest = text[prefix.Length..];
        int cut = rest.IndexOf(" · ", StringComparison.Ordinal);
        string name = cut > 0 ? rest[..cut] : rest, tail = cut > 0 ? rest[cut..] : "";
        MsgText.Inlines.Add(new Run(prefix));
        MsgText.Inlines.Add(new Run(name) { FontWeight = FontWeights.SemiBold });
        if (tail.Length > 0) MsgText.Inlines.Add(new Run(tail) { Foreground = Brush(theme.Dim) });
    }

    void SetMessageIcon(bool ok)
    {
        var src = ok ? Brain.LastIcon : null;
        Brain.LastIcon = null;
        var img = src != null ? Icons.For(src) : null;
        MsgIcon.Source = img;
        MsgIcon.Visibility = img != null ? Visibility.Visible : Visibility.Collapsed;
        MsgDot.Visibility = img != null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- morph ----------

    void MorphIn()
    {
        var ease = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(360);
        var begin = TimeSpan.FromMilliseconds(70);
        BodyScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, d) { EasingFunction = ease, BeginTime = begin });
        BodyScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.8, 1, d) { EasingFunction = ease, BeginTime = begin });
    }

    // ---------- shine + clip ----------

    void SyncLook()
    {
        var r = Island.CornerRadius.TopLeft;
        Shine.CornerRadius = new CornerRadius(Math.Max(0, r - 1));
        double w = Math.Max(0, Island.ActualWidth - 2), h = Math.Max(0, Island.ActualHeight - 2);
        Inner.Clip = new RectangleGeometry(new Rect(0, 0, w, h), Math.Max(0, r - 1), Math.Max(0, r - 1));
        Shimmer.Width = Math.Max(80, w * 0.45);
    }

    void ApplyLook(Theme t)
    {
        bool light = t.Name == "Light";
        ShineTop.Color = light ? Color.FromArgb(0, 255, 255, 255) : Color.FromArgb(t.Glass ? (byte)0x22 : (byte)0x16, 255, 255, 255);
        ShineEdge.Color = light ? Color.FromArgb(0x30, 0, 0, 0) : Color.FromArgb(t.Glass ? (byte)0x80 : (byte)0x50, 255, 255, 255);
        ShimmerMid.Color = light ? Color.FromArgb(0x22, 0, 0, 0) : Color.FromArgb(0x30, 255, 255, 255);

        var fx = (DropShadowEffect)Shadow.Effect;
        Shadow.Background = Brushes.Black;
        fx.Color = Colors.Black; fx.Opacity = 0.35; fx.BlurRadius = 18; fx.ShadowDepth = 4;
        StartBreathing();
    }

    // ---------- shimmer while busy ----------

    bool shimmering;

    void SetShimmer(bool on)
    {
        if (on == shimmering) return;
        shimmering = on;
        if (!on)
        {
            Shimmer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
            ShimmerMove.BeginAnimation(TranslateTransform.XProperty, null);
            return;
        }
        Shimmer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        var w = Math.Max(200, Island.ActualWidth);
        var anim = new DoubleAnimation(-Shimmer.Width, w + 40, TimeSpan.FromMilliseconds(1100))
        { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        Timeline.SetDesiredFrameRate(anim, 40);
        ShimmerMove.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    // ---------- breathing dot ----------

    void StartBreathing()
    {
        IdleDot.BeginAnimation(OpacityProperty, null);
        IdleDot.Opacity = 1;
    }

    // ---------- squish ----------

    void Squish()
    {
        var down = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(80)) { AutoReverse = true, EasingFunction = new QuadraticEase() };
        IslandScale.BeginAnimation(ScaleTransform.ScaleXProperty, down);
        IslandScale.BeginAnimation(ScaleTransform.ScaleYProperty, down.Clone());
    }
}

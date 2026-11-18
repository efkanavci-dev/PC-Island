using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PCIsland;

public static class WindowAnim
{
    public static void Attach(Window w)
    {
        bool closingDone = false;
        UIElement Target() => w.AllowsTransparency ? w : w.Content as UIElement;
        if (w.AllowsTransparency) w.Opacity = 0;

        w.Loaded += (_, _) =>
        {
            if (w.Content is not FrameworkElement root) return;
            var scale = new ScaleTransform(0.96, 0.96);
            var move = new TranslateTransform(0, 10);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(move);
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            root.RenderTransform = group;
            var d = TimeSpan.FromMilliseconds(260);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Target()?.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, d) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, d) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, d) { EasingFunction = ease });
        };

        w.Closing += (_, e) =>
        {
            if (closingDone) return;
            var target = Target();
            if (target == null) return;
            e.Cancel = true;
            closingDone = true;
            var fade = new DoubleAnimation(target.Opacity, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => w.Close();
            target.BeginAnimation(UIElement.OpacityProperty, fade);
        };
    }
}

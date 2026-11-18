using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace PCIsland;

public static class Chrome
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int l, r, t, b; }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34, DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public static void Prepare(Window w)
    {
        w.WindowStyle = WindowStyle.None;
        w.AllowsTransparency = false;
        w.Background = Brushes.Transparent;
        WindowChrome.SetWindowChrome(w, new WindowChrome
        {
            CaptionHeight = 0, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
        });
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle;
            HwndSource.FromHwnd(h).CompositionTarget.BackgroundColor = Colors.Transparent;
            var m = new MARGINS { l = -1, r = -1, t = -1, b = -1 };
            DwmExtendFrameIntoClientArea(h, ref m);
            int round = 2; DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
            Apply(w, w.Tag as string == "glass", w.Tag as string != "light");
        };
    }

    public static Style ScrollStyle(bool light)
    {
        string thumb = light ? "#40000000" : "#45FFFFFF";
        string hover = light ? "#70000000" : "#80FFFFFF";
        var xaml = $@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
  <Setter Property='Width' Value='10'/>
  <Setter Property='MinWidth' Value='10'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent' Margin='0,6,3,6'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border x:Name='B' CornerRadius='3' Background='{thumb}' Width='5' HorizontalAlignment='Right'/>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='B' Property='Background' Value='{hover}'/>
                        <Setter TargetName='B' Property='Width' Value='7'/>
                      </Trigger>
                      <Trigger Property='IsDragging' Value='True'>
                        <Setter TargetName='B' Property='Background' Value='{hover}'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    static uint tint = 0x55201A1Au;

    public static void Apply(Window w, Core.Theme t)
    {
        var c = (Color)ColorConverter.ConvertFromString(t.Bg);
        bool light = t.Name == "Light";
        byte a = light ? (byte)0x88 : (byte)0x60;
        tint = (uint)(a << 24 | c.B << 16 | c.G << 8 | c.R);
        Apply(w, true, !light);
    }

    public static void Apply(Window w, bool glass, bool dark)
    {
        w.Tag = glass ? "glass" : dark ? "dark" : "light";
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        int d = dark ? 1 : 0; DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, 4);
        int backdrop = 1; DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4);
        var accent = new Core.Native.AccentPolicy { AccentState = glass ? 4 : 0, AccentFlags = 2, GradientColor = glass ? tint : 0u };
        int size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new Core.Native.WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = size };
            Core.Native.SetWindowCompositionAttribute(h, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
        int border = glass ? 0x00504A4A : unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref border, 4);
    }
}

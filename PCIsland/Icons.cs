using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PCIsland;

public static class Icons
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFO bmi, uint usage);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public int[] colors;
    }

    static readonly Dictionary<string, ImageSource> cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource For(string target, int px = 48)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        lock (cache) if (cache.TryGetValue(target, out var hit)) return hit;
        ImageSource img = null;
        try
        {
            var guid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(target, IntPtr.Zero, guid, out var factory);
            if (factory.GetImage(new SIZE { cx = px, cy = px }, 0x1 /* SIIGBF_RESIZETOFIT */, out var hbm) == 0 && hbm != IntPtr.Zero)
            {
                try { img = FromHBitmap(hbm, px); }
                finally { DeleteObject(hbm); }
            }
            Marshal.ReleaseComObject(factory);
        }
        catch { }
        lock (cache) cache[target] = img;
        return img;
    }

    static ImageSource FromHBitmap(IntPtr hbm, int px)
    {
        var bmi = new BITMAPINFO { biSize = 40, biWidth = px, biHeight = -px, biPlanes = 1, biBitCount = 32, colors = new int[4] };
        var bits = new byte[px * px * 4];
        var dc = GetDC(IntPtr.Zero);
        try { if (GetDIBits(dc, hbm, 0, (uint)px, bits, ref bmi, 0) == 0) return null; }
        finally { ReleaseDC(IntPtr.Zero, dc); }
        bool anyAlpha = false;
        for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;
        var bs = BitmapSource.Create(px, px, 96, 96, PixelFormats.Pbgra32, null, bits, px * 4);
        bs.Freeze();
        return bs;
    }
}

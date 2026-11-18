using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace PCIsland.Core;

public static class SystemTools
{
    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);

    public record Battery(int Percent, bool Charging, bool HasBattery, int MinutesLeft);

    public static Battery GetBattery()
    {
        GetSystemPowerStatus(out var s);
        bool has = s.BatteryFlag != 128 && s.BatteryFlag != 255;
        return new Battery(s.BatteryLifePercent > 100 ? 100 : s.BatteryLifePercent, s.ACLineStatus == 1, has, s.BatteryLifeTime > 0 ? s.BatteryLifeTime / 60 : -1);
    }

    public static (int percent, double usedGb, double totalGb) Ram()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        double total = m.ullTotalPhys / 1024.0 / 1024 / 1024, used = (m.ullTotalPhys - m.ullAvailPhys) / 1024.0 / 1024 / 1024;
        return ((int)m.dwMemoryLoad, used, total);
    }

    public static async Task<int> Cpu()
    {
        GetSystemTimes(out long i1, out long k1, out long u1);
        await Task.Delay(500);
        GetSystemTimes(out long i2, out long k2, out long u2);
        long idle = i2 - i1, total = (k2 - k1) + (u2 - u1);
        return total <= 0 ? 0 : (int)Math.Round(100.0 * (total - idle) / total);
    }

    public static async Task<string> SetRadio(RadioKind kind, bool on)
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) return "Windows didn't allow radio control";
            var radios = await Radio.GetRadiosAsync();
            var r = radios.FirstOrDefault(x => x.Kind == kind);
            if (r == null) return kind == RadioKind.Bluetooth ? "No Bluetooth found" : "No Wi-Fi found";
            var res = await r.SetStateAsync(on ? RadioState.On : RadioState.Off);
            return res == RadioAccessStatus.Allowed ? null : "Windows didn't allow it";
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static void SetDarkMode(bool dark)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        k.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        k.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x2, 1000, out _);
    }

    public static bool IsDarkMode()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    public static string Screenshot()
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bmp.Size);
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(dir, $"Screenshot {DateTime.Now:yyyy-MM-dd h.mm.ss tt}.png");
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }
}

using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace PCIsland.Core;

public static class Audio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float level, ref Guid ctx);
        int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint ch, float level, ref Guid ctx);
        int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        int GetChannelVolumeLevel(uint ch, out float level);
        int GetChannelVolumeLevelScalar(uint ch, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    static IAudioEndpointVolume Endpoint()
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        Marshal.ThrowExceptionForHR(en.GetDefaultAudioEndpoint(0, 1, out var dev));
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23, IntPtr.Zero, out var o));
        return (IAudioEndpointVolume)o;
    }

    public static int GetVolume()
    {
        Endpoint().GetMasterVolumeLevelScalar(out float v);
        return (int)Math.Round(v * 100);
    }

    public static bool GetMute()
    {
        Endpoint().GetMute(out bool m);
        return m;
    }

    public static int SetVolume(int percent)
    {
        var ep = Endpoint();
        var g = Guid.Empty;
        percent = Math.Clamp(percent, 0, 100);
        ep.SetMasterVolumeLevelScalar(percent / 100f, ref g);
        if (percent > 0) ep.SetMute(false, ref g);
        ep.GetMasterVolumeLevelScalar(out float v);
        return (int)Math.Round(v * 100);
    }

    public static void SetMute(bool mute)
    {
        var g = Guid.Empty;
        Endpoint().SetMute(mute, ref g);
    }

    public static int? GetBrightness()
    {
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\wmi"), new SelectQuery("WmiMonitorBrightness"));
            foreach (ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return null;
    }

    public static int? SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\wmi"), new SelectQuery("WmiMonitorBrightnessMethods"));
            bool any = false;
            foreach (ManagementObject o in s.Get())
            {
                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                any = true;
            }
            if (!any) return null;
            System.Threading.Thread.Sleep(120);
            return GetBrightness() ?? percent;
        }
        catch { return null; }
    }
}

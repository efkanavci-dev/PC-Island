using System;
using Microsoft.Win32;

namespace PCIsland.Core;

public static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "PC Island";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enabled) key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else if (key.GetValue(Name) != null) key.DeleteValue(Name);
        }
        catch { }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string s && s.Trim('"').Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

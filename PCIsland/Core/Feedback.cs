using System;
using System.Reflection;

namespace PCIsland.Core;

public static class Feedback
{
    const string FormUrl = "https://docs.google.com/forms/d/1mvhIRJjtBIvhmfhnkXCd8hd8Rzgjlta29GjooV65qzM/viewform";
    const string KindEntry = "entry.27637463";
    const string VersionEntry = "entry.381258167";
    const string WindowsEntry = "entry.1330048733";

    public const string Bug = "Bug (something doesn't work)";
    public const string Idea = "Idea (a new feature or change)";

    public static string AppVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version;
            return v == null ? "unknown" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static string WindowsVersion
    {
        get
        {
            var v = Environment.OSVersion.Version;
            string name = v.Major == 10 && v.Build >= 22000 ? "Windows 11" : v.Major == 10 ? "Windows 10" : $"Windows {v.Major}.{v.Minor}";
            return $"{name} (build {v.Build})";
        }
    }

    public static string Url(string kind = null)
    {
        var url = $"{FormUrl}?usp=pp_url&{VersionEntry}={Uri.EscapeDataString(AppVersion)}&{WindowsEntry}={Uri.EscapeDataString(WindowsVersion)}";
        if (!string.IsNullOrEmpty(kind)) url += $"&{KindEntry}={Uri.EscapeDataString(kind)}";
        return url;
    }
}

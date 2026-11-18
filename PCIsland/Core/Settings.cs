using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PCIsland.Core;

public record Theme(string Name, string[] Words, string Bg, string Fg, string Dim, string Acc, string Soft, string Danger, string Edge, bool Glass = false);

public static class Themes
{
    public static readonly Theme[] All =
    {
        new("Frosted Glass", new[]{"glass","frosted","cam","buzlu"},  "#661E1E24", "#FFFFFFFF", "#FFD0D0DA", "#FFFFFFFF", "#33FFFFFF", "#FFFF453A", "#40FFFFFF", true),
        new("Classic Black", new[]{"black","classic","siyah","klasik"}, "#B8000000", "#FFFFFFFF", "#FF9A9AA0", "#FF30D158", "#33FFFFFF", "#FFFF453A", "#30FFFFFF", true),
        new("Midnight",      new[]{"midnight","navy","gece","lacivert"}, "#A80B1530", "#FFE6F0FF", "#FF93A3CC", "#FF38D6FF", "#3338D6FF", "#FFFF4D6A", "#4038D6FF", true),
        new("Violet",        new[]{"violet","purple","mor"},          "#A81D1033", "#FFF3EAFF", "#FFB9A8D8", "#FFFF6AD5", "#33FF6AD5", "#FFFF4D6A", "#40FF6AD5", true),
        new("Ember",         new[]{"ember","orange","kor","turuncu"},  "#A817120F", "#FFFFF3EA", "#FFC0AE9F", "#FFFF7A1A", "#33FF7A1A", "#FFE5342B", "#40FF7A1A", true),
        new("Light",         new[]{"light","white","beyaz","acik"},   "#B8F7F7FA", "#FF111111", "#FF5F5F6A", "#FF0A84FF", "#22000000", "#FFFF3B30", "#80FFFFFF", true),
    };

    public static Theme ByName(string name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];

    public static string Solid(Theme t) => t.Name == "Frosted Glass" ? "#FF4A4A55" : "#FF" + t.Bg[3..];
    public static string WithAlpha(string hex, byte a) => "#" + a.ToString("X2") + hex[^6..];

    public static Theme FromWords(string text)
    {
        var words = Text.Clean(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in All)
            if (words.Any(w => t.Words.Contains(w) || Text.SuffixVariants(w).Any(t.Words.Contains))) return t;
        var (best, score) = Matcher.Best(All, text, t => t.Words.Prepend(t.Name));
        return score >= 75 ? best : null;
    }
}

public class Shortcut
{
    public string Name { get; set; } = "";
    public List<string> Commands { get; set; } = new();
}

public class Settings
{
    public string Theme { get; set; } = "Frosted Glass";
    public uint HotkeyMods { get; set; } = 0x0002;
    public uint HotkeyKey { get; set; } = 0x20;
    public bool StartWithWindows { get; set; } = true;
    public bool HoverOpen { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;
    public bool ShowNowPlaying { get; set; } = true;
    public bool StreamMode { get; set; } = false;
    public bool BatteryAlerts { get; set; } = true;
    public bool RobloxHighPriority { get; set; } = true;
    public bool AiEnabled { get; set; } = false;
    public string AiModel { get; set; } = "";
    public string AiGameMode { get; set; } = "off";
    public string AiWarmedFor { get; set; } = "";
    public List<string> BoostClose { get; set; } = new()
    {
        "SystemSettings", "PhoneExperienceHost", "CrossDeviceService", "XboxPcAppFT", "msedge", "GameBar"
    };
    public List<Game> Games { get; set; } = new();
    public List<string> ExtraAppFolders { get; set; } = new();
    public List<Shortcut> Shortcuts { get; set; } = new()
    {
        new Shortcut { Name = "gaming mode", Commands = new() { "close chrome", "close spotify", "open discord" } },
    };

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCIsland");
    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Settings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Opts) ?? new Settings();
        }
        catch { Current = new Settings(); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Opts));
        }
        catch { }
    }
}

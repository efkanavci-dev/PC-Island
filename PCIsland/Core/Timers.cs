using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCIsland.Core;

public class Reminder
{
    public DateTime At { get; set; }
    public string Text { get; set; } = "";
}

public static class Timers
{
    public static DateTime? TimerEnd;
    public static TimeSpan TimerLength;
    public static List<Reminder> Reminders = new();
    public static event Action<string, string> Fired;

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCIsland", "reminders.json");

    public static void Load()
    {
        try { if (File.Exists(FilePath)) Reminders = JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(FilePath)) ?? new(); } catch { }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Reminders));
        }
        catch { }
    }

    public static void Check()
    {
        var now = DateTime.Now;
        if (TimerEnd != null && now >= TimerEnd)
        {
            TimerEnd = null;
            Fired?.Invoke("Timer done", $"{Length(TimerLength)} is up");
        }
        var due = Reminders.Where(r => r.At <= now).ToList();
        if (due.Count > 0)
        {
            foreach (var r in due) Reminders.Remove(r);
            Save();
            foreach (var r in due)
            {
                bool late = (now - r.At).TotalMinutes > 2;
                Fired?.Invoke("Reminder", r.Text + (late ? $" (was for {r.At:h:mm tt})" : ""));
            }
        }
    }

    public static string Length(TimeSpan t)
    {
        var parts = new List<string>();
        if (t.Hours > 0 || t.Days > 0) parts.Add($"{(int)t.TotalHours} h");
        if (t.Minutes > 0) parts.Add($"{t.Minutes} min");
        if (t.Seconds > 0) parts.Add($"{t.Seconds} s");
        return parts.Count == 0 ? "0 s" : string.Join(" ", parts);
    }

    public static string Remaining()
    {
        if (TimerEnd == null) return null;
        var left = TimerEnd.Value - DateTime.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}" : $"{left.Minutes}:{left.Seconds:00}";
    }

    static readonly Regex DurPart = new(@"(\d+(?:[.,]\d+)?)\s*(hours?|hrs?|h|saat|minutes?|mins?|m|dakika|dk|seconds?|secs?|s|saniye|sn)\b", RegexOptions.IgnoreCase);

    public static TimeSpan? ParseDuration(string folded)
    {
        var ms = DurPart.Matches(folded);
        if (ms.Count == 0) return null;
        double secs = 0;
        foreach (Match m in ms)
        {
            double n = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var u = m.Groups[2].Value.ToLowerInvariant();
            if (u.StartsWith("h") || u == "saat") secs += n * 3600;
            else if (u.StartsWith("m") || u.StartsWith("dak") || u == "dk") secs += n * 60;
            else secs += n;
        }
        return secs > 0 ? TimeSpan.FromSeconds(Math.Min(secs, 7 * 24 * 3600)) : null;
    }

    public static DateTime? ParseClock(string folded)
    {
        var m = Regex.Match(folded, @"(?:\bat\s+|\bsaat\s+)?\b(\d{1,2})(?:[:.](\d{2}))?\s*(am|pm|a\.m\.|p\.m\.)?(?:'?(?:de|da|te|ta))?\b");
        foreach (Match x in Regex.Matches(folded, @"(?:\bat\s+|\bsaat\s+)(\d{1,2})(?:[:.](\d{2}))?\s*(am|pm)?|(\d{1,2})[:.](\d{2})\s*(am|pm)?|(\d{1,2})\s*(am|pm)\b"))
        {
            int h, min = 0; string ap;
            if (x.Groups[1].Success) { h = int.Parse(x.Groups[1].Value); if (x.Groups[2].Success) min = int.Parse(x.Groups[2].Value); ap = x.Groups[3].Value; }
            else if (x.Groups[4].Success) { h = int.Parse(x.Groups[4].Value); min = int.Parse(x.Groups[5].Value); ap = x.Groups[6].Value; }
            else { h = int.Parse(x.Groups[7].Value); ap = x.Groups[8].Value; }
            if (h > 23 || min > 59) continue;
            if (ap == "pm" && h < 12) h += 12;
            if (ap == "am" && h == 12) h = 0;
            var now = DateTime.Now;
            var t = now.Date.AddHours(h).AddMinutes(min);
            if (string.IsNullOrEmpty(ap) && h <= 12 && t <= now && t.AddHours(12) > now) t = t.AddHours(12);
            if (t <= now) t = t.AddDays(1);
            if (folded.Contains("tomorrow") || folded.Contains("yarin")) { if (t.Date == now.Date) t = t.AddDays(1); }
            return t;
        }
        return null;
    }

    public static Reply StartTimer(TimeSpan len)
    {
        TimerLength = len;
        TimerEnd = DateTime.Now + len;
        return new Message($"Timer set for {Length(len)}");
    }

    public static Reply AddReminder(DateTime at, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) text = "Reminder";
        Reminders.Add(new Reminder { At = at, Text = text.Trim() });
        Save();
        string when = at.Date == DateTime.Today ? at.ToString("h:mm tt", CultureInfo.InvariantCulture)
                    : at.Date == DateTime.Today.AddDays(1) ? "tomorrow " + at.ToString("h:mm tt", CultureInfo.InvariantCulture)
                    : at.ToString("MMM d h:mm tt", CultureInfo.InvariantCulture);
        return new Message($"I'll remind you at {when}: {text.Trim()}");
    }

    public static void ClearReminders() { Reminders.Clear(); Save(); }
    public static void Remove(Reminder r) { Reminders.Remove(r); Save(); }

    static readonly string[] ReminderNoise =
    {
        "remind me", "remind", "hatirlat", "bana", "to", "that", "in", "at", "sonra", "icinde", "saat", "tomorrow", "yarin", "olunca", "de", "da"
    };

    public static string ReminderText(string raw)
    {
        var s = raw;
        s = Regex.Replace(s, @"(?i)\bremind\s+me\b|\bremind\b|\bhat[ıi]rlat\w*|\bbana\b", " ");
        s = Regex.Replace(s, @"(?i)\b(in|at|saat|after)\s+(?=\d)", " ");
        s = Regex.Replace(s, @"(?i)\d+(?:[.,]\d+)?\s*(hours?|hrs?|h|saat|minutes?|mins?|m|dakika|dk|seconds?|secs?|s|saniye|sn)\b(\s*(sonra|i[cç]inde))?", " ");
        s = Regex.Replace(s, @"(?i)\b\d{1,2}([:.]\d{2})?\s*(am|pm)?('?(de|da|te|ta))?\b", " ");
        s = Regex.Replace(s, @"(?i)\b(tomorrow|yar[ıi]n)\b", " ");
        s = Regex.Replace(s, @"^\s*(to|that)\s+", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+", " ").Trim().Trim(',', ':', '-');
        s = Regex.Replace(s, @"^(to|that)\s+", "", RegexOptions.IgnoreCase);
        return s;
    }
}

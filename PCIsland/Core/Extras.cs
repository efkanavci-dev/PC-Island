using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PCIsland.Core;

public class Note
{
    public string Text { get; set; } = "";
    public DateTime At { get; set; }
}

public static class Notes
{
    public static List<Note> All = new();
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCIsland", "notes.json");

    public static void Load()
    {
        try { if (File.Exists(FilePath)) All = JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(FilePath)) ?? new(); } catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(All, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void Add(string text) { All.Add(new Note { Text = text.Trim(), At = DateTime.Now }); Save(); }
    public static void Remove(Note n) { All.Remove(n); Save(); }
}

public static class ClipHistory
{
    public static readonly List<string> Items = new();

    public static void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20000) return;
        Items.Remove(text);
        Items.Insert(0, text);
        if (Items.Count > 10) Items.RemoveAt(10);
    }
}

public class Game
{
    public string Name { get; set; } = "";
    public List<string> Words { get; set; } = new();
    public string Url { get; set; } = "";
}

public static class Games
{
    public static (Game game, int score) Find(string q)
    {
        Game best = null; int score = 0;
        foreach (var g in Settings.Current.Games)
            foreach (var w in g.Words.Prepend(g.Name))
            {
                if (Text.Clean(w) == Text.Clean(q) || Text.Compact(w) == Text.Compact(q)) return (g, 100);
            }
        return (best, score);
    }

    static readonly string[] Watched = { "RobloxPlayerBeta" };
    static readonly HashSet<int> boosted = new();

    public static int BoostRunningGames()
    {
        if (!Settings.Current.RobloxHighPriority) return 0;
        int n = 0;
        foreach (var name in Watched)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (!boosted.Contains(p.Id) || p.PriorityClass != ProcessPriorityClass.High)
                    {
                        p.PriorityClass = ProcessPriorityClass.High;
                        boosted.Add(p.Id);
                        n++;
                    }
                }
                catch { }
                p.Dispose();
            }
        return n;
    }

    public static List<RunningApp> BoostTargets()
    {
        var wanted = Settings.Current.BoostClose.Select(Text.Clean).Where(s => s.Length > 0).ToHashSet();
        return Closer.List().Where(a => wanted.Contains(Text.Clean(a.ProcessName)) || wanted.Contains(Text.Clean(a.Display))).ToList();
    }
}

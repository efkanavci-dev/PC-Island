using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PCIsland.Core;

public class LearnedData
{
    public Dictionary<string, string> Aliases { get; set; } = new();
    public Dictionary<string, int> Uses { get; set; } = new();
}

public static class Learner
{
    static LearnedData data = new();
    static readonly object gate = new();
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCIsland", "learned.json");

    static string currentPhrase;
    static string failedPhrase;
    static DateTime failedAt;

    public static string LastLearned;

    public static void Load()
    {
        try { if (File.Exists(FilePath)) data = JsonSerializer.Deserialize<LearnedData>(File.ReadAllText(FilePath)) ?? new(); } catch { data = new(); }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    static string Key(string phrase) => Text.Clean(phrase);

    public static Dictionary<string, string> Aliases { get { lock (gate) return new(data.Aliases); } }

    public static bool TryAlias(string phrase, out string alias)
    {
        lock (gate) return data.Aliases.TryGetValue(Key(phrase), out alias);
    }

    public static int Bonus(string name)
    {
        lock (gate)
        {
            if (!data.Uses.TryGetValue(Key(name), out int n) || n <= 0) return 0;
            return Math.Min(6, 1 + (int)Math.Log2(n + 1));
        }
    }

    public static void StartCommand(string phrase)
    {
        currentPhrase = Key(phrase);
        LastLearned = null;
    }

    public static void ClearFailed() => failedPhrase = null;

    public static void Failed(string phrase)
    {
        failedPhrase = Key(phrase);
        failedAt = DateTime.Now;
    }

    static string lastOpenPhrase, lastOpenName;
    static DateTime lastOpenAt;

    public static void Closed(string appName)
    {
    }

    public static void Opened(string targetName, string targetQuery, bool chosenFromList = false)
    {
        lock (gate)
        {
            var nameKey = Key(targetName);
            data.Uses[nameKey] = data.Uses.GetValueOrDefault(nameKey) + 1;

            if (chosenFromList && currentPhrase != null && currentPhrase.Length > 0 && currentPhrase != nameKey)
            {
                data.Aliases[currentPhrase] = targetQuery;
                LastLearned = $"{currentPhrase} = {targetName}";
            }
            if (currentPhrase != null) { lastOpenPhrase = currentPhrase; lastOpenName = targetName; lastOpenAt = DateTime.Now; }

            if (failedPhrase != null && (DateTime.Now - failedAt).TotalSeconds <= 40 && failedPhrase != nameKey && failedPhrase != currentPhrase)
            {
                data.Aliases[failedPhrase] = targetQuery;
                LastLearned = $"{failedPhrase} = {targetName}";
            }
            failedPhrase = null;
            currentPhrase = null;
            Save();
        }
    }

    public static void Teach(string phrase, string target)
    {
        lock (gate) { data.Aliases[Key(phrase)] = target.Trim(); Save(); }
    }

    public static bool Forget(string phrase)
    {
        lock (gate)
        {
            bool removed = data.Aliases.Remove(Key(phrase));
            if (removed) Save();
            return removed;
        }
    }

    public static void ForgetAll()
    {
        lock (gate) { data = new LearnedData(); Save(); }
    }
}

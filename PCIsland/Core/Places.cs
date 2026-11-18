using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PCIsland.Core;

public record Place(string Name, string Target, string[] Words);
public record FoundItem(string Path, bool IsFolder, int Score, int Depth);

public static class Places
{
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string Known(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

    public static List<Place> Folders => new()
    {
        new("Downloads", Path.Combine(Home, "Downloads"), new[] { "downloads", "indirilenler", "indirmeler" }),
        new("Desktop", Known(Environment.SpecialFolder.DesktopDirectory), new[] { "desktop", "masaustu" }),
        new("Documents", Known(Environment.SpecialFolder.MyDocuments), new[] { "documents", "docs", "belgeler", "belgelerim" }),
        new("Pictures", Known(Environment.SpecialFolder.MyPictures), new[] { "pictures", "photos folder", "resimler", "resimlerim" }),
        new("Music", Known(Environment.SpecialFolder.MyMusic), new[] { "music", "muzik", "muzikler" }),
        new("Videos", Known(Environment.SpecialFolder.MyVideos), new[] { "videos", "videolar", "videolarim" }),
        new("Home folder", Home, new[] { "home", "user folder", "kullanici klasoru", "ev" }),
        new("This PC", "shell:MyComputerFolder", new[] { "this pc", "my computer", "bu bilgisayar", "bilgisayarim" }),
        new("Recycle Bin", "shell:RecycleBinFolder", new[] { "recycle bin", "trash", "geri donusum kutusu", "cop kutusu", "cop" }),
        new("Startup folder", "shell:startup", new[] { "startup", "startup folder", "baslangic" }),
        new("Windows settings", "ms-settings:", new[] { "windows settings", "windows ayarlari", "pc settings" }),
        new("Display settings", "ms-settings:display", new[] { "display settings", "ekran ayarlari" }),
        new("Sound settings", "ms-settings:sound", new[] { "sound settings", "ses ayarlari" }),
        new("Wi-Fi settings", "ms-settings:network-wifi", new[] { "wifi", "wi fi", "wifi settings", "internet ayarlari" }),
        new("Bluetooth settings", "ms-settings:bluetooth", new[] { "bluetooth" }),
    };

    public static List<Place> Sites => new()
    {
        new("YouTube", "https://www.youtube.com", new[] { "youtube", "yt" }),
        new("Google", "https://www.google.com", new[] { "google" }),
        new("Gmail", "https://mail.google.com", new[] { "gmail", "mail", "email", "e posta" }),
        new("Twitch", "https://www.twitch.tv", new[] { "twitch" }),
        new("Netflix", "https://www.netflix.com", new[] { "netflix" }),
        new("GitHub", "https://github.com", new[] { "github" }),
        new("Reddit", "https://www.reddit.com", new[] { "reddit" }),
        new("X", "https://x.com", new[] { "twitter", "x com" }),
        new("Instagram", "https://www.instagram.com", new[] { "instagram", "insta" }),
        new("TikTok", "https://www.tiktok.com", new[] { "tiktok" }),
        new("ChatGPT", "https://chatgpt.com", new[] { "chatgpt" }),
        new("Claude", "https://claude.ai", new[] { "claude", "claude ai" }),
        new("Wikipedia", "https://www.wikipedia.org", new[] { "wikipedia", "vikipedi" }),
        new("WhatsApp Web", "https://web.whatsapp.com", new[] { "whatsapp", "whatsapp web" }),
        new("Google Translate", "https://translate.google.com", new[] { "translate", "ceviri", "cevir" }),
        new("Google Maps", "https://maps.google.com", new[] { "maps", "harita", "haritalar" }),
        new("Roblox website", "https://www.roblox.com", new[] { "roblox website", "roblox site" }),
        new("itch.io", "https://itch.io", new[] { "itch", "itch io" }),
        new("Amazon", "https://www.amazon.com.tr", new[] { "amazon" }),
        new("Trendyol", "https://www.trendyol.com", new[] { "trendyol" }),
        new("Kick", "https://kick.com", new[] { "kick" }),
    };

    public static (Place place, int score) FindFolder(string q) => Matcher.Best(Folders, q, p => p.Words.Prepend(p.Name));
    public static (Place place, int score) FindSite(string q) => Matcher.Best(Sites, q, p => p.Words.Prepend(p.Name));

    public static bool LooksLikeUrl(string raw, out string url)
    {
        url = null;
        var s = raw.Trim();
        if (s.Contains(' ')) return false;
        if (s.StartsWith("http://") || s.StartsWith("https://")) { url = s; return true; }
        var host = s.Split('/')[0];
        var parts = host.Split('.');
        if (parts.Length >= 2 && parts[^1].Length is >= 2 and <= 6 && parts[^1].All(char.IsLetter) && parts.All(p => p.Length > 0)
            && !new[] { "exe", "txt", "png", "jpg", "jpeg", "mp4", "mp3", "pdf", "zip", "rar", "docx", "xlsx", "pptx", "lnk", "html", "json", "py", "cs", "wav", "gif", "webp", "7z", "msi", "iso", "bat", "pyw", "mov", "mkv" }.Contains(parts[^1].ToLowerInvariant()))
        { url = "https://" + s; return true; }
        return false;
    }

    public static void Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "AppData", "node_modules", ".git", "bin", "obj", ".vs", "__pycache__", "$Recycle.Bin", ".vscode", ".nuget",
        "packages", "venv", ".venv", "site-packages", "Application Data", "Local Settings", "OneDrive", ".cache", "dist-newstyle", "target"
    };

    public static IEnumerable<(string root, int depth)> SearchRoots()
    {
        var desk = Known(Environment.SpecialFolder.DesktopDirectory);
        yield return (desk, 4);
        yield return (Path.Combine(Home, "Downloads"), 3);
        yield return (Known(Environment.SpecialFolder.MyDocuments), 4);
        yield return (Known(Environment.SpecialFolder.MyPictures), 3);
        yield return (Known(Environment.SpecialFolder.MyVideos), 3);
        yield return (Known(Environment.SpecialFolder.MyMusic), 3);
        yield return (Path.Combine(Home, "source", "repos"), 3);
        yield return (Home, 2);
    }

    public static List<FoundItem> FindFiles(string query, Scope scope, int minScore = 80, int timeoutMs = 2500)
    {
        var results = new Dictionary<string, FoundItem>(StringComparer.OrdinalIgnoreCase);
        var sw = Stopwatch.StartNew();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string qClean = Text.Clean(query);
        bool hasExt = Path.HasExtension(query.Trim()) && !query.Trim().EndsWith(".");

        foreach (var (root, maxDepth) in SearchRoots())
        {
            if (!Directory.Exists(root)) continue;
            var stack = new Stack<(string dir, int depth)>();
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                if (sw.ElapsedMilliseconds > timeoutMs) goto done;
                var (dir, depth) = stack.Pop();
                if (!visited.Add(dir)) continue;
                IEnumerable<string> entries;
                try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); } catch { continue; }
                foreach (var e in entries)
                {
                    bool isDir;
                    try
                    {
                        var attr = File.GetAttributes(e);
                        if ((attr & FileAttributes.Hidden) != 0 || (attr & FileAttributes.System) != 0) continue;
                        isDir = (attr & FileAttributes.Directory) != 0;
                        if (isDir && (attr & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }
                    var name = Path.GetFileName(e);
                    if (isDir && (SkipDirs.Contains(name) || name.StartsWith("."))) continue;

                    if ((scope == Scope.Any) || (scope == Scope.Folder && isDir) || (scope == Scope.File && !isDir))
                    {
                        int s = hasExt || isDir ? Matcher.Score(query, name) : Math.Max(Matcher.Score(query, Path.GetFileNameWithoutExtension(name)), Matcher.Score(query, name) - 2);
                        if (Text.Clean(name) == qClean || Text.Clean(Path.GetFileNameWithoutExtension(name)) == qClean) s = Math.Max(s, 99);
                        if (s >= minScore && !results.ContainsKey(e)) results[e] = new FoundItem(e, isDir, s, depth);
                    }
                    if (isDir && depth + 1 < maxDepth) stack.Push((e, depth + 1));
                }
            }
        }
    done:
        return results.Values
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Depth)
            .ThenByDescending(r => r.IsFolder && scope != Scope.File)
            .ToList();
    }

    public static bool IsDangerous(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\');
            if (Path.GetPathRoot(full)?.TrimEnd('\\') == full) return true;
            var bad = new[]
            {
                Home, Known(Environment.SpecialFolder.Windows), Known(Environment.SpecialFolder.ProgramFiles),
                Known(Environment.SpecialFolder.ProgramFilesX86), Known(Environment.SpecialFolder.DesktopDirectory),
                Known(Environment.SpecialFolder.MyDocuments), Path.Combine(Home, "Downloads"), Known(Environment.SpecialFolder.MyPictures),
                Known(Environment.SpecialFolder.MyVideos), Known(Environment.SpecialFolder.MyMusic),
                Known(Environment.SpecialFolder.ApplicationData), Known(Environment.SpecialFolder.LocalApplicationData),
                Path.Combine(Home, "source"), Path.Combine(Home, "source", "repos"), Path.Combine(Home, ".claude"), @"C:\Users"
            };
            if (bad.Any(b => string.Equals(b.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))) return true;
            if (full.StartsWith(Known(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) return true;
            if (full.StartsWith(Known(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)) return true;
            if (full.StartsWith(Known(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch { return true; }
    }

    public static (int files, long bytes) FolderSize(string dir, int limitMs = 1500)
    {
        int files = 0; long bytes = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                files++;
                try { bytes += new FileInfo(f).Length; } catch { }
                if (sw.ElapsedMilliseconds > limitMs) return (-files, bytes);
            }
        }
        catch { }
        return (files, bytes);
    }

    public static string Size(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{b / 1024.0 / 1024:0.#} MB",
        _ => $"{b / 1024.0 / 1024 / 1024:0.##} GB"
    };

    public static string Short(string path)
    {
        var h = Home;
        if (path.StartsWith(h, StringComparison.OrdinalIgnoreCase)) path = "~" + path[h.Length..];
        return path;
    }

    public static string DeleteForever(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var f in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
                    try { File.SetAttributes(f, File.GetAttributes(f) & ~FileAttributes.ReadOnly); } catch { }
                Directory.Delete(path, true);
            }
            else if (File.Exists(path))
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                File.Delete(path);
            }
            else return "It's already gone";
            return null;
        }
        catch (IOException) { return "It's open in another app. Close it and try again"; }
        catch (UnauthorizedAccessException) { return "Windows won't allow it (needs admin)"; }
        catch (Exception ex) { return ex.Message; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PCIsland.Core;

public abstract record Reply;
public record Message(string Text, bool Ok = true) : Reply;
public record Confirm(string Title, string Sub, string YesLabel, bool Danger, Func<Task<Reply>> OnYes) : Reply;
public record Choice(string Label, string Sub, Func<Task<Reply>> Run, Func<Task<Reply>> Delete = null);
public record Answer(string Text, string Copy) : Reply;
public record Shot(bool Area) : Reply;
public record ShowMusic : Reply;
public record OpenGuide : Reply;
public record Pick(string Title, List<Choice> Choices) : Reply;
public record Silent : Reply;
public record Deferred(Func<Task<Reply>> Run) : Reply;
public record Level(string Label, int Value, string Kind) : Reply;
public record OpenSettings : Reply;
public record ThemeChanged(Theme Theme) : Reply;

public static partial class Brain
{
    static Reply VolumeHandler(int? amount, int step)
    {
        try
        {
            int now = Audio.GetVolume();
            if (amount == null && step == 0)
                return new Level(Audio.GetMute() ? "Muted" : "Volume", Audio.GetMute() ? 0 : now, "volume");
            int v = Audio.SetVolume(amount ?? now + step);
            return new Level("Volume", v, "volume");
        }
        catch (Exception ex) { return new Message("Couldn't change volume: " + ex.Message, false); }
    }

    static Reply MuteHandler(bool mute)
    {
        try
        {
            Audio.SetMute(mute);
            return mute ? new Level("Muted", 0, "volume") : new Level("Volume", Audio.GetVolume(), "volume");
        }
        catch (Exception ex) { return new Message("Couldn't change mute: " + ex.Message, false); }
    }

    static Reply BrightnessHandler(int? amount, int step)
    {
        var now = Audio.GetBrightness();
        if (now == null) return new Message("Brightness only works on the laptop screen", false);
        if (amount == null && step == 0) return new Level("Brightness", now.Value, "brightness");
        var v = Audio.SetBrightness(amount ?? now.Value + step);
        return v == null ? new Message("Couldn't change brightness", false) : new Level("Brightness", v.Value, "brightness");
    }

    public static async Task<Reply> Handle(string input, bool insideShortcut = false)
    {
        if (!insideShortcut && !noAi.Value) currentInput.Value = input;
        if (!insideShortcut && !noAi.Value && Ai.Ready && Ai.Cached(input) is { } cachedCmds && cachedCmds.Count > 0)
            return await AskAi(input, null);
        if (!insideShortcut && Parser.Parse(input).Kind != Kind.Search)
        {
            var teach = System.Text.RegularExpressions.Regex.Match(input.Trim(), @"^(.+?)\s+(?:means|=|demek|yani)\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (teach.Success)
            {
                var phrase = teach.Groups[1].Value.Trim().Trim('"');
                var target = teach.Groups[2].Value.Trim().Trim('"');
                Learner.Teach(phrase, target);
                return new Message($"Got it: \"{phrase}\" opens {target}");
            }
            var forget = System.Text.RegularExpressions.Regex.Match(Text.Fold(input.Trim()), @"^(?:forget|unut)\s+(.+)$|^(.+?)\s+(?:unut|'?u unut|'?i unut)$");
            if (forget.Success)
            {
                var phrase = (forget.Groups[1].Success ? forget.Groups[1].Value : forget.Groups[2].Value).Trim().Trim('"');
                if (phrase is "everything" or "all" or "her seyi" or "hepsini")
                    return new Confirm("Forget everything it learned?", "Your words and favorites reset. Settings stay.", "Forget", true, () =>
                    {
                        Learner.ForgetAll();
                        return Task.FromResult<Reply>(new Message("Forgot everything"));
                    });
                return Learner.Forget(phrase) ? new Message($"Forgot \"{phrase}\"") : new Message($"I hadn't learned \"{phrase}\"", false);
            }
        }

        var extra = await TryExtras(input, insideShortcut);
        if (extra != null) { Learner.ClearFailed(); return extra; }

        if (!insideShortcut && !noAi.Value && Ai.Ready && Ai.IsQuestion(input))
            return Ai.AboutThisPc(input) ? new Deferred(() => AskAi(input, null)) : new Deferred(() => AnswerQuestion(input));

        var intent = Parser.Parse(input);
        if (intent.Kind != Kind.Open && !insideShortcut) Learner.ClearFailed();
        switch (intent.Kind)
        {
            case Kind.Empty: return new Silent();
            case Kind.Help:
                return new Message(string.Join(" · ", new[] { "open X", "close X", "close tab(s)", "delete X", "search X", "volume 50", "mute", "brightness 70",
                    "pause", "next", "15*24", "timer 10 min", "remind me at 9 pm to X", "battery", "ram", "wifi off", "dark mode",
                    "screenshot", "note X", "notes", "clipboard", "translate X to turkish", "disk", "minimize all", "switch to X", "X left", "boost", "play blox fruits",
                    "theme glass", "settings", "sleep", "lock", "restart", "shutdown", "X means Y", "forget X", "guide = full guide" }
                    .Select(s => s.Replace(' ', ' '))));
            case Kind.Settings: return new OpenSettings();
            case Kind.Theme:
            {
                var t = Themes.FromWords(intent.Target);
                if (t == null) return new Message("Themes: black, glass, midnight, violet, ember, light", false);
                return new ThemeChanged(t);
            }
            case Kind.Search:
                if (intent.RawTarget.Length == 0) return new Message("Search for what?", false);
                if (Places.LooksLikeUrl(intent.RawTarget.Trim(), out var siteUrl))
                {
                    Places.Open(siteUrl);
                    return new Message($"Opening {intent.RawTarget.Trim()}");
                }
                Places.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(intent.RawTarget));
                return new Message($"Searching \"{intent.RawTarget}\"");
            case Kind.Volume: return VolumeHandler(intent.Amount, intent.Step);
            case Kind.Mute: return MuteHandler(true);
            case Kind.Unmute: return MuteHandler(false);
            case Kind.Brightness: return await Task.Run(() => BrightnessHandler(intent.Amount, intent.Step));
            case Kind.Shutdown: return Power("Shut down", "Shut down the PC?", () => Run("shutdown", "/s /t 0"));
            case Kind.Restart: return Power("Restart", "Restart the PC?", () => Run("shutdown", "/r /t 0"));
            case Kind.Sleep: return Power("Sleep", "Put the PC to sleep?", () => Native.SetSuspendState(false, false, false));
            case Kind.Lock: Native.LockWorkStation(); return new Message("Locked");
            case Kind.Tabs: return await Task.Run(() => CloseTabs(intent, insideShortcut));
            case Kind.Close: return await Task.Run(() => CloseApp(intent, insideShortcut));
            case Kind.Delete: return await Task.Run(() => DeleteThing(intent));
            case Kind.Open: return await Task.Run(() => OpenThing(intent, insideShortcut));
        }
        return new Message("I didn't get that", false);
    }

    static void Run(string exe, string args) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });

    static Reply Power(string yes, string title, Action act) =>
        new Confirm(title, "Save your work first.", yes, true, () => { act(); return Task.FromResult<Reply>(new Message(yes + "...")); });

    static string Nice(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    static async Task<Reply> RunShortcut(Shortcut sc)
    {
        var errors = new List<string>();
        foreach (var cmd in sc.Commands.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            var r = await Handle(cmd, insideShortcut: true);
            if (r is Confirm c) r = await c.OnYes();
            if (r is Pick p && p.Choices.Count > 0) r = await p.Choices[0].Run();
            if (r is Message m && !m.Ok) errors.Add(m.Text);
            await Task.Delay(150);
        }
        return errors.Count == 0 ? new Message($"{Nice(sc.Name)} done") : new Message($"{Nice(sc.Name)}: {errors[0]}", false);
    }

    static Reply Learned(Reply r)
    {
        if (r is Message m && m.Ok && Learner.LastLearned != null)
        {
            var l = Learner.LastLearned;
            Learner.LastLearned = null;
            return new Message($"{m.Text} · learned {l}");
        }
        return r;
    }

    public static Reply LaunchGame(Game g)
    {
        try
        {
            Places.Open(g.Url);
            if (g.Url.StartsWith("roblox:"))
            {
                var (rp, s) = AppIndex.Find("roblox player");
                if (rp != null && s >= 90) LastIcon = rp.IsShellApp ? "shell:AppsFolder\\" + rp.Launch : rp.Launch;
            }
            return new Message($"Starting {g.Name}");
        }
        catch (Exception ex) { return new Message("Couldn't start the game: " + ex.Message, false); }
    }

    static Reply OpenPlace(string name, string target, string learnAs)
    {
        try
        {
            Places.Open(target); Learner.Opened(name, learnAs);
            if (Directory.Exists(target) || File.Exists(target) || target.StartsWith("shell:")) LastIcon = target;
            return new Message($"Opening {name}");
        }
        catch (Exception ex) { return new Message(ex.Message, false); }
    }

    static Reply OpenThing(Intent intent, bool insideShortcut)
    {
        if (!insideShortcut) Learner.StartCommand(intent.Target);
        if (Learner.TryAlias(intent.Target, out var alias) && Text.Clean(alias) != Text.Clean(intent.Target))
            intent = intent with { Target = Text.Fold(alias), RawTarget = alias };
        var q = intent.Target;
        var raw = intent.RawTarget;
        if (q.Length == 0) return new Message("Open what?", false);

        if (!insideShortcut)
        {
            var sc = Settings.Current.Shortcuts.FirstOrDefault(s => Text.Clean(s.Name) == Text.Clean(q));
            if (sc != null)
            {
                bool risky = sc.Commands.Any(c => Parser.Parse(c).Kind is Kind.Close or Kind.Delete or Kind.Shutdown or Kind.Restart or Kind.Sleep);
                string list = string.Join(", ", sc.Commands.Take(4)) + (sc.Commands.Count > 4 ? "..." : "");
                if (risky) return new Confirm($"Run {sc.Name}?", list, "Run", false, () => RunShortcut(sc));
                return new Deferred(() => RunShortcut(sc));
            }
        }

        if ((raw.Length > 2 && raw[1] == ':' ) || raw.StartsWith("\\\\") || raw.StartsWith("~"))
        {
            var p = raw.StartsWith("~") ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + raw[1..] : raw;
            if (File.Exists(p) || Directory.Exists(p)) return Learned(OpenPlace(Path.GetFileName(p.TrimEnd('\\')), p, p));
        }

        if (intent.Scope == Scope.Any && Places.LooksLikeUrl(raw, out var url))
            return Learned(OpenPlace(raw, url, url));

        if (intent.Scope == Scope.Any)
        {
            var (game, gs) = Games.Find(q);
            if (game != null && gs >= 100) return LaunchGame(game);
        }

        var (folder, fs) = Places.FindFolder(q);
        if (folder != null && fs >= 90) return Learned(OpenPlace(folder.Name, folder.Target, folder.Words[0]));

        AppEntry app = null; int appScore = 0;
        if (intent.Scope == Scope.Any)
        {
            (app, appScore) = AppIndex.Find(q);
            var compact = Text.Compact(q);
            if (!insideShortcut && appScore < 90 && compact.Length is >= 2 and <= 6 && !q.Contains(' ') && compact.All(char.IsLetter))
            {
                var rg = Roblox.Search(q).GetAwaiter().GetResult();
                if (rg != null && Roblox.Initials(rg.Name) == compact) return LaunchGame(Roblox.Remember(rg, q));
            }
            if (app != null && appScore >= 80) return Learned(Launch(app));
        }

        var (site, ss) = Places.FindSite(q);
        if (intent.Scope == Scope.Any && site != null && ss >= 85) return Learned(OpenPlace(site.Name, site.Target, site.Target));

        var files = Places.FindFiles(raw, intent.Scope, minScore: 80);
        if (files.Count > 0)
        {
            var top = files[0];
            if (files.Count == 1 || top.Score >= 99 && files[1].Score < 99 || top.Score - files[1].Score >= 8)
                return Learned(OpenFound(top));
            if (insideShortcut) return OpenFound(top);
            return new Pick($"Which {raw}?", files.Take(4).Select(f =>
                new Choice(Path.GetFileName(f.Path), Places.Short(Path.GetDirectoryName(f.Path)), () => Task.FromResult(Learned(OpenFound(f, true))))).ToList());
        }

        bool sentence = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4 && AiAllowedNow && !insideShortcut;
        if (!sentence)
        {
            if (app != null && appScore >= 68) return Learned(Launch(app));
            if (folder != null && fs >= 75) return Learned(OpenPlace(folder.Name, folder.Target, folder.Words[0]));
            if (site != null && ss >= 75 && intent.Scope == Scope.Any) return Learned(OpenPlace(site.Name, site.Target, site.Target));
        }

        if (intent.Scope == Scope.Any && q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5)
        {
            var rg = Roblox.Search(raw).GetAwaiter().GetResult();
            if (rg != null && Roblox.Matches(raw, rg.Name)) return LaunchGame(Roblox.Remember(rg, q));
        }

        if (insideShortcut) return new Message($"Couldn't find {raw}", false);
        Learner.Failed(intent.Target);
        return AiOr(new Confirm($"Couldn't find \"{raw}\"", "Search the web for it instead?", "Search", false, () =>
        {
            Learner.ClearFailed();
            Places.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(raw));
            return Task.FromResult<Reply>(new Message($"Searching \"{raw}\""));
        }));
    }

    static Reply Launch(AppEntry app)
    {
        try
        {
            AppIndex.Launch(app); Learner.Opened(app.Name, app.Name);
            LastIcon = app.IsShellApp ? "shell:AppsFolder\\" + app.Launch : app.Launch;
            return new Message($"Opening {app.Name}");
        }
        catch (Exception ex) { return new Message($"Couldn't open {app.Name}: {ex.Message}", false); }
    }

    static Reply OpenFound(FoundItem f, bool chosen = false)
    {
        try { Places.Open(f.Path); Learner.Opened(Path.GetFileName(f.Path), f.Path, chosen); LastIcon = f.Path; return new Message($"Opening {Path.GetFileName(f.Path)}"); }
        catch (Exception ex) { return new Message(ex.Message, false); }
    }

    static Reply CloseApp(Intent intent, bool insideShortcut)
    {
        var q = intent.Target;
        if (q.Length == 0) return new Message("Close what?", false);
        if (Learner.TryAlias(q, out var alias) && !alias.Contains('\\') && !alias.Contains("://")) q = Text.Fold(alias);
        var (app, score) = Closer.Find(q);
        if (app == null || score < 70)
            return insideShortcut ? new Message("", true)
                 : q.Contains(' ') ? AiOr(new Message($"{Nice(intent.RawTarget)} isn't running", false))
                 : new Message($"{Nice(intent.RawTarget)} isn't running", false);

        Func<Task<Reply>> doClose = async () =>
        {
            var err = await Closer.Close(app);
            if (err == null) Learner.Closed(app.Display);
            return err == null ? new Message($"Closed {app.Display}") : new Message(err, false);
        };
        if (insideShortcut) return new Confirm("", "", "", false, doClose);
        int n = app.Ids.Count;
        string sub = app.HasWindow ? "Unsaved work in it may be lost." : "It's running in the background.";
        return new Confirm($"Close {app.Display}?", sub, "Close", true, doClose);
    }

    static Reply CloseTabs(Intent intent, bool insideShortcut)
    {
        var w = Tabs.Pick(intent.Target);
        if (w == null)
            return new Message(intent.Target.Length > 0 ? $"{Nice(intent.Target)} isn't open" : "No browser is open", false);
        int n = intent.Amount ?? 1;
        if (n > 0)
            return new Deferred(async () =>
            {
                var err = await Tabs.CloseOne(w, n);
                if (err != null) return new Message(err, false);
                return new Message(n == 1 ? $"Closed a tab in {w.Display}" : $"Closed {n} tabs in {w.Display}");
            });

        Func<Task<Reply>> all = async () =>
        {
            var (closed, err) = await Tabs.CloseAll(w);
            if (err != null) return new Message(err, false);
            return new Message(closed == 0 ? $"{w.Display} had no other tabs" : $"Closed {closed} tab{(closed == 1 ? "" : "s")} in {w.Display}");
        };
        if (insideShortcut) return new Deferred(all);
        return new Confirm($"Close all tabs in {w.Display}?", $"{w.Display} stays open with one new tab. Ctrl+Shift+T brings tabs back.", "Close tabs", true, all);
    }

    static Reply DeleteThing(Intent intent)
    {
        var raw = intent.RawTarget;
        if (raw.Length == 0) return new Message("Delete what?", false);

        List<FoundItem> found;
        string direct = raw.StartsWith("~") ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + raw[1..] : raw;
        if ((direct.Length > 2 && direct[1] == ':') && (File.Exists(direct) || Directory.Exists(direct)))
            found = new() { new FoundItem(direct, Directory.Exists(direct), 100, 0) };
        else
            found = Places.FindFiles(raw, intent.Scope, minScore: 90);

        found = found.Where(f => !Places.IsDangerous(f.Path)).ToList();
        if (found.Count == 0) return new Message($"Couldn't find \"{raw}\"", false);

        var exact = found.Where(f => f.Score >= 99).ToList();
        if (exact.Count == 1) return AskDelete(exact[0]);
        if (exact.Count == 0 && found.Count == 1) return AskDelete(found[0]);

        var list = (exact.Count > 1 ? exact : found).Take(4).ToList();
        return new Pick($"Which one to delete?", list.Select(f =>
            new Choice(Path.GetFileName(f.Path) + (f.IsFolder ? "  (folder)" : ""), Places.Short(Path.GetDirectoryName(f.Path)),
                () => Task.FromResult(AskDelete(f)))).ToList());
    }

    static Reply AskDelete(FoundItem f)
    {
        string name = Path.GetFileName(f.Path);
        string sub;
        if (f.IsFolder)
        {
            var (n, bytes) = Places.FolderSize(f.Path);
            sub = n < 0 ? $"Folder with {-n}+ files. This can't be undone." :
                  n == 0 ? "Empty folder. This can't be undone." :
                  $"Folder with {n} file{(n == 1 ? "" : "s")} ({Places.Size(bytes)}). This can't be undone.";
        }
        else
        {
            long len = 0; try { len = new FileInfo(f.Path).Length; } catch { }
            sub = $"{Places.Short(Path.GetDirectoryName(f.Path))} · {Places.Size(len)}. This can't be undone.";
        }
        return new Confirm($"Permanently delete \"{name}\"?", sub, "Delete", true, () =>
        {
            var err = Places.DeleteForever(f.Path);
            return Task.FromResult<Reply>(err == null ? new Message($"Deleted {name}") : new Message(err, false));
        });
    }
}

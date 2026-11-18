using System;
using System.Linq;
using PCIsland.Core;

int pass = 0, fail = 0;
void Check(string input, Kind kind, string target = null, int? amount = null, int? step = null)
{
    var i = Parser.Parse(input);
    bool ok = i.Kind == kind && (target == null || i.Target == target) && (amount == null || i.Amount == amount) && (step == null || i.Step == step);
    if (ok) pass++; else { fail++; Console.WriteLine($"FAIL parse '{input}' -> {i}  (want {kind} '{target}')"); }
}

Check("open discord", Kind.Open, "discord");
Check("discord", Kind.Open, "discord");
Check("discord'u aç", Kind.Open, "discord");
Check("discordu aç", Kind.Open, "discordu");
Check("Spotify'ı aç", Kind.Open, "spotify");
Check("close chrome", Kind.Close, "chrome");
Check("chrome'u kapat", Kind.Close, "chrome");
Check("spotifyı kapat", Kind.Close, "spotifyi");
Check("delete old_video.mp4", Kind.Delete, "old_video.mp4");
Check("old_video.mp4'ü sil", Kind.Delete, "old_video.mp4");
Check("search how to fix wifi", Kind.Search, "how to fix wifi");
Check("search for cats", Kind.Search, "cats");
Check("kedi videoları ara", Kind.Search);
Check("google minecraft seeds", Kind.Search);
Check("volume 50", Kind.Volume, amount: 50);
Check("ses 30", Kind.Volume, amount: 30);
Check("sesi aç", Kind.Volume, step: 10);
Check("sesi kıs", Kind.Volume, step: -10);
Check("volume up", Kind.Volume, step: 10);
Check("mute", Kind.Mute);
Check("sesi kapat", Kind.Mute);
Check("unmute", Kind.Unmute);
Check("brightness 70", Kind.Brightness, amount: 70);
Check("parlaklığı azalt", Kind.Brightness, step: -10);
Check("parlaklık 40", Kind.Brightness, amount: 40);
Check("shutdown", Kind.Shutdown);
Check("bilgisayarı kapat", Kind.Shutdown);
Check("pc'yi kapat", Kind.Shutdown);
Check("restart", Kind.Restart);
Check("yeniden başlat", Kind.Restart);
Check("sleep", Kind.Sleep);
Check("uyku modu", Kind.Sleep);
Check("lock", Kind.Lock);
Check("kilitle", Kind.Lock);
Check("theme midnight", Kind.Theme, "midnight");
Check("tema mor", Kind.Theme, "mor");
Check("settings", Kind.Settings);
Check("ayarlar", Kind.Settings);
Check("open windows settings", Kind.Open, "windows settings");
Check("open school folder", Kind.Open, "school");
Check("okul klasörünü aç", Kind.Open, "okul");
Check("indirilenleri aç", Kind.Open, "indirilenleri");
Check("open youtube", Kind.Open, "youtube");
Check("gaming mode", Kind.Open, "gaming mode");
Check("help", Kind.Help);
Check("start roblox", Kind.Open, "roblox");
Check("kill discord", Kind.Close, "discord");
Check("close tabs on chrome", Kind.Tabs, "chrome", amount: -1);
Check("close tab", Kind.Tabs, "", amount: 1);
Check("close tab on edge", Kind.Tabs, "edge", amount: 1);
Check("close all chrome tabs", Kind.Tabs, "chrome", amount: -1);
Check("close 3 tabs", Kind.Tabs, "", amount: 3);
Check("chromedaki sekmeleri kapat", Kind.Tabs, "chrome", amount: -1);
Check("edgenin sekmelerini kapat", Kind.Tabs, "edge", amount: -1);
Check("sekmeyi kapat", Kind.Tabs, "", amount: 1);
Check("chrome'daki tüm sekmeleri kapat", Kind.Tabs, "chrome", amount: -1);
Check("close edge", Kind.Close, "edge");
Check("open tablet mode", Kind.Open);

void Score(string q, string c, int min)
{
    int s = Matcher.Score(q, c);
    if (s >= min) pass++; else { fail++; Console.WriteLine($"FAIL score '{q}' vs '{c}' = {s} (want >= {min})"); }
}
void Low(string q, string c, int max)
{
    int s = Matcher.Score(q, c);
    if (s <= max) pass++; else { fail++; Console.WriteLine($"FAIL low '{q}' vs '{c}' = {s} (want <= {max})"); }
}
Score("discord", "Discord", 100);
Score("discrod", "Discord", 68);
Score("disc", "Discord", 80);
Score("spotifyi", "Spotify", 68);
Score("discordu", "Discord", 80);
Score("indirilenleri", "indirilenler", 80);
Score("vsc", "Visual Studio Code", 80);
Score("steam", "Steam", 100);
Score("calc", "Calculator", 80);
Low("chrome", "Discord", 50);
Low("note", "OneNote", 79);
Low("spotify", "Steam", 50);

Console.WriteLine($"\nparser/matcher: {pass} passed, {fail} failed");

if (args.Contains("--apps"))
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    AppIndex.Load();
    Console.WriteLine($"apps indexed: {AppIndex.Count} in {sw.ElapsedMilliseconds} ms");
    foreach (var q in new[] { "discord", "chrome", "roblox", "spotify", "vs code", "notepad", "steam", "edge", "calculator", "task manager", "settings", "terminal", "explorer", "discrod", "hesap makinesi", "not defteri", "gorev yoneticisi", "firefox", "paint", "word" })
    {
        var (a, s) = AppIndex.Find(q);
        Console.WriteLine($"  {q,-18} -> {(a == null ? "-" : a.Name),-40} {s}");
    }
}
if (args.Contains("--files"))
{
    foreach (var q in new[] { "documents", "downloads", "PCIsland" })
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = Places.FindFiles(q, Scope.Any);
        Console.WriteLine($"  {q} ({sw.ElapsedMilliseconds} ms): " + string.Join(" | ", r.Take(3).Select(f => $"{f.Path} [{f.Score}]")));
    }
}
if (args.Contains("--running"))
{
    foreach (var a in Closer.List().OrderBy(a => a.Display)) Console.WriteLine($"  {a.Display} ({a.ProcessName}) x{a.Ids.Count} win={a.HasWindow}");
}
if (args.Contains("--tabs-title"))
{
    var part = args[Array.IndexOf(args, "--tabs-title") + 1];
    var all = Tabs.FindWindows();
    foreach (var w0 in all) Console.WriteLine($"  window {w0.Handle} {w0.Display}");
    var tw = all.FirstOrDefault(w =>
    {
        var sb = new System.Text.StringBuilder(256);
        GetWindowTextW(w.Handle, sb, 256);
        return sb.ToString().StartsWith(part);
    });
    if (tw == null) { Console.WriteLine("no window with that title"); return 1; }
    var (c2, e2) = await Tabs.CloseAll(tw);
    Console.WriteLine($"closed={c2} err={e2}");
}
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder sb, int max);
if (args.Contains("--tabs"))
{
    var exe = args[Array.IndexOf(args, "--tabs") + 1];
    var dir = args[Array.IndexOf(args, "--tabs") + 2];
    var before = Tabs.FindWindows().Select(w => w.Handle).ToHashSet();
    var urls = string.Join(" ", Enumerable.Range(1, 4).Select(i => $"\"data:text/html,<title>T{i}</title>\""));
    System.Diagnostics.Process.Start(exe, $"--user-data-dir=\"{dir}\" --no-first-run --no-default-browser-check --new-window {urls}");
    Tabs.BrowserWindow test = null;
    for (int i = 0; i < 60 && test == null; i++)
    {
        System.Threading.Thread.Sleep(250);
        test = Tabs.FindWindows().FirstOrDefault(w => !before.Contains(w.Handle));
    }
    if (test == null) { Console.WriteLine("test window didn't appear"); return 1; }
    System.Threading.Thread.Sleep(2500);
    Console.WriteLine($"test window: {test.Display} {test.Handle}");
    var (closed, err) = await Tabs.CloseAll(test);
    Console.WriteLine($"closed={closed} err={err}");
    System.Threading.Thread.Sleep(800);
    var sb = new System.Text.StringBuilder(256);
    Console.WriteLine($"window still open: {Native.IsWindowVisible(test.Handle)}");
}
if (args.Contains("--ai-download"))
{
    Console.WriteLine($"gpu: {AiModels.Gpu()} best: {AiModels.BestForThisPc().Name}");
    foreach (var m in AiModels.All)
    {
        if (AiModels.IsDownloaded(m)) { Console.WriteLine($"{m.Name}: already downloaded"); continue; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var job = AiModels.Download(m);
        int lastPct = -10;
        while (!job.Finished)
        {
            await System.Threading.Tasks.Task.Delay(1000);
            if ((int)job.Percent >= lastPct + 10) { lastPct = (int)job.Percent; Console.WriteLine($"{m.Name}: {job.Percent:0}% {(job.Verifying ? "checking" : "")}"); }
        }
        Console.WriteLine($"{m.Name}: {(job.Error ?? "OK")} in {sw.Elapsed.TotalSeconds:0} s, file ok={AiModels.IsDownloaded(m)}");
    }
}
if (args.Contains("--vc"))
{
    foreach (var q in new[] { "open discord and join the lounge voice chat in study group", "join lounge vc in study group", "join the homework voice channel", "connect to music vc on book club server",
                              "study group sunucusundaki lounge ses kanalına gir", "lounge sesliye katıl", "leave vc", "sesten çık", "open discord", "join discord" })
    {
        var r = DiscordVc.Parse(q);
        Console.WriteLine($"  {q,-55} -> {(r == null ? "-" : r.Leave ? "LEAVE" : $"channel='{r.Channel}' server='{r.Server}'")}");
    }
}
if (args.Contains("--roblox"))
{
    AppIndex.Load();
    foreach (var q in new[] { "am", "adopt me", "brookhaven", "zzqqxx", "discord" })
    {
        var g = await Roblox.Search(q);
        Console.WriteLine($"  {q,-24} -> {(g == null ? "-" : $"{g.Name} ({g.PlaceId}) initials={Roblox.Initials(g.Name)} match={Roblox.Matches(q, g.Name)}")}");
    }
    var (app, s) = AppIndex.Find("am");
    Console.WriteLine($"  app for am: {app?.Name} {s}");
}
if (args.Contains("--urls"))
{
    foreach (var u in new[] { "roblox.com", "search roblox.com", "google roblox.com", "roblox.com ara", "www.youtube.com", "open roblox.com/games/920587237", "https://github.com", "search how to fix wifi", "notes.txt", "open discord", "go to roblox.com", "roblox.com'u aç" })
    {
        var p = Parser.Parse(u);
        bool url = Places.LooksLikeUrl(p.RawTarget.Trim(), out var link);
        Console.WriteLine($"  {u,-42} -> {p.Kind,-7} '{p.RawTarget}' url={(url ? link : "-")} calc={Calc.LooksLikeMath(u)} q={Ai.IsQuestion(u)} tr={(TranslateParser.Parse(u) != null)}");
    }
}
if (args.Contains("--translate"))
{
    var cases = new[] { "waterı türkçeye çevir", "water ı turkceye cevir", "waterı turkceye cevir", "appleyi türkçeye çevir", "gameı türkçeye çevir", "waterın türkçesi", "translate taxi to turkish", "kitabı ingilizceye çevir", "translate water to turkish","water'ı türkçeye çevir", "water türkçesi", "water türkçesi ne", "translate su", "translate good morning",
                        "kitap ingilizcesi", "how do you say thank you in german", "translate I love playing games to turkish", "bunu ingilizceye çevir: nasılsın", "translate", "open translate" };
    foreach (var c in cases) { var r = TranslateParser.Parse(c); Console.WriteLine($"  {c,-45} -> {(r == null ? "-" : $"'{r.Text}' to {r.Language}")}"); }
    Settings.Current.AiModel = "smart"; Settings.Current.AiEnabled = true;
    foreach (var c in new[] { "translate water to turkish", "translate su", "translate I love playing games to turkish", "kitap ingilizcesi", "how do you say thank you in german",
                              "bunu ingilizceye çevir: nasılsın", "translate I didn't finish my homework yet", "translate yarın maça gidiyoruz", "translate where is the library to turkish", "translate apple" })
    {
        var r = TranslateParser.Parse(c);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t = await Ai.Translate(r.Text, r.Language, true);
        Console.WriteLine($"  {sw.Elapsed.TotalSeconds:0.0}s  {r.Text} -> {t}");
    }
}
if (args.Contains("--ask"))
{
    foreach (var q in new[] { "what does gravity mean", "yerçekimi ne demek", "why is the sky blue?", "türkiye'nin başkenti neresidir", "who made minecraft", "can you open spotify?", "open discord", "how much ram", "is my pc full", "how do I take a screenshot" })
        Console.WriteLine($"  question? {Ai.IsQuestion(q),-5} {q}");
    Settings.Current.AiModel = "smart"; Settings.Current.AiEnabled = true;
    foreach (var q in new[] { "what does gravity mean", "yerçekimi ne demek", "why is the sky blue?", "türkiye'nin başkenti neresidir", "who made minecraft" })
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var a = await Ai.Answer(q, true);
        Console.WriteLine($"  {sw.Elapsed.TotalSeconds:0.0}s  {q}\n      {a}");
    }
}
if (args.Contains("--ai-test"))
{
    AppIndex.Load();
    var only = args.SkipWhile(a => a != "--ai-test").Skip(1).FirstOrDefault();
    var tests = new[]
    {
        "open the thing I play music with",
        "arkadaşlarla konuştuğum uygulamayı aç",
        "open spotify and turn the volume down a bit",
        "make the screen less bright it hurts my eyes",
        "how much space is left on my computer",
        "müzik çalan uygulamayı kapat",
        "put a 5 minute timer for my noodles",
        "what is the capital of france",
        "close the music app",
        "bana 10 dakika sonra ders çalışmayı hatırlat",
        "ekranı biraz karart",
        "I want to watch videos",
    };
    foreach (var m in AiModels.All.Where(m => only == null || m.Id == only))
    {
        if (!AiModels.IsDownloaded(m)) { Console.WriteLine($"== {m.Name}: not downloaded"); continue; }
        Settings.Current.AiModel = m.Id; Settings.Current.AiEnabled = true;
        foreach (var gpu in new[] { true, false })
        {
            Console.WriteLine($"== {m.Name} ({m.Params}) on {(gpu ? "GPU" : "CPU")}");
            foreach (var q in tests.Take(gpu ? tests.Length : 2))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var a = await Ai.Ask(q, gpu);
                Console.WriteLine($"  {sw.Elapsed.TotalSeconds,4:0.0}s  {q}  ->  [{string.Join(" | ", a.Commands)}] {a.Say} {a.Error}");
            }
        }
    }
}
if (args.Contains("--bench"))
{
    async System.Threading.Tasks.Task Time(string name, Func<System.Threading.Tasks.Task> f, int runs = 5)
    {
        await f();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < runs; i++) await f();
        Console.WriteLine($"  {name,-38} {sw.Elapsed.TotalMilliseconds / runs,8:0.00} ms");
    }
    var sw0 = System.Diagnostics.Stopwatch.StartNew(); AppIndex.Load(); Console.WriteLine($"  {"load app list (startup)",-38} {sw0.ElapsedMilliseconds,8} ms");
    await Time("understand a command (parser)", () => { Parser.Parse("chrome'u kapat"); return System.Threading.Tasks.Task.CompletedTask; }, 1000);
    await Time("find an app (open discrod)", () => { AppIndex.Find("discrod"); return System.Threading.Tasks.Task.CompletedTask; }, 50);
    await Time("guess while typing", () => { Predictor.Predict("open spo"); return System.Threading.Tasks.Task.CompletedTask; }, 50);
    await Time("calculator 15*24", async () => await Brain.Handle("15*24"), 50);
    await Time("ram", async () => await Brain.Handle("ram"), 20);
    await Time("battery", async () => await Brain.Handle("battery"), 20);
    await Time("volume (read)", async () => await Brain.Handle("volume"), 20);
    await Time("brightness (read)", async () => await Brain.Handle("brightness"), 5);
    await Time("list running apps (for close)", () => { Closer.List(); return System.Threading.Tasks.Task.CompletedTask; }, 5);
    await Time("list windows (switch to / snap)", () => { WinControl.Windows(); return System.Threading.Tasks.Task.CompletedTask; }, 5);
    await Time("find browser windows (tabs)", () => { Tabs.FindWindows(); return System.Threading.Tasks.Task.CompletedTask; }, 5);
    await Time("what's playing", async () => await Media.Current(), 10);
    await Time("find a folder (documents)", () => { Places.FindFiles("documents", Scope.Any); return System.Threading.Tasks.Task.CompletedTask; }, 3);
    await Time("find a file that doesn't exist", () => { Places.FindFiles("zzqqxx_nothing", Scope.Any); return System.Threading.Tasks.Task.CompletedTask; }, 2);
    await Time("cpu (has a 0.5 s sample built in)", async () => await Brain.Handle("cpu"), 2);
}
if (args.Contains("--extras"))
{
    foreach (var e in new[] { "15*24", "2+2*3", "(3+4)^2", "%18 of 250", "18% of 250", "250'nin %18'i", "10/4", "3,5*2", "2026", "10 - 15" })
        Console.WriteLine($"  calc '{e}': math={Calc.LooksLikeMath(e)} ok={Calc.TryEval(e, out var v)} -> {Calc.Format(v)}");
    foreach (var e in new[] { "timer 10 min", "10 dakika sayaç", "timer 1h 30m", "5 min timer", "sayaç 90 saniye" })
        Console.WriteLine($"  dur '{e}' -> {Timers.ParseDuration(Text.Fold(e))}");
    foreach (var e in new[] { "remind me at 9 pm to study", "remind me at 9:30 to call mom", "saat 21:00'de ders çalış hatırlat", "remind me in 20 min to eat", "20 dakika sonra yemek yemeyi hatırlat", "remind me tomorrow at 8 am to wake up" })
        Console.WriteLine($"  rem '{e}' -> clock={Timers.ParseClock(Text.Fold(e).Replace("'", ""))} dur={Timers.ParseDuration(Text.Fold(e))} text='{Timers.ReminderText(e)}'");
    var b = SystemTools.GetBattery(); Console.WriteLine($"  battery {b}");
    Console.WriteLine($"  ram {SystemTools.Ram()} cpu {await SystemTools.Cpu()}");
    Console.WriteLine($"  dark mode {SystemTools.IsDarkMode()}");
    var np = await Media.Current(); Console.WriteLine($"  now playing: {np}");
    Console.WriteLine("  windows: " + string.Join(", ", WinControl.Windows().Select(w => w.Display).Distinct().Take(8)));
    foreach (var e in new[] { "pause", "sonraki şarkı", "battery", "ram", "wifi off", "bluetooth aç", "dark mode", "screenshot", "ss", "note buy milk", "süt al diye not al", "not defteri aç", "notes", "clipboard", "minimize all", "switch to chrome", "chrome'a geç", "chrome left", "edge'i sola", "maximize edge", "play brookhaven", "boost", "reminders", "timer", "stop timer", "open am" })
    {
        var p = Parser.Parse(e);
        Console.WriteLine($"  '{e}' -> parser {p.Kind}");
    }
}
if (args.Contains("--audio"))
{
    int v0 = Audio.GetVolume(); bool m0 = Audio.GetMute();
    Console.WriteLine($"  volume now {v0} muted={m0}");
    Console.WriteLine($"  set {Math.Max(v0 - 1, 0)} -> {Audio.SetVolume(Math.Max(v0 - 1, 0))}");
    Console.WriteLine($"  set back -> {Audio.SetVolume(v0)}");
    Audio.SetMute(m0);
    var b0 = Audio.GetBrightness();
    Console.WriteLine($"  brightness now {b0?.ToString() ?? "n/a"}");
    if (b0 != null)
    {
        Console.WriteLine($"  set {Math.Max(b0.Value - 1, 0)} -> {Audio.SetBrightness(Math.Max(b0.Value - 1, 0))}");
        Console.WriteLine($"  set back -> {Audio.SetBrightness(b0.Value)}");
    }
    var r = await Brain.Handle("volume");
    Console.WriteLine($"  'volume' -> {r}");
}
if (args.Contains("--learn"))
{
    AppIndex.Load();
    Learner.Load();
    async System.Threading.Tasks.Task<string> Run(string cmd)
    {
        var r = await Brain.Handle(cmd);
        if (r is Confirm c) return "CONFIRM " + c.Title;
        return r is Message m ? m.Text : r.GetType().Name;
    }
    Console.WriteLine("  1 " + await Run("zzmuzikk"));
    Console.WriteLine("  2 " + await Run("not defteri aç"));
    System.Threading.Thread.Sleep(1500);
    var r3 = await Brain.Handle("close notepad"); if (r3 is Confirm c3) Console.WriteLine("  3 " + await c3.OnYes());
    Console.WriteLine("  4 " + await Run("zzmuzikk"));
    System.Threading.Thread.Sleep(1500);
    var r5 = await Brain.Handle("close notepad"); if (r5 is Confirm c5) Console.WriteLine("  5 " + await c5.OnYes());
    Console.WriteLine("  6 " + await Run("zzkedi means calculator"));
    Console.WriteLine("  7 predict 'zzk' -> " + Predictor.Predict("zzk"));
    Console.WriteLine("  8 " + await Run("forget zzmuzikk"));
    Console.WriteLine("  9 " + await Run("forget zzkedi"));
    Console.WriteLine("  10 search guard: " + Parser.Parse("search what love means").Kind);
}
if (args.Contains("--predict"))
{
    AppIndex.Load();
    foreach (var t in new[] { "open disc", "disc", "op", "open spo", "close chr", "theme mid", "tema m", "vol", "the", "sett", "open you", "open down", "open indir", "parl", "sesi", "gam", "open stand", "st", "ste", "rob", "shut" })
        Console.WriteLine($"  '{t}' -> '{Predictor.Predict(t)}'");
}
if (args.Contains("--live"))
{
    AppIndex.Load();
    async System.Threading.Tasks.Task<string> Say(string cmd, bool yes)
    {
        var r = await Brain.Handle(cmd);
        var s = r.ToString();
        if (r is Confirm c) { s = $"CONFIRM {c.Title} | {c.Sub}"; if (yes) s += " -> " + (await c.OnYes()); }
        if (r is Pick p) s = "PICK " + string.Join(" / ", p.Choices.Select(x => x.Label + " @ " + x.Sub));
        return s;
    }
    var tmp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "pcisland_test_delete_me.txt");
    System.IO.File.WriteAllText(tmp, "test");
    foreach (var (cmd, yes) in new[] { ("not defteri aç", false), ("close notepad", true), ("delete pcisland_test_delete_me.txt", true), ("open asdkjhqwe", false), ("delete some_missing_folder_xyz", false), ("close chrome", false), ("open downloads", false) })
    {
        if (cmd == "close notepad") System.Threading.Thread.Sleep(2500);
        if (cmd == "open downloads") { Console.WriteLine("  (skipping actually opening downloads)"); continue; }
        Console.WriteLine($"  {cmd} => {await Say(cmd, yes)}");
    }
    Console.WriteLine($"  test file still exists: {System.IO.File.Exists(tmp)}");
}
return fail == 0 ? 0 : 1;

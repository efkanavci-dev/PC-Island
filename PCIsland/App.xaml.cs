using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PCIsland.Core;

namespace PCIsland;

public partial class App : Application
{
    static Mutex mutex;
    static EventWaitHandle summonEvent;
    MainWindow island;
    Tray tray;
    SettingsWindow settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length >= 2 && e.Args[0] == "--selftest")
        {
            var outFile = e.Args[1];
            var args_shot = e.Args.Length >= 3 ? e.Args[2] : null;
            Task.Run(async () =>
            {
                var log = new System.Text.StringBuilder();
                try
                {
                    Settings.Load();
                    AppIndex.Load();
                    log.AppendLine($"apps {AppIndex.Count}");
                    log.AppendLine($"calc {(await Brain.Handle("15*24"))}");
                    foreach (var q in new[] { "discord", "spotify", "notepad" })
                    {
                        var (app, _) = AppIndex.Find(q);
                        if (app == null) continue;
                        var src = app.IsShellApp ? "shell:AppsFolder\\" + app.Launch : app.Launch;
                        var img = Dispatcher.Invoke(() => Icons.For(src)) as System.Windows.Media.Imaging.BitmapSource;
                        log.AppendLine($"icon {app.Name}: {(img == null ? "none" : $"{img.PixelWidth}x{img.PixelHeight}")}");
                    }
                    if (args_shot != null) Dispatcher.Invoke(() =>
                    {
                        var da = AppIndex.Find("discord").app;
                        var img = Icons.For(da.IsShellApp ? "shell:AppsFolder\\" + da.Launch : da.Launch) as System.Windows.Media.Imaging.BitmapSource;
                        if (img == null) return;
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                        using var fs = System.IO.File.Create(args_shot);
                        enc.Save(fs);
                    });
                    var m = AiModels.All.LastOrDefault(AiModels.IsDownloaded);
                    if (m != null)
                    {
                        Settings.Current.AiModel = m.Id;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var a = await Ai.Ask("open the thing I play music with", AiModels.HasGraphicsCard);
                        log.AppendLine($"ai {m.Name} {sw.Elapsed.TotalSeconds:0.0}s [{string.Join("|", a.Commands)}] {a.Error}");
                        var t = await Ai.Translate("water", "Turkish", AiModels.HasGraphicsCard);
                        log.AppendLine($"translate water -> {t}");
                    }
                }
                catch (Exception ex) { log.AppendLine("ERROR " + ex); }
                System.IO.File.WriteAllText(outFile, log.ToString());
                Dispatcher.Invoke(Shutdown);
            });
            return;
        }
        mutex = new Mutex(true, "PCIsland.SingleInstance", out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting("PCIsland.Summon").Set(); } catch { }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            try
            {
                var log = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCIsland", "errors.log");
                System.IO.File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex.Exception}\n\n");
            }
            catch { }
        };
        Settings.Load();
        Predictor.LoadHistory();
        Learner.Load();
        Ai.LoadCache();
        AiModels.Progress += job =>
        {
            if (!job.Finished) return;
            if (job.Error == null && !Native.IsFullscreenAppRunning()) _ = Ai.WarmUp(job.Model);
            Dispatcher.BeginInvoke(() => island?.Notify(job.Error == null ? $"AI ready: {job.Model.Name}" : $"AI download: {job.Error}", job.Error == null));
        };
        Task.Run(AppIndex.Load);
        if (Settings.Current.StartWithWindows) Core.Startup.Apply(true);

        island = new MainWindow();
        tray = new Tray();
        island.SettingsRequested += ShowSettings;
        island.GuideRequested += ShowGuide;
        tray.OpenGuide += ShowGuide;
        island.ThemeApplied += t => { tray?.SetTheme(t); if (settings?.IsVisible == true) settings.Build(); };
        tray.OpenIsland += () => island.Summon();
        tray.OpenSettings += ShowSettings;
        tray.ThemePicked += t => island.ApplyTheme(t);
        tray.Quit += Quit;
        island.Show();
        if (e.Args.Contains("--open-settings")) Dispatcher.BeginInvoke(ShowSettings);
        if (e.Args.Contains("--open-guide")) Dispatcher.BeginInvoke(ShowGuide);
        if (e.Args.Contains("--open-ai")) Dispatcher.BeginInvoke(ShowAiPicker);

        summonEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "PCIsland.Summon");
        new Thread(() =>
        {
            while (true)
            {
                summonEvent.WaitOne();
                Dispatcher.BeginInvoke(() => island.Summon());
            }
        }) { IsBackground = true }.Start();

        var warm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        warm.Tick += async (_, _) =>
        {
            warm.Stop();
            var key = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? AppContext.BaseDirectory) + "|" + Settings.Current.AiModel;
            if (!Ai.Ready || Settings.Current.AiWarmedFor == key || Native.IsFullscreenAppRunning()) return;
            await Ai.WarmUp(Ai.Current);
            Settings.Current.AiWarmedFor = key;
            Settings.Save();
        };
        warm.Start();

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        timer.Tick += (_, _) => Task.Run(AppIndex.Load);
        timer.Start();
    }

    GuideWindow guide;
    AiPickerWindow picker;

    void ShowAiPicker()
    {
        if (picker == null || !picker.IsLoaded)
        {
            picker = new AiPickerWindow();
            picker.Changed += () => { if (settings?.IsLoaded == true) settings.Build(); };
            picker.Closed += (_, _) => { picker = null; if (settings?.IsLoaded == true) settings.Build(); };
            picker.Show();
        }
        picker.Activate();
    }

    void ShowGuide()
    {
        if (guide == null || !guide.IsLoaded)
        {
            guide = new GuideWindow(island);
            guide.Closed += (_, _) => guide = null;
            guide.Show();
        }
        guide.Activate();
    }

    void ShowSettings()
    {
        if (settings == null || !settings.IsLoaded)
        {
            settings = new SettingsWindow(island);
            settings.ThemePicked += t => island.ApplyTheme(t);
            settings.QuitRequested += Quit;
            settings.AiPickerRequested += ShowAiPicker;
            settings.Closed += (_, _) => settings = null;
            settings.Show();
        }
        settings.Activate();
    }

    void Quit()
    {
        tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        base.OnExit(e);
    }
}

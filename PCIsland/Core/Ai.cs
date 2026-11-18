using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace PCIsland.Core;

public record AiAnswer(List<string> Commands, string Say, string Error = null);

public static class Ai
{
    static bool nativeReady;
    static readonly SemaphoreSlim gate = new(1, 1);
    static Dictionary<string, List<string>> cache = new();
    static string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCIsland", "ai_cache.json");

    public static AiModel Current => AiModels.ById(Settings.Current.AiModel);

    public static bool Ready => Settings.Current.AiEnabled && Current != null && AiModels.IsDownloaded(Current);

    public static void LoadCache()
    {
        try { if (File.Exists(CachePath)) cache = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(CachePath)) ?? new(); } catch { }
    }

    public static void Remember(string phrase, List<string> commands)
    {
        lock (cache)
        {
            cache[Text.Clean(phrase)] = commands;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                File.WriteAllText(CachePath, JsonSerializer.Serialize(cache));
            }
            catch { }
        }
    }

    public static void ForgetAll()
    {
        lock (cache) { cache.Clear(); try { File.Delete(CachePath); } catch { } }
    }

    public static List<string> Cached(string phrase)
    {
        lock (cache) return cache.TryGetValue(Text.Clean(phrase), out var c) ? c : null;
    }

    static void InitNative()
    {
        if (nativeReady) return;
        try
        {
            var dirs = new List<string> { AppContext.BaseDirectory };
            if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string nd)
                dirs.AddRange(nd.Split(';', StringSplitOptions.RemoveEmptyEntries));
            dirs = dirs.Select(d => d.TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase)
                       .Where(d => Directory.Exists(Path.Combine(d, "runtimes"))).ToList();
            NativeLibraryConfig.All.WithSearchDirectories(dirs).WithVulkan(true).WithAutoFallback(true);
            NativeLibraryConfig.All.WithLogCallback((level, msg) => { });
        }
        catch { }
        nativeReady = true;
    }

    static string Prompt(string request)
    {
        var apps = AppIndex.All.Select(a => a.Name).Where(n => n.Length <= 40).Distinct().OrderBy(n => n).Take(220);
        var running = Closer.List().Where(a => a.HasWindow).Select(a => a.Display).Distinct().Take(30);
        var sb = new StringBuilder();
        sb.Append("<|im_start|>system\n");
        sb.Append("You turn a person's request into commands for PC Island, a Windows command bar. ");
        sb.Append("Reply with ONLY one JSON object: {\"commands\":[...],\"say\":\"...\"}. ");
        sb.Append("Each command must use one of these forms exactly:\n");
        sb.Append("open <app, website or folder> | close <app> | close tab | close tabs | search <words> | volume <0-100> | volume up | volume down | mute | unmute | ");
        sb.Append("brightness <0-100> | brightness up | brightness down | pause | play | next | previous | timer <number> min | ");
        sb.Append("remind me in <number> min to <thing> | remind me at <time> to <thing> | note <text> | battery | ram | cpu | disk | ");
        sb.Append("wifi on | wifi off | bluetooth on | bluetooth off | dark mode | light mode | screenshot | minimize all | switch to <app> | ");
        sb.Append("<app> left | <app> right | maximize <app> | minimize <app> | lock | sleep | restart | shutdown\n");
        sb.Append("Rules: write commands in English even if the request is Turkish. Use app names from the lists. ");
        sb.Append("\"close tab\" and \"close tabs\" are only for browser tabs; to close a program use \"close <app>\". ");
        sb.Append("Timers are always \"timer <number> min\". ");
        sb.Append("If something can't be done with these commands, use an empty list and answer briefly in \"say\" in the person's language. Never invent other commands.\n");
        sb.Append("Installed apps: ").Append(string.Join(", ", apps)).Append('\n');
        sb.Append("Open windows: ").Append(string.Join(", ", running)).Append('\n');
        sb.Append("Websites (use open <name>): ").Append(string.Join(", ", Places.Sites.Select(s => s.Name))).Append('\n');
        sb.Append("Games: ").Append(string.Join(", ", Settings.Current.Games.Select(g => g.Name))).Append(" (use: play <game>)\n");
        sb.Append("Examples:\n");
        sb.Append("open the thing I play music with -> {\"commands\":[\"open Spotify\"],\"say\":\"\"}\n");
        sb.Append("arkadaşlarla konuştuğum uygulamayı aç -> {\"commands\":[\"open Discord\"],\"say\":\"\"}\n");
        sb.Append("open spotify and turn the volume down a bit -> {\"commands\":[\"open Spotify\",\"volume down\"],\"say\":\"\"}\n");
        sb.Append("close everything except discord -> {\"commands\":[\"close Google Chrome\",\"close Spotify\"],\"say\":\"\"} (only apps from Open windows)\n");
        sb.Append("müzik çalan programı kapat -> {\"commands\":[\"close Spotify\"],\"say\":\"\"}\n");
        sb.Append("set an alarm in half an hour -> {\"commands\":[\"timer 30 min\"],\"say\":\"\"}\n");
        sb.Append("is my pc full -> {\"commands\":[\"disk\"],\"say\":\"\"}\n");
        sb.Append("I'm bored, I want to watch something -> {\"commands\":[\"open YouTube\"],\"say\":\"\"}\n");
        sb.Append("<|im_end|>\n<|im_start|>user\n");
        sb.Append(request.Trim()).Append(" /no_think<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n{");
        return sb.ToString();
    }

    public static async Task<AiAnswer> Ask(string request, bool useGpu, CancellationToken ct = default)
    {
        var model = Current;
        if (model == null || !AiModels.IsDownloaded(model)) return new AiAnswer(new(), "", "No AI model is downloaded");
        await gate.WaitAsync(ct);
        try
        {
            return await Task.Run(async () =>
            {
                InitNative();
                var p = new ModelParams(AiModels.PathOf(model))
                {
                    ContextSize = 4096,
                    GpuLayerCount = useGpu ? 999 : 0,
                    BatchSize = 512,
                };
                using var weights = await LLamaWeights.LoadFromFileAsync(p, ct);
                var exec = new StatelessExecutor(weights, p);
                var inf = new InferenceParams
                {
                    MaxTokens = 160,
                    AntiPrompts = new List<string> { "<|im_end|>" },
                    SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.1f, TopP = 0.9f }
                };
                var sb = new StringBuilder("{");
                await foreach (var tok in exec.InferAsync(Prompt(request), inf, ct))
                {
                    sb.Append(tok);
                    if (Balanced(sb)) break;
                }
                return Parse(sb.ToString());
            }, ct);
        }
        catch (Exception ex) { return new AiAnswer(new(), "", "AI failed: " + ex.Message); }
        finally
        {
            gate.Release();
            GC.Collect();
        }
    }

    static readonly Regex QuestionStart = new(@"^(what|whats|what's|who|whos|who's|why|how|when|where|which|is|are|can|could|does|do|did|should|would|will|define|explain|tell me|meaning of|ne|neden|niye|nasil|kim|kimdir|nerede|nereye|hangi|kac|ne zaman|anlat|acikla)\b", RegexOptions.IgnoreCase);
    static readonly Regex QuestionEnd = new(@"(\?|\b(ne demek|ne demektir|nedir|kimdir|nerededir|neresidir|nasil yapilir|ne ise yarar|mi|mu|midir|mudur|misin|musun|mean|means)\s*\??)$", RegexOptions.IgnoreCase);

    static readonly HashSet<string> TurkishWords = new()
    {
        "ne", "nedir", "demek", "demektir", "neden", "niye", "nasil", "kim", "kimdir", "nerede", "neresi", "neresidir", "hangi", "kac",
        "mi", "mu", "midir", "mudur", "bir", "ve", "bu", "su", "icin", "ile", "gibi", "zaman", "anlat", "acikla", "nedir", "olan", "yapilir", "var", "yok"
    };

    public static bool IsTurkish(string text)
    {
        if (text.Any(c => "çğıöşüÇĞİÖŞÜ".Contains(c))) return true;
        var words = Text.Clean(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Count(TurkishWords.Contains) >= 1 && !words.Any(w => w is "what" or "why" or "how" or "who" or "the" or "is" or "does" or "do");
    }

    public static bool AboutThisPc(string text)
    {
        var w = Text.Clean(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] pc = { "pc", "pcm", "computer", "bilgisayar", "bilgisayarim", "bilgisayarimda", "laptop", "disk", "space", "storage", "battery", "pil",
                        "sarj", "wifi", "ram", "cpu", "volume", "ses", "brightness", "parlaklik", "screen", "ekran", "playing", "caliyor", "timer", "reminder" };
        return w.Any(pc.Contains);
    }

    public static bool IsQuestion(string input)
    {
        var f = Text.Fold(input.Trim()).Replace("'", "");
        var words = Text.Clean(f).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 && !f.EndsWith("?")) return false;
        string[] actions = { "open", "close", "kill", "delete", "launch", "start", "play", "pause", "turn", "set", "volume", "brightness",
                             "remind", "timer", "search", "mute", "minimize", "maximize", "switch", "lock", "shutdown", "restart",
                             "ac", "kapat", "sil", "baslat", "oynat", "durdur", "kis", "hatirlat", "kur", "ara" };
        if (words.Any(w => actions.Contains(w) || Text.SuffixVariants(w).Skip(1).Any(v => v is "kapat" or "hatirlat"))) return false;
        return f.EndsWith("?") || QuestionStart.IsMatch(f) || QuestionEnd.IsMatch(f);
    }

    public static async Task<string> Answer(string question, bool useGpu, CancellationToken ct = default)
    {
        var model = Current;
        if (model == null || !AiModels.IsDownloaded(model)) return null;
        await gate.WaitAsync(ct);
        try
        {
            return await Task.Run(async () =>
            {
                InitNative();
                var p = new ModelParams(AiModels.PathOf(model)) { ContextSize = 2048, GpuLayerCount = useGpu ? 999 : 0, BatchSize = 512 };
                using var weights = await LLamaWeights.LoadFromFileAsync(p, ct);
                var exec = new StatelessExecutor(weights, p);
                string lang = IsTurkish(question) ? "Turkish" : "English";
                var prompt = "<|im_start|>system\nYou are a helpful assistant inside a small Windows bar. Answer ONLY in " + lang + ". "
                           + "Be short and clear: at most 3 sentences, no lists, no markdown. "
                           + "If you are not sure, say so.<|im_end|>\n<|im_start|>user\n" + question.Trim()
                           + " /no_think<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n";
                var inf = new InferenceParams
                {
                    MaxTokens = 180,
                    AntiPrompts = new List<string> { "<|im_end|>" },
                    SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.3f, TopP = 0.9f }
                };
                var sb = new StringBuilder();
                await foreach (var tok in exec.InferAsync(prompt, inf, ct)) sb.Append(tok);
                var text = sb.ToString().Replace("<|im_end|>", "").Replace("**", "").Trim();
                text = Regex.Replace(text, @"\s+", " ");
                if (text.Length > 420)
                {
                    int cut = text.LastIndexOf('.', 420);
                    text = cut > 120 ? text[..(cut + 1)] : text[..420] + "…";
                }
                return text;
            }, ct);
        }
        catch { return null; }
        finally { gate.Release(); GC.Collect(); }
    }

    public static async Task<string> Translate(string text, string language, bool useGpu, CancellationToken ct = default)
    {
        var model = Current;
        if (model == null || !AiModels.IsDownloaded(model)) return null;
        await gate.WaitAsync(ct);
        try
        {
            return await Task.Run(async () =>
            {
                InitNative();
                var p = new ModelParams(AiModels.PathOf(model)) { ContextSize = 1024, GpuLayerCount = useGpu ? 999 : 0, BatchSize = 512 };
                using var weights = await LLamaWeights.LoadFromFileAsync(p, ct);
                var exec = new StatelessExecutor(weights, p);
                async Task<string> Once(string target)
                {
                string Turn(string u, string a) => $"<|im_start|>user\nTranslate into {target}: {u} /no_think<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n{a}<|im_end|>\n";
                string shots = target == "Turkish"
                    ? Turn("I love playing games", "Oyun oynamayı seviyorum.") + Turn("I don't like rain", "Yağmuru sevmiyorum.") + Turn("book", "kitap")
                    : target == "English"
                    ? Turn("Yarın okula gideceğim", "I will go to school tomorrow.") + Turn("Bunu sevmedim", "I didn't like this.") + Turn("elma", "apple")
                    : "";
                var prompt = "<|im_start|>system\nYou are a careful translator. Translate the user's text into " + target + ". "
                           + "Keep the exact meaning, including negatives and tense. "
                           + "Reply with ONLY the translation: no quotes, no explanations. "
                           + "For a single word, give its most common translation.<|im_end|>\n"
                           + shots
                           + "<|im_start|>user\nTranslate into " + target + ": " + text.Trim()
                           + " /no_think<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n";
                var inf = new InferenceParams
                {
                    MaxTokens = Math.Min(400, 30 + text.Length * 2),
                    AntiPrompts = new List<string> { "<|im_end|>" },
                    SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.1f, TopP = 0.9f, RepeatPenalty = 1.15f }
                };
                var sb = new StringBuilder();
                await foreach (var tok in exec.InferAsync(prompt, inf, ct)) sb.Append(tok);
                var r = sb.ToString().Replace("<|im_end|>", "").Replace("**", "").Trim().Trim('"', '“', '”', '\'');
                int nl = r.IndexOf('\n');
                if (nl > 0 && text.IndexOf('\n') < 0) r = r[..nl].Trim();
                return r;
                }

                if (language != "auto") return await Once(language);
                bool looksTurkish = IsTurkish(text);
                var first = await Once(looksTurkish ? "English" : "Turkish");
                if (Text.Clean(first) != Text.Clean(text)) return first;
                return await Once(looksTurkish ? "Turkish" : "English");
            }, ct);
        }
        catch { return null; }
        finally { gate.Release(); GC.Collect(); }
    }

    public static async Task WarmUp(AiModel m)
    {
        if (m == null || !AiModels.IsDownloaded(m) || !AiModels.HasGraphicsCard) return;
        var saved = Settings.Current.AiModel;
        await gate.WaitAsync();
        try
        {
            await Task.Run(async () =>
            {
                InitNative();
                var p = new ModelParams(AiModels.PathOf(m)) { ContextSize = 4096, GpuLayerCount = 999, BatchSize = 512 };
                using var w = LLamaWeights.LoadFromFile(p);
                var exec = new StatelessExecutor(w, p);
                await foreach (var _ in exec.InferAsync(Prompt("open spotify"), new InferenceParams { MaxTokens = 2 })) { }
            });
        }
        catch { }
        finally { gate.Release(); GC.Collect(); }
    }

    static bool Balanced(StringBuilder sb)
    {
        int depth = 0; bool inStr = false, esc = false;
        for (int i = 0; i < sb.Length; i++)
        {
            char c = sb[i];
            if (esc) { esc = false; continue; }
            if (c == '\\') { esc = true; continue; }
            if (c == '"') inStr = !inStr;
            else if (!inStr && c == '{') depth++;
            else if (!inStr && c == '}') { depth--; if (depth == 0) return true; }
        }
        return false;
    }

    public static AiAnswer Parse(string text)
    {
        var s = text.Trim();
        int a = s.IndexOf('{'), b = s.LastIndexOf('}');
        if (a >= 0 && b > a) s = s[a..(b + 1)];
        try
        {
            using var doc = JsonDocument.Parse(s);
            var cmds = new List<string>();
            if (doc.RootElement.TryGetProperty("commands", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && e.GetString().Trim().Length > 0) cmds.Add(e.GetString().Trim());
            string say = doc.RootElement.TryGetProperty("say", out var sv) && sv.ValueKind == JsonValueKind.String ? sv.GetString() : "";
            return new AiAnswer(cmds.Take(6).ToList(), say ?? "");
        }
        catch
        {
            var cmds = Regex.Matches(s, "\"([^\"]{2,80})\"").Select(m => m.Groups[1].Value)
                .Where(x => x != "commands" && x != "say").Take(3).ToList();
            return new AiAnswer(cmds, "", cmds.Count == 0 ? "The AI gave an answer I couldn't read" : null);
        }
    }
}

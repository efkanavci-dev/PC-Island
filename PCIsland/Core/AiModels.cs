using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PCIsland.Core;

public record AiModel(string Id, string Name, string Params, string Repo, string File, long Bytes, string Sha256,
                      double VramGb, string Description, string Good, string Weak);

public static class AiModels
{
    public static readonly AiModel[] All =
    {
        new("tiny", "Tiny", "0.6B", "Qwen/Qwen3-0.6B-GGUF", "Qwen3-0.6B-Q8_0.gguf", 639_446_688L,
            "9465e63a22add5354d9bb4b99e90117043c7124007664907259bd16d043bb031", 1.0,
            "The smallest and quickest. Made for older PCs and laptops without a graphics card.",
            "Very fast, small download, runs fine on almost any PC",
            "Often misreads long sentences and Turkish"),
        new("fast", "Fast", "1.7B", "unsloth/Qwen3-1.7B-GGUF", "Qwen3-1.7B-Q4_K_M.gguf", 1_107_409_472L,
            "b139949c5bd74937ad8ed8c8cf3d9ffb1e99c866c823204dc42c0d91fa181897", 1.6,
            "A good middle. Understands everyday sentences and answers quickly.",
            "Quick answers, works well on most PCs",
            "Can get confused by long or unusual requests"),
        new("smart", "Smart", "4B", "Qwen/Qwen3-4B-GGUF", "Qwen3-4B-Q4_K_M.gguf", 2_497_280_256L,
            "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5", 3.0,
            "The best at understanding what you mean, in Turkish and English.",
            "Understands loose, long and mixed-language requests",
            "Bigger download, needs about 3 GB of graphics memory to be quick"),
    };

    public static AiModel ById(string id) => All.FirstOrDefault(m => m.Id == id);

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCIsland", "models");
    public static string PathOf(AiModel m) => System.IO.Path.Combine(Dir, m.File);
    public static bool IsDownloaded(AiModel m) => File.Exists(PathOf(m));

    // ---------- PC check ----------

    static (double gb, string name)? gpu;

    public static (double gb, string name) Gpu()
    {
        if (gpu != null) return gpu.Value;
        double best = 0; string name = "";
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls != null)
                foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k == null) continue;
                    double gb = 0;
                    var q = k.GetValue("HardwareInformation.qwMemorySize");
                    if (q is long l) gb = l / 1024.0 / 1024 / 1024;
                    else if (q is byte[] b && b.Length >= 8) gb = BitConverter.ToInt64(b, 0) / 1024.0 / 1024 / 1024;
                    else if (k.GetValue("HardwareInformation.MemorySize") is int i) gb = (uint)i / 1024.0 / 1024 / 1024;
                    else if (k.GetValue("HardwareInformation.MemorySize") is byte[] b2 && b2.Length >= 4) gb = BitConverter.ToUInt32(b2, 0) / 1024.0 / 1024 / 1024;
                    var n = k.GetValue("DriverDesc") as string ?? "";
                    if (gb > best) { best = gb; name = n; }
                }
        }
        catch { }
        gpu = (best, name);
        return gpu.Value;
    }

    public static bool HasGraphicsCard => Gpu().gb >= 1.9;

    public static AiModel BestForThisPc()
    {
        var g = Gpu().gb;
        if (g >= 5.5) return ById("smart");
        if (g >= 2.5) return ById("fast");
        return ById("tiny");
    }

    public static bool MaybeSlow(AiModel m) => Gpu().gb < m.VramGb + 0.8;

    // ---------- download ----------

    public class Job
    {
        public AiModel Model;
        public long Done, Total;
        public string Error;
        public bool Finished, Verifying;
        public CancellationTokenSource Cancel = new();
        public double Percent => Total <= 0 ? 0 : Math.Min(100, Done * 100.0 / Total);
    }

    public static readonly Dictionary<string, Job> Jobs = new();
    public static event Action<Job> Progress;

    static readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };

    public static Job Download(AiModel m)
    {
        lock (Jobs)
        {
            if (Jobs.TryGetValue(m.Id, out var existing) && !existing.Finished) return existing;
            var job = new Job { Model = m, Total = m.Bytes };
            Jobs[m.Id] = job;
            _ = Task.Run(() => Run(job));
            return job;
        }
    }

    static async Task Run(Job job)
    {
        var m = job.Model;
        var final = PathOf(m);
        var part = final + ".part";
        try
        {
            Directory.CreateDirectory(Dir);
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            var url = $"https://huggingface.co/{m.Repo}/resolve/main/{m.File}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("PCIsland/1.0");
            if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, job.Cancel.Token);
            if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable) have = 0;
            else resp.EnsureSuccessStatusCode();
            bool resumed = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!resumed) have = 0;
            var len = resp.Content.Headers.ContentLength;
            if (len != null) job.Total = have + len.Value;
            job.Done = have;

            await using (var src = await resp.Content.ReadAsStreamAsync(job.Cancel.Token))
            await using (var dst = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var buf = new byte[1 << 20];
                int n;
                var last = DateTime.MinValue;
                while ((n = await src.ReadAsync(buf, job.Cancel.Token)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), job.Cancel.Token);
                    job.Done += n;
                    if ((DateTime.Now - last).TotalMilliseconds > 250) { last = DateTime.Now; Progress?.Invoke(job); }
                }
            }

            job.Verifying = true;
            Progress?.Invoke(job);
            string hash;
            await using (var fs = File.OpenRead(part)) hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, job.Cancel.Token)).ToLowerInvariant();
            if (hash != m.Sha256)
            {
                File.Delete(part);
                throw new Exception("The download was damaged. Try again");
            }
            if (File.Exists(final)) File.Delete(final);
            File.Move(part, final);
        }
        catch (OperationCanceledException) { job.Error = "Stopped"; }
        catch (HttpRequestException) { job.Error = "No internet connection or the download site is down"; }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070070)) { job.Error = "Not enough disk space"; }
        catch (Exception ex) { job.Error = ex.Message; }
        job.Verifying = false;
        job.Finished = true;
        Progress?.Invoke(job);
    }

    public static void Remove(AiModel m)
    {
        try { File.Delete(PathOf(m)); } catch { }
        try { File.Delete(PathOf(m) + ".part"); } catch { }
    }

    public static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes / (double)(1 << 20):0} MB";
}

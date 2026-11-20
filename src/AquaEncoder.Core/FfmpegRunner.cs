using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AquaEncoder.Core;

/// <summary>ffmpeg/ffprobe çalıştırma: bilgi okuma, kodlama, ilerleme, iptal.</summary>
public sealed class FfmpegRunner
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string FfmpegPath { get; }
    public string FfprobePath { get; }

    public FfmpegRunner(string? ffmpegPath = null, string? ffprobePath = null)
    {
        FfmpegPath = ffmpegPath ?? Locate("ffmpeg");
        FfprobePath = ffprobePath ?? Locate("ffprobe");
    }

    /// <summary>Önce programın yanındaki ffmpeg'e (taşınabilir kullanım), yoksa PATH'e bakar.</summary>
    public static string Locate(string name)
    {
        string exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var dir in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "ffmpeg") })
        {
            var p = Path.Combine(dir, exe);
            if (File.Exists(p)) return p;
        }
        return exe; // PATH'ten çözülür
    }

    // ---------------- Bilgi okuma ----------------

    public async Task<MediaInfo> ProbeAsync(string file, CancellationToken ct = default)
    {
        var (code, stdout, stderr) = await RunCaptureAsync(FfprobePath,
            ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", file], ct);
        if (code != 0) throw new InvalidOperationException($"ffprobe başarısız: {stderr.Trim()}");

        using var doc = JsonDocument.Parse(stdout);
        int w = 0, h = 0; double fps = 0; bool audio = false;
        foreach (var st in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            string type = st.GetProperty("codec_type").GetString() ?? "";
            if (type == "video" && w == 0)
            {
                // Kapak resmi (attached_pic) videosunu atla.
                if (st.TryGetProperty("disposition", out var d) &&
                    d.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1) continue;
                w = st.GetProperty("width").GetInt32();
                h = st.GetProperty("height").GetInt32();
                fps = ParseRate(st.TryGetProperty("avg_frame_rate", out var r) ? r.GetString() : null);
                if (fps <= 0) fps = ParseRate(st.TryGetProperty("r_frame_rate", out var r2) ? r2.GetString() : null);
            }
            else if (type == "audio") audio = true;
        }
        if (w == 0) throw new InvalidOperationException("Dosyada video akışı bulunamadı.");

        double dur = 0;
        if (doc.RootElement.TryGetProperty("format", out var fmt) &&
            fmt.TryGetProperty("duration", out var ds))
            double.TryParse(ds.GetString(), NumberStyles.Float, Inv, out dur);

        return new MediaInfo(w, h, fps, dur, audio);
    }

    private static double ParseRate(string? s)
    {
        if (string.IsNullOrEmpty(s) || s == "0/0") return 0;
        var p = s.Split('/');
        if (p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, Inv, out var n) &&
            double.TryParse(p[1], NumberStyles.Float, Inv, out var d) && d != 0) return n / d;
        return double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : 0;
    }

    // ---------------- Kodlama ----------------

    public async Task EncodeAsync(EncodeSettings s, IProgress<EncodeProgress>? progress = null,
                                  Action<string>? log = null, CancellationToken ct = default)
    {
        if (!File.Exists(s.InputVideo)) throw new FileNotFoundException("Video bulunamadı.", s.InputVideo);
        if (!string.IsNullOrWhiteSpace(s.SubtitleFile) && !File.Exists(s.SubtitleFile))
            throw new FileNotFoundException("Altyazı bulunamadı.", s.SubtitleFile);
        if (string.IsNullOrWhiteSpace(s.OutputFile)) throw new ArgumentException("Çıkış dosyası seçilmedi.");

        var main = await ProbeAsync(s.InputVideo, ct);
        MediaInfo? intro = null;
        if (!string.IsNullOrWhiteSpace(s.IntroVideo) && s.PreviewSeconds <= 0)
        {
            if (!File.Exists(s.IntroVideo)) throw new FileNotFoundException("Intro bulunamadı.", s.IntroVideo);
            intro = await ProbeAsync(s.IntroVideo, ct);
        }

        double totalSec = main.DurationSeconds + (intro?.DurationSeconds ?? 0);
        if (s.PreviewSeconds > 0)
            totalSec = Math.Max(1, Math.Min(s.PreviewSeconds, main.DurationSeconds - s.PreviewStartSeconds));
        var args = FfmpegCommandBuilder.Build(s, main, intro);

        // Önce basit adlı geçici dosyaya yaz, bitince asıl ada taşı (yarım dosya asıl adla kalmaz).
        string finalOut = Path.GetFullPath(s.OutputFile);
        string outDir = Path.GetDirectoryName(finalOut)!;
        Directory.CreateDirectory(outDir);
        string tmpOut = Path.Combine(outDir, "aquaenc_" + Guid.NewGuid().ToString("N")[..8] + ".part.mp4");
        args[^1] = tmpOut;

        log?.Invoke($"[tanı] ffmpeg={FfmpegPath} · çalışma klasörü={outDir} · hedef={finalOut}");
        log?.Invoke(FfmpegPath + " " + string.Join(" ", args.Select(Quote)));

        var psi = NewPsi(FfmpegPath, args, outDir, redirectStdin: true);
        using var proc = new Process { StartInfo = psi };
        var errTail = new Queue<string>();
        var sw = Stopwatch.StartNew();

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            log?.Invoke(e.Data);
            lock (errTail) { errTail.Enqueue(e.Data); while (errTail.Count > 15) errTail.Dequeue(); }
        };

        double outSec = 0, fpsNow = 0, speed = 0;
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            int eq = e.Data.IndexOf('=');
            if (eq < 0) return;
            string k = e.Data[..eq], v = e.Data[(eq + 1)..].Trim();
            switch (k)
            {
                case "out_time_us" or "out_time_ms":
                    if (long.TryParse(v, out var us) && us >= 0) outSec = us / 1_000_000.0; break;
                case "fps": double.TryParse(v, NumberStyles.Float, Inv, out fpsNow); break;
                case "speed": double.TryParse(v.TrimEnd('x'), NumberStyles.Float, Inv, out speed); break;
                case "progress":
                    double pct = totalSec > 0 ? Math.Clamp(outSec / totalSec * 100, 0, 100) : 0;
                    if (v == "end") pct = 100;
                    progress?.Report(new EncodeProgress(pct, fpsNow, speed, sw.Elapsed, TimeSpan.FromSeconds(outSec)));
                    break;
            }
        };

        proc.Start();
        try { proc.StandardInput.Close(); } catch { }   // ffmpeg klavye girişi beklemesin
        TrySetLowPriority(proc);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } });
        await proc.WaitForExitAsync(CancellationToken.None);

        if (ct.IsCancellationRequested)
        {
            TryDelete(tmpOut);
            throw new OperationCanceledException(ct);
        }
        if (proc.ExitCode != 0)
        {
            TryDelete(tmpOut);
            string tail; lock (errTail) tail = string.Join("\n", errTail);
            string hint = tail.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
                ? $"\nİpucu: ffmpeg '{outDir}' klasörüne yazamadı. Çalışma klasörü ve ffmpeg yolu günlükteki [tanı] satırında."
                : "";
            throw new InvalidOperationException($"ffmpeg hata verdi (kod {proc.ExitCode}):\n{tail}{hint}");
        }

        try { File.Move(tmpOut, finalOut, overwrite: true); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Kodlama bitti ama dosya asıl adına taşınamadı: {ex.Message}\nGeçici dosya duruyor: {tmpOut}");
        }
    }

    // ---------------- Donanım tespiti ----------------

    /// <summary>
    /// Hangi arka uçların GERÇEKTEN çalıştığını bulur. "ffmpeg -encoders" listesinde olması
    /// yetmez (ekran kartı yoksa/sürücü eksikse patlar), bu yüzden 1 karelik deneme kodlaması yapılır.
    /// </summary>
    public async Task<IReadOnlyList<EncoderBackend>> DetectBackendsAsync(VideoCodec codec, CancellationToken ct = default)
    {
        var found = new List<EncoderBackend> { EncoderBackend.Cpu };
        foreach (var be in new[] { EncoderBackend.Nvidia, EncoderBackend.IntelQsv, EncoderBackend.AmdAmf })
        {
            var enc = FfmpegCommandBuilder.VideoEncoderArgs(new EncodeSettings { Backend = be, Codec = codec, Quality = 28, Speed = SpeedPreset.Hizli });
            var args = new List<string> { "-hide_banner", "-v", "error", "-f", "lavfi", "-i", "color=c=black:s=256x144:r=1:d=1", "-frames:v", "1",
                                          "-pix_fmt", "yuv420p" };
            args.AddRange(enc);
            args.AddRange(["-f", "null", "-"]);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15));
                var (code, _, _) = await RunCaptureAsync(FfmpegPath, args, cts.Token);
                if (code == 0) found.Add(be);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* zaman aşımı = kullanılamaz */ }
            catch (System.ComponentModel.Win32Exception) { break; } // ffmpeg hiç yok
        }
        return found;
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var (code, o, _) = await RunCaptureAsync(FfmpegPath, ["-version"], ct);
            return code == 0 ? o.Split('\n')[0].Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    // ---------------- Yardımcılar ----------------

    private static ProcessStartInfo NewPsi(string exe, IEnumerable<string> args, string? workDir = null, bool redirectStdin = false)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = redirectStdin,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
        foreach (var a in args) psi.ArgumentList.Add(a); // tırnak/boşluk sorunu yok
        return psi;
    }

    private static async Task<(int code, string stdout, string stderr)> RunCaptureAsync(
        string exe, IEnumerable<string> args, CancellationToken ct)
    {
        using var p = new Process { StartInfo = NewPsi(exe, args) };
        p.Start();
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        return (p.ExitCode, await o, await e);
    }

    /// <summary>Kodlama sırasında bilgisayar donmasın: düşük öncelik.</summary>
    private static void TrySetLowPriority(Process p)
    {
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private static string Quote(string s) => s.Contains(' ') || s.Contains(';') ? $"\"{s}\"" : s;
}

using System.Globalization;

namespace AquaEncoder.Core;

/// <summary>
/// EncodeSettings'ten ffmpeg argüman listesi üretir. Saf fonksiyon: process başlatmaz,
/// bu yüzden kolayca test edilebilir.
/// Tasarım: altyazı SADECE ana videoya basılır, sonra intro ile birleştirilir
/// (böylece altyazı zamanlaması intro süresinden etkilenmez).
/// </summary>
public static class FfmpegCommandBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static List<string> Build(EncodeSettings s, MediaInfo main, MediaInfo? intro)
    {
        var a = new List<string> { "-hide_banner", "-nostdin", "-y", "-nostats", "-progress", "pipe:1" };
        bool preview = s.PreviewSeconds > 0;
        bool hasIntro = !preview && intro != null && !string.IsNullOrWhiteSpace(s.IntroVideo);

        // Çıkış çözünürlüğü: ana videonun (veya seçilen) boyutu. Intro buna uydurulur.
        int outH = s.Height == OutputHeight.Orijinal ? main.Height : Math.Min((int)s.Height, main.Height);
        int outW = (int)Math.Round(main.Width * (double)outH / main.Height);
        outW -= outW % 2; outH -= outH % 2;           // yuv420p için çift sayı şart
        double fps = main.Fps > 0 ? main.Fps : 24;
        string fpsStr = fps.ToString("0.###", Inv);

        // --- Girişler ---
        int mainIdx = 0, introIdx = -1, silenceIdx = -1;
        if (hasIntro) { a.AddRange(["-i", s.IntroVideo!]); introIdx = 0; mainIdx = 1; }
        a.AddRange(["-i", s.InputVideo]);
        if (hasIntro && !intro!.HasAudio)
        {
            // Intro'da ses yoksa concat bozulmasın diye sessizlik üret.
            a.AddRange(["-f", "lavfi", "-t", intro.DurationSeconds.ToString("0.###", Inv),
                        "-i", "anullsrc=r=48000:cl=stereo"]);
            silenceIdx = mainIdx + 1;
        }

        // --- Video filtre zinciri (ana video) ---
        var vf = new List<string>();
        if (outH != main.Height) vf.Add($"scale={outW}:{outH}:flags=bicubic");
        if (!string.IsNullOrWhiteSpace(s.SubtitleFile)) vf.Add(SubtitleFilter(s.SubtitleFile!, s.FontsDir));
        vf.Add("setsar=1");
        vf.Add($"fps={fpsStr}");
        vf.Add("format=yuv420p");

        bool mainHasAudio = main.HasAudio;

        if (!hasIntro)
        {
            a.AddRange(["-map", "0:v:0"]);
            if (mainHasAudio) a.AddRange(["-map", $"0:a:{s.AudioStreamIndex}?"]);
            a.AddRange(["-vf", string.Join(",", vf)]);
        }
        else
        {
            var fc = new List<string>
            {
                // Intro'yu ana videonun boyut/fps/formatına uydur (siyah bantla, bozmadan).
                $"[{introIdx}:v]scale={outW}:{outH}:force_original_aspect_ratio=decrease," +
                $"pad={outW}:{outH}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fpsStr},format=yuv420p[iv]",
                $"[{mainIdx}:v]{string.Join(",", vf)}[mv]",
            };
            string introAudioSrc = intro!.HasAudio ? $"[{introIdx}:a:0]" : $"[{silenceIdx}:a]";
            fc.Add($"{introAudioSrc}aresample=48000,aformat=channel_layouts=stereo[ia]");
            if (mainHasAudio)
            {
                fc.Add($"[{mainIdx}:a:{s.AudioStreamIndex}]aresample=48000,aformat=channel_layouts=stereo[ma]");
                fc.Add("[iv][ia][mv][ma]concat=n=2:v=1:a=1[v][a]");
                a.AddRange(["-filter_complex", string.Join(";", fc), "-map", "[v]", "-map", "[a]"]);
            }
            else
            {
                fc.Add("[iv][mv]concat=n=2:v=1:a=0[v]");
                a.AddRange(["-filter_complex", string.Join(";", fc), "-map", "[v]"]);
            }
        }

        // --- Video kodlayıcı ---
        a.AddRange(VideoEncoderArgs(s));
        if (s.Threads > 0 && s.Backend == EncoderBackend.Cpu)
            a.AddRange(["-threads", s.Threads.ToString(Inv)]);

        // --- Ses ---
        if (mainHasAudio || hasIntro)
            a.AddRange(["-c:a", "aac", "-b:a", $"{s.AudioKbps}k", "-ac", "2"]);

        // Önizleme: ÇIKIŞ tarafında -ss/-t kullanılır (girişte -ss altyazı zamanlamasını kaydırırdı).
        if (preview)
        {
            if (s.PreviewStartSeconds > 0) a.AddRange(["-ss", s.PreviewStartSeconds.ToString("0.###", Inv)]);
            a.AddRange(["-t", s.PreviewSeconds.ToString(Inv)]);
        }

        // Altyazı/ek akışlar gömülü olmasın; temiz MP4.
        a.AddRange(["-sn", "-dn", "-map_chapters", "-1", "-movflags", "+faststart", s.OutputFile]);
        return a;
    }

    /// <summary>Seçilen arka uç + codec + hız ön ayarı için kodlayıcı argümanları.</summary>
    public static List<string> VideoEncoderArgs(EncodeSettings s)
    {
        int q = Math.Clamp(s.Quality, 0, 51);
        string qs = q.ToString(Inv);
        bool h265 = s.Codec == VideoCodec.H265;
        var r = new List<string>();

        switch (s.Backend)
        {
            case EncoderBackend.Cpu:
                r.AddRange(["-c:v", h265 ? "libx265" : "libx264",
                            "-preset", s.Speed switch { SpeedPreset.Hizli => "veryfast", SpeedPreset.Dengeli => "medium", _ => "slow" },
                            "-crf", qs]);
                if (h265) r.AddRange(["-tag:v", "hvc1"]); // Apple/Windows oynatıcı uyumu
                break;

            case EncoderBackend.Nvidia:
                r.AddRange(["-c:v", h265 ? "hevc_nvenc" : "h264_nvenc",
                            "-preset", s.Speed switch { SpeedPreset.Hizli => "p2", SpeedPreset.Dengeli => "p5", _ => "p7" },
                            "-rc", "vbr", "-cq", qs, "-b:v", "0"]);
                if (h265) r.AddRange(["-tag:v", "hvc1"]);
                break;

            case EncoderBackend.IntelQsv:
                r.AddRange(["-c:v", h265 ? "hevc_qsv" : "h264_qsv",
                            "-preset", s.Speed switch { SpeedPreset.Hizli => "veryfast", SpeedPreset.Dengeli => "medium", _ => "veryslow" },
                            "-global_quality", qs]);
                if (h265) r.AddRange(["-tag:v", "hvc1"]);
                break;

            case EncoderBackend.AmdAmf:
                r.AddRange(["-c:v", h265 ? "hevc_amf" : "h264_amf",
                            "-quality", s.Speed switch { SpeedPreset.Hizli => "speed", SpeedPreset.Dengeli => "balanced", _ => "quality" },
                            "-rc", "cqp", "-qp_i", qs, "-qp_p", qs]);
                if (h265) r.AddRange(["-tag:v", "hvc1"]);
                break;
        }
        return r;
    }

    /// <summary>
    /// subtitles filtresi için yol kaçışı. Windows yolları (C:\a\b.ass) filtre
    /// sözdiziminde bozulur: ters slash -> slash, ':' -> '\:' , tek tırnak kaçışı gerekir.
    /// </summary>
    public static string SubtitleFilter(string subtitlePath, string? fontsDir)
    {
        string f = $"subtitles=filename='{EscapePath(subtitlePath)}'";
        if (!string.IsNullOrWhiteSpace(fontsDir)) f += $":fontsdir='{EscapePath(fontsDir!)}'";
        return f;
    }

    public static string EscapePath(string path) =>
        path.Replace('\\', '/')
            .Replace(":", "\\:")
            .Replace("'", "'\\\\\\''")
            .Replace("[", "\\[").Replace("]", "\\]")
            .Replace(",", "\\,");
}

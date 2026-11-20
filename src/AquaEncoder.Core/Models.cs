namespace AquaEncoder.Core;

public enum VideoCodec { H264, H265 }

/// <summary>Kodlayıcı arka ucu: yazılım (CPU) veya donanım hızlandırma.</summary>
public enum EncoderBackend { Cpu, Nvidia, IntelQsv, AmdAmf }

/// <summary>Hız/kalite ön ayarı. Eski PC'ler için "Hizli" önerilir.</summary>
public enum SpeedPreset { Hizli, Dengeli, Kaliteli }

/// <summary>Videoyu (altyazı gömülmeden önce) yeniden boyutlandırma seçeneği.</summary>
public enum OutputHeight { Orijinal = 0, P1080 = 1080, P720 = 720, P480 = 480 }

public sealed class EncodeSettings
{
    /// <summary>Ana video dosyası.</summary>
    public string InputVideo { get; set; } = "";

    /// <summary>Gömülecek altyazı (.ass / .srt / .ssa). Boşsa altyazı gömülmez.</summary>
    public string? SubtitleFile { get; set; }

    /// <summary>Altyazı için ekstra font klasörü (fansub'larda özel fontlar için şart).</summary>
    public string? FontsDir { get; set; }

    /// <summary>Videonun başına eklenecek intro. Boşsa eklenmez.</summary>
    public string? IntroVideo { get; set; }

    public string OutputFile { get; set; } = "";

    public VideoCodec Codec { get; set; } = VideoCodec.H264;
    public EncoderBackend Backend { get; set; } = EncoderBackend.Cpu;
    public SpeedPreset Speed { get; set; } = SpeedPreset.Dengeli;

    /// <summary>CRF (CPU) veya CQ/QP/global_quality (donanım). Düşük = daha iyi kalite. Tipik: 18-28.</summary>
    public int Quality { get; set; } = 23;

    public OutputHeight Height { get; set; } = OutputHeight.Orijinal;

    /// <summary>Ses bit hızı (kbps). Intro eklendiğinde ses her zaman yeniden kodlanır.</summary>
    public int AudioKbps { get; set; } = 160;

    /// <summary>Çok çekirdekli CPU'yu kısıtlamak için (0 = otomatik). Zayıf PC'lerde 2-4 önerilir.</summary>
    public int Threads { get; set; } = 0;

    /// <summary>Ana videodaki ses akışı sırası (birden fazla dil varsa).</summary>
    public int AudioStreamIndex { get; set; } = 0;

    /// <summary>0 = normal encode. &gt;0 = sadece bu kadar saniyelik önizleme (intro atlanır).</summary>
    public int PreviewSeconds { get; set; } = 0;

    /// <summary>Önizlemenin başlangıç saniyesi (altyazının olduğu bir yeri görmek için).</summary>
    public double PreviewStartSeconds { get; set; } = 0;
}

/// <summary>ffprobe ile okunan, komut oluşturmak için gereken video bilgisi.</summary>
public sealed record MediaInfo(int Width, int Height, double Fps, double DurationSeconds, bool HasAudio);

public sealed record EncodeProgress(double Percent, double Fps, double Speed, TimeSpan Elapsed, TimeSpan Position);

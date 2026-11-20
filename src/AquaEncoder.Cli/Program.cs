using AquaEncoder.Core;

// Çekirdek motoru arayüzsüz test etmek için basit CLI.
// Kullanım: AquaEncoder.Cli --in v.mkv --sub s.ass --intro i.mp4 --out o.mp4 [--crf 23] [--backend cpu|nvidia|qsv|amf] [--h265] [--height 720] [--detect]
var map = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i++)
{
    if (!args[i].StartsWith("--")) continue;
    string k = args[i][2..];
    map[k] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
}

var runner = new FfmpegRunner();
Console.WriteLine("ffmpeg: " + (await runner.GetVersionAsync() ?? "BULUNAMADI"));

if (map.ContainsKey("detect"))
{
    var be = await runner.DetectBackendsAsync(VideoCodec.H264);
    Console.WriteLine("Kullanılabilir arka uçlar: " + string.Join(", ", be));
    return 0;
}

var s = new EncodeSettings
{
    InputVideo = map.GetValueOrDefault("in", ""),
    SubtitleFile = map.GetValueOrDefault("sub"),
    FontsDir = map.GetValueOrDefault("fonts"),
    IntroVideo = map.GetValueOrDefault("intro"),
    OutputFile = map.GetValueOrDefault("out", ""),
    Quality = int.Parse(map.GetValueOrDefault("crf", "23")),
    Codec = map.ContainsKey("h265") ? VideoCodec.H265 : VideoCodec.H264,
    Backend = map.GetValueOrDefault("backend", "cpu") switch
    { "nvidia" => EncoderBackend.Nvidia, "qsv" => EncoderBackend.IntelQsv, "amf" => EncoderBackend.AmdAmf, _ => EncoderBackend.Cpu },
    Speed = SpeedPreset.Hizli,
    PreviewSeconds = int.Parse(map.GetValueOrDefault("preview", "0")),
    PreviewStartSeconds = double.Parse(map.GetValueOrDefault("start", "0"), System.Globalization.CultureInfo.InvariantCulture),
    Height = map.GetValueOrDefault("height") switch
    { "1080" => OutputHeight.P1080, "720" => OutputHeight.P720, "480" => OutputHeight.P480, _ => OutputHeight.Orijinal },
};

var prog = new Progress<EncodeProgress>(p => Console.Write($"\r  %{p.Percent,5:0.0}  {p.Fps,5:0} fps  {p.Speed:0.0}x   "));
try
{
    await runner.EncodeAsync(s, prog, log: map.ContainsKey("verbose") ? Console.Error.WriteLine : null);
    Console.WriteLine("\nBitti: " + s.OutputFile);
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("\nHATA: " + ex.Message);
    return 1;
}

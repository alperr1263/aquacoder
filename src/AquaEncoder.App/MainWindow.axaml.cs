using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using AquaEncoder.Core;

namespace AquaEncoder.App;

public partial class MainWindow : Window
{
    private static readonly string[] VideoExt = [".mkv", ".mp4", ".avi", ".mov", ".webm", ".m4v", ".ts", ".flv", ".wmv"];
    private static readonly string[] SubExt = [".ass", ".ssa", ".srt"];

    private readonly FfmpegRunner _runner = new();
    private readonly ObservableCollection<QueueItem> _items = new();
    private readonly AppSettings _cfg = AppSettings.Load();
    private readonly StringBuilder _log = new();

    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _loading = true;       // true iken kontrol değişiklikleri "kullanıcı eylemi" sayılmaz
    private bool _ffmpegOk;
    private int _detectVersion;
    private EncoderBackend? _prefBackend;
    private string? _intro, _fonts, _outDir;

    public MainWindow()
    {
        InitializeComponent();

        QueueList.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => UpdateEmptyHint();
        QueueList.SelectionChanged += (_, _) => UpdateSelectionPanel();

        Root.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Root.AddHandler(DragDrop.DropEvent, OnDrop);

        ApplySettingsToUi();
        WireControls();
        _loading = false;

        UpdateEmptyHint();
        UpdateSelectionPanel();
        UpdateTotals();

        Opened += async (_, _) => await DetectBackendsAsync();
        Closing += (_, _) => { _cts?.Cancel(); SaveSettings(); };
    }

    // ═══════════════════════════════════════════ Ayarlar (yükle / kaydet)

    private void ApplySettingsToUi()
    {
        ApplyTheme(_cfg.DarkTheme);
        ProfileBox.SelectedIndex = Math.Clamp(_cfg.ProfileIndex, 0, 3);
        CodecBox.SelectedIndex = Math.Clamp(_cfg.CodecIndex, 0, 1);
        HeightBox.SelectedIndex = Math.Clamp(_cfg.HeightIndex, 0, 3);
        SpeedBox.SelectedIndex = Math.Clamp(_cfg.SpeedIndex, 0, 2);
        QualitySlider.Value = Math.Clamp(_cfg.Quality, 14, 32);
        LowEndBox.IsChecked = _cfg.LowEnd;
        ThreadsBox.Value = _cfg.Threads;
        AudioBox.Value = _cfg.AudioKbps;
        AudioIdxBox.Value = _cfg.AudioStreamIndex;
        SuffixBox.Text = _cfg.Suffix;
        OverwriteBox.IsChecked = _cfg.Overwrite;
        EmbeddedFontsBox.IsChecked = _cfg.UseEmbeddedFonts;
        PreviewStartBox.Value = _cfg.PreviewStart;
        PreviewLenBox.Value = _cfg.PreviewLength;

        _intro = ExistingOrNull(_cfg.IntroPath, file: true);
        _fonts = ExistingOrNull(_cfg.FontsDir, file: false);
        _outDir = ExistingOrNull(_cfg.OutputDir, file: false);
        if (Enum.TryParse<EncoderBackend>(_cfg.Backend, out var be)) _prefBackend = be;

        RefreshPathTexts();
        UpdateQualityLabel();
    }

    private static string? ExistingOrNull(string? p, bool file) =>
        !string.IsNullOrEmpty(p) && (file ? File.Exists(p) : Directory.Exists(p)) ? p : null;

    private void SaveSettings()
    {
        _cfg.DarkTheme = ThemeBtn.Content?.ToString()?.Contains("Açık") == true;
        _cfg.ProfileIndex = ProfileBox.SelectedIndex;
        _cfg.CodecIndex = CodecBox.SelectedIndex;
        _cfg.HeightIndex = HeightBox.SelectedIndex;
        _cfg.SpeedIndex = SpeedBox.SelectedIndex;
        _cfg.Quality = (int)QualitySlider.Value;
        _cfg.LowEnd = LowEndBox.IsChecked == true;
        _cfg.Threads = (int)(ThreadsBox.Value ?? 0);
        _cfg.AudioKbps = (int)(AudioBox.Value ?? 160);
        _cfg.AudioStreamIndex = (int)(AudioIdxBox.Value ?? 0);
        _cfg.Suffix = SuffixBox.Text ?? "";
        _cfg.Overwrite = OverwriteBox.IsChecked == true;
        _cfg.UseEmbeddedFonts = EmbeddedFontsBox.IsChecked == true;
        _cfg.PreviewStart = (int)(PreviewStartBox.Value ?? 60);
        _cfg.PreviewLength = (int)(PreviewLenBox.Value ?? 10);
        _cfg.IntroPath = _intro;
        _cfg.FontsDir = _fonts;
        _cfg.OutputDir = _outDir;
        _cfg.Backend = _prefBackend?.ToString();
        _cfg.Save();
    }

    private void WireControls()
    {
        QualitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
            UpdateQualityLabel();
            if (!_loading) ProfileBox.SelectedIndex = 3;           // elle ayar = "Özel"
        };
        SpeedBox.SelectionChanged += (_, _) => { if (!_loading) ProfileBox.SelectedIndex = 3; };
        ProfileBox.SelectionChanged += (_, _) =>
        {
            if (!_loading && ProfileBox.SelectedIndex is >= 0 and < 3) ApplyProfile(ProfileBox.SelectedIndex);
        };
        BackendBox.SelectionChanged += (_, _) =>
        {
            if (!_loading && (BackendBox.SelectedItem as ComboBoxItem)?.Tag is EncoderBackend b) _prefBackend = b;
        };
        CodecBox.SelectionChanged += async (_, _) => { if (!_loading) await DetectBackendsAsync(); };
        LowEndBox.IsCheckedChanged += (_, _) => { if (!_loading) SpeedBox.SelectedIndex = LowEndBox.IsChecked == true ? 0 : 1; };
    }

    private void ApplyProfile(int i)
    {
        var old = _loading; _loading = true;
        (QualitySlider.Value, SpeedBox.SelectedIndex) = i switch { 0 => (26, 0), 2 => (20, 2), _ => (23, 1) };
        _loading = old;
        UpdateQualityLabel();
    }

    private void UpdateQualityLabel()
    {
        int q = (int)QualitySlider.Value;
        string d = q <= 19 ? "çok yüksek" : q <= 23 ? "yüksek" : q <= 27 ? "dengeli" : "küçük dosya";
        QualityLabel.Text = $"{q} · {d}";
    }

    // ═══════════════════════════════════════════ Tema

    private void ApplyTheme(bool dark)
    {
        if (Application.Current != null)
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemeBtn.Content = dark ? "☀ Açık tema" : "☾ Koyu tema";
    }

    private void OnToggleTheme(object? s, RoutedEventArgs e) =>
        ApplyTheme(ThemeBtn.Content?.ToString()?.Contains("Koyu") == true);

    // ═══════════════════════════════════════════ Donanım tespiti

    private async Task DetectBackendsAsync()
    {
        int ver = ++_detectVersion;
        var codec = CodecBox.SelectedIndex == 1 ? VideoCodec.H265 : VideoCodec.H264;

        BackendBox.IsEnabled = false;
        StartBtn.IsEnabled = false;
        SetStatus("Donanım taranıyor…");

        string? version = await _runner.GetVersionAsync();
        if (ver != _detectVersion) return;
        _ffmpegOk = version != null;
        if (!_ffmpegOk)
        {
            SetStatus("ffmpeg bulunamadı! ffmpeg ve ffprobe dosyalarını AquaEncoder'ın yanına (veya 'ffmpeg' klasörüne) koy.");
            BackendBox.IsEnabled = true;
            return;
        }

        var found = await _runner.DetectBackendsAsync(codec);
        if (ver != _detectVersion) return;

        var old = _loading; _loading = true;
        BackendBox.Items.Clear();
        foreach (var be in found) BackendBox.Items.Add(new ComboBoxItem { Content = BackendName(be, codec), Tag = be });
        int idx = _prefBackend != null ? found.ToList().IndexOf(_prefBackend.Value) : 0;
        BackendBox.SelectedIndex = idx >= 0 ? idx : 0;
        _loading = old;

        BackendBox.IsEnabled = true;
        StartBtn.IsEnabled = !_busy;
        SetStatus(found.Count > 1
            ? $"Hazır · donanım hızlandırma bulundu: {string.Join(", ", found.Skip(1).Select(b => BackendName(b, codec)))}"
            : "Hazır · ekran kartı hızlandırması bulunamadı, işlemci kullanılacak.");
    }

    private static string BackendName(EncoderBackend be, VideoCodec c) => be switch
    {
        EncoderBackend.Cpu => c == VideoCodec.H265 ? "İşlemci (x265)" : "İşlemci (x264)",
        EncoderBackend.Nvidia => "NVIDIA (NVENC)",
        EncoderBackend.IntelQsv => "Intel (Quick Sync)",
        EncoderBackend.AmdAmf => "AMD (AMF)",
        _ => be.ToString()
    };

    // ═══════════════════════════════════════════ Dosya ekleme / sürükle-bırak

    private void OnDragOver(object? s, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? s, DragEventArgs e)
    {
        var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
        if (paths is { Count: > 0 }) HandlePaths(paths);
        e.Handled = true;
    }

    /// <summary>Bırakılan/seçilen her şeyi uzantısına göre ayırır: video → kuyruğa, altyazı → eşleştir.</summary>
    private void HandlePaths(IEnumerable<string> paths)
    {
        var videos = new List<string>();
        var subs = new List<string>();

        void Classify(string f)
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (VideoExt.Contains(ext)) videos.Add(f);
            else if (SubExt.Contains(ext)) subs.Add(f);
        }

        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                try { foreach (var f in Directory.EnumerateFiles(p)) Classify(f); } catch { }
            }
            else Classify(p);
        }

        int added = 0;
        foreach (var v in videos.OrderBy(NaturalKey, StringComparer.OrdinalIgnoreCase))
        {
            if (_items.Any(i => string.Equals(i.Video, v, StringComparison.OrdinalIgnoreCase))) continue;
            _items.Add(new QueueItem(v) { Sub = FindSiblingSub(v) });
            added++;
        }

        int matched = subs.Count > 0 ? MatchSubs(subs) : 0;
        UpdateSelectionPanel();
        UpdateTotals();

        if (added + matched == 0 && subs.Count + videos.Count == 0)
            SetStatus($"Desteklenen dosya yok. Video: {string.Join(" ", VideoExt)} · Altyazı: {string.Join(" ", SubExt)}");
        else
            SetStatus($"{added} video eklendi" + (subs.Count > 0 ? $", {matched}/{subs.Count} altyazı eşleşti" : "") + ".");
    }

    private static string NaturalKey(string s) => Regex.Replace(s, @"\d+", m => m.Value.PadLeft(10, '0'));

    /// <summary>Videoyla aynı klasörde aynı isimli (veya "isim.tr.ass" gibi) altyazıyı bulur.</summary>
    private static string? FindSiblingSub(string video)
    {
        try
        {
            string dir = Path.GetDirectoryName(video) ?? "";
            string stem = Path.GetFileNameWithoutExtension(video);
            foreach (var ext in SubExt)
            {
                var exact = Path.Combine(dir, stem + ext);
                if (File.Exists(exact)) return exact;
            }
            return Directory.EnumerateFiles(dir, stem + ".*")
                .Where(f => SubExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Altyazıları kuyruktaki videolarla dosya adına göre eşleştirir; kaç tane eşleştiğini döner.</summary>
    private int MatchSubs(List<string> subs)
    {
        int n = 0;
        var unmatched = new List<string>();

        foreach (var sub in subs)
        {
            string stem = Path.GetFileNameWithoutExtension(sub);
            var item = _items.FirstOrDefault(i =>
            {
                string vs = Path.GetFileNameWithoutExtension(i.Video);
                return stem.Equals(vs, StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith(vs + ".", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith(vs + " ", StringComparison.OrdinalIgnoreCase);
            });
            if (item != null) { item.Sub = sub; n++; } else unmatched.Add(sub);
        }

        if (unmatched.Count == 1 && QueueList.SelectedItem is QueueItem sel)
        {
            sel.Sub = unmatched[0]; n++;                       // tek altyazı → seçili videoya
        }
        else if (n == 0 && unmatched.Count > 1 && unmatched.Count == _items.Count)
        {
            // İsimler hiç tutmuyor ama sayılar eşit: doğal sıralamayla sırayla eşle (bölüm 1,2,3…).
            var ordered = unmatched.OrderBy(NaturalKey, StringComparer.OrdinalIgnoreCase).ToList();
            var videos = _items.OrderBy(i => NaturalKey(i.Name), StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < ordered.Count; i++) { videos[i].Sub = ordered[i]; n++; }
        }
        return n;
    }

    private async Task<List<string>> PickFilesAsync(string title, string name, string[] ext, bool multiple)
    {
        var res = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            FileTypeFilter = [new FilePickerFileType(name) { Patterns = ext.Select(x => "*" + x).ToList() }]
        });
        return res.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var res = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return res.Count > 0 ? res[0].TryGetLocalPath() : null;
    }

    private async void OnAddFiles(object? s, RoutedEventArgs e)
    {
        var f = await PickFilesAsync("Video seç", "Video", VideoExt, true);
        if (f.Count > 0) HandlePaths(f);
    }

    private async void OnAddFolder(object? s, RoutedEventArgs e)
    {
        var d = await PickFolderAsync("Video klasörü seç");
        if (d != null) HandlePaths([d]);
    }

    private void OnRemove(object? s, RoutedEventArgs e)
    {
        foreach (var it in QueueList.SelectedItems!.Cast<QueueItem>().ToList())
            if (it.State != ItemState.Running) _items.Remove(it);
        UpdateTotals();
    }

    private void OnClear(object? s, RoutedEventArgs e)
    {
        foreach (var it in _items.Where(i => i.State != ItemState.Running).ToList()) _items.Remove(it);
        UpdateTotals();
    }

    // ═══════════════════════════════════════════ Seçili öğe / Intro / Font / Çıkış paneli

    private void UpdateEmptyHint() => EmptyHint.IsVisible = _items.Count == 0;

    private void UpdateSelectionPanel()
    {
        if (QueueList.SelectedItem is QueueItem sel)
            SelSubText.Text = string.IsNullOrEmpty(sel.Sub) ? $"{sel.Name}\nAltyazı yok (sadece yeniden kodlanır)." : $"{sel.Name}\n{sel.Sub}";
        else
            SelSubText.Text = "Listeden bir video seç.";
    }

    private void RefreshPathTexts()
    {
        IntroText.Text = _intro ?? "Intro yok.";
        FontsText.Text = _fonts ?? "Seçilmedi (sistem fontları kullanılır).";
        OutDirText.Text = _outDir ?? "Videoyla aynı klasör.";
    }

    private async void OnPickSelSub(object? s, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not QueueItem sel) { SetStatus("Önce listeden bir video seç."); return; }
        var f = await PickFilesAsync("Altyazı seç", "Altyazı", SubExt, false);
        if (f.Count > 0) { sel.Sub = f[0]; UpdateSelectionPanel(); }
    }

    private void OnClearSelSub(object? s, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is QueueItem sel) { sel.Sub = null; UpdateSelectionPanel(); }
    }

    private async void OnMatchSubs(object? s, RoutedEventArgs e)
    {
        if (_items.Count == 0) { SetStatus("Önce video ekle."); return; }
        var f = await PickFilesAsync("Altyazıları seç", "Altyazı", SubExt, true);
        if (f.Count == 0) return;
        int n = MatchSubs(f);
        UpdateSelectionPanel();
        SetStatus($"{n}/{f.Count} altyazı eşleşti.");
    }

    private async void OnPickIntro(object? s, RoutedEventArgs e)
    {
        var f = await PickFilesAsync("Intro seç", "Video", VideoExt, false);
        if (f.Count > 0) { _intro = f[0]; RefreshPathTexts(); }
    }
    private void OnClearIntro(object? s, RoutedEventArgs e) { _intro = null; RefreshPathTexts(); }

    private async void OnPickFonts(object? s, RoutedEventArgs e)
    {
        var d = await PickFolderAsync("Font klasörü seç");
        if (d != null) { _fonts = d; RefreshPathTexts(); }
    }
    private void OnClearFonts(object? s, RoutedEventArgs e) { _fonts = null; RefreshPathTexts(); }

    private async void OnPickOutDir(object? s, RoutedEventArgs e)
    {
        var d = await PickFolderAsync("Çıkış klasörü seç");
        if (d != null) { _outDir = d; RefreshPathTexts(); }
    }
    private void OnClearOutDir(object? s, RoutedEventArgs e) { _outDir = null; RefreshPathTexts(); }

    // ═══════════════════════════════════════════ Encode ayarlarını oluşturma

    private string ComputeOutput(QueueItem item)
    {
        string dir = _outDir ?? Path.GetDirectoryName(item.Video) ?? "";
        string stem = Path.GetFileNameWithoutExtension(item.Video) + (SuffixBox.Text ?? "");
        string path = Path.Combine(dir, stem + ".mp4");

        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(item.Video), StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(dir, stem + "_out.mp4");                         // girişin üzerine asla yazma

        if (OverwriteBox.IsChecked != true)
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{stem}_{i}.mp4");
        return path;
    }

    private EncodeSettings BuildSettings(QueueItem item)
    {
        bool low = LowEndBox.IsChecked == true;
        var backend = (BackendBox.SelectedItem as ComboBoxItem)?.Tag as EncoderBackend? ?? EncoderBackend.Cpu;
        int threads = (int)(ThreadsBox.Value ?? 0);
        if (low && threads == 0) threads = 2;

        return new EncodeSettings
        {
            InputVideo = item.Video,
            SubtitleFile = item.Sub,
            FontsDir = _fonts,
            UseEmbeddedFonts = EmbeddedFontsBox.IsChecked == true,
            IntroVideo = _intro,
            OutputFile = ComputeOutput(item),
            Codec = CodecBox.SelectedIndex == 1 ? VideoCodec.H265 : VideoCodec.H264,
            Backend = backend,
            Speed = low ? SpeedPreset.Hizli : (SpeedPreset)Math.Clamp(SpeedBox.SelectedIndex, 0, 2),
            Quality = (int)QualitySlider.Value,
            Height = HeightBox.SelectedIndex switch
            { 1 => OutputHeight.P1080, 2 => OutputHeight.P720, 3 => OutputHeight.P480, _ => OutputHeight.Orijinal },
            AudioKbps = (int)(AudioBox.Value ?? 160),
            AudioStreamIndex = (int)(AudioIdxBox.Value ?? 0),
            Threads = threads,
        };
    }

    // ═══════════════════════════════════════════ Kuyruk çalıştırma

    private async void OnStart(object? s, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_ffmpegOk) { SetStatus("ffmpeg bulunamadı; encode başlatılamaz."); return; }
        if (!_items.Any(IsPending))
        {
            SetStatus(_items.Count == 0 ? "Kuyruk boş. Önce video ekle." : "Bekleyen video yok. Bitenleri 'Temizle' ile kaldırabilirsin.");
            return;
        }

        SaveSettings();
        _cts = new CancellationTokenSource();
        SetBusy(true);

        var seen = new HashSet<QueueItem>();
        int ok = 0, fail = 0;
        bool cancelled = false;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // Çalışırken eklenen videolar da sıraya girer.
                var item = _items.FirstOrDefault(i => !seen.Contains(i) && IsPending(i));
                if (item == null) break;
                seen.Add(item);

                int total = seen.Count + _items.Count(i => !seen.Contains(i) && IsPending(i));
                TotalText.Text = $"{seen.Count} / {total}";

                var settings = BuildSettings(item);
                item.OutputFile = settings.OutputFile;
                item.Error = null;
                item.State = ItemState.Running;
                item.Progress = 0;
                item.StatusText = "Başladı…";
                AppendLog($"═══ {item.Name} ═══");

                int doneBefore = seen.Count - 1;
                var progress = new Progress<EncodeProgress>(p =>
                {
                    item.Progress = p.Percent;
                    item.StatusText = $"%{p.Percent:0}";
                    Bar.Value = p.Percent;
                    TotalBar.Value = (doneBefore + p.Percent / 100.0) / total * 100;
                    StatusText.Text = $"{item.Name} · %{p.Percent:0.0} · {p.Fps:0} fps · {p.Speed:0.0}x hız";
                    EtaText.Text = p.Percent > 1
                        ? "Kalan ≈ " + FormatSpan(TimeSpan.FromSeconds(p.Elapsed.TotalSeconds * (100 - p.Percent) / p.Percent))
                        : "";
                });

                try
                {
                    await _runner.EncodeAsync(settings, progress, AppendLog, _cts.Token);
                    item.State = ItemState.Done; item.Progress = 100; item.StatusText = "Bitti";
                    ok++;
                }
                catch (OperationCanceledException)
                {
                    item.State = ItemState.Cancelled; item.Progress = 0; item.StatusText = "İptal";
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    item.State = ItemState.Failed; item.Progress = 0; item.StatusText = "Hata (günlüğe bak)";
                    item.Error = ex.Message;
                    AppendLog("HATA: " + ex.Message);
                    fail++;
                }
            }
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
            Bar.Value = 0;
            EtaText.Text = "";
            UpdateTotals();
        }

        if (cancelled) SetStatus("İptal edildi.");
        else
        {
            SetStatus($"Kuyruk bitti · {ok} başarılı" + (fail > 0 ? $", {fail} hatalı (günlüğe bak)" : "") + ".");
            if (fail > 0 && !LogBox.IsVisible) OnToggleLog(null, new RoutedEventArgs());
        }
    }

    private static bool IsPending(QueueItem i) => i.State is ItemState.Pending or ItemState.Failed or ItemState.Cancelled;

    private void OnStop(object? s, RoutedEventArgs e) { _cts?.Cancel(); SetStatus("Durduruluyor…"); }

    private async void OnPreview(object? s, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_ffmpegOk) { SetStatus("ffmpeg bulunamadı."); return; }
        var item = QueueList.SelectedItem as QueueItem ?? _items.FirstOrDefault();
        if (item == null) { SetStatus("Önizleme için önce video ekle."); return; }

        var settings = BuildSettings(item);
        settings.PreviewSeconds = Math.Clamp((int)(PreviewLenBox.Value ?? 10), 3, 60);
        settings.OutputFile = Path.Combine(Path.GetTempPath(), "AquaEncoder_onizleme.mp4");

        _cts = new CancellationTokenSource();
        SetBusy(true);
        AppendLog($"═══ ÖNİZLEME: {item.Name} ═══");
        try
        {
            var info = await _runner.ProbeAsync(item.Video, _cts.Token);
            double start = (double)(PreviewStartBox.Value ?? 60);
            settings.PreviewStartSeconds = Math.Max(0, Math.Min(start, info.DurationSeconds - settings.PreviewSeconds));

            var progress = new Progress<EncodeProgress>(p =>
            {
                Bar.Value = p.Percent;
                StatusText.Text = $"Önizleme · %{p.Percent:0} · {p.Fps:0} fps";
            });
            await _runner.EncodeAsync(settings, progress, AppendLog, _cts.Token);
            SetStatus("Önizleme hazır, oynatıcıda açılıyor…");
            OpenPath(settings.OutputFile);
        }
        catch (OperationCanceledException) { SetStatus("Önizleme iptal edildi."); }
        catch (Exception ex)
        {
            AppendLog("HATA: " + ex.Message);
            SetStatus("Önizleme hatası (günlüğe bak).");
            if (!LogBox.IsVisible) OnToggleLog(null, new RoutedEventArgs());
        }
        finally
        {
            _cts?.Dispose(); _cts = null;
            SetBusy(false);
            Bar.Value = 0;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        StartBtn.IsEnabled = !busy && _ffmpegOk;
        StopBtn.IsEnabled = busy;
        PreviewBtn.IsEnabled = !busy;
        SettingsTabs.IsEnabled = !busy;      // çalışırken ayarlar kilitli: tüm kuyruk aynı ayarla biter
    }

    // ═══════════════════════════════════════════ Durum / günlük / yardımcılar

    private void SetStatus(string text) => StatusText.Text = text;

    private void UpdateTotals()
    {
        int done = _items.Count(i => i.State == ItemState.Done);
        TotalText.Text = _items.Count == 0 ? "" : $"{done} / {_items.Count} tamamlandı";
        TotalBar.Value = _items.Count == 0 ? 0 : done * 100.0 / _items.Count;
    }

    private void AppendLog(string line)
    {
        // ffmpeg çıktısı arka plan thread'inden gelir.
        Dispatcher.UIThread.Post(() =>
        {
            _log.AppendLine(line);
            if (_log.Length > 300_000) _log.Remove(0, 100_000);
            if (LogBox.IsVisible) { LogBox.Text = _log.ToString(); LogBox.CaretIndex = LogBox.Text.Length; }
        });
    }

    private void OnToggleLog(object? s, RoutedEventArgs e)
    {
        LogBox.IsVisible = !LogBox.IsVisible;
        LogBtn.Content = LogBox.IsVisible ? "Günlük ▾" : "Günlük ▴";
        if (LogBox.IsVisible) { LogBox.Text = _log.ToString(); LogBox.CaretIndex = LogBox.Text.Length; }
    }

    private async void OnCopyLog(object? s, RoutedEventArgs e)
    {
        var clip = GetTopLevel(this)?.Clipboard;
        if (clip == null) return;
        await clip.SetTextAsync(_log.ToString());
        SetStatus("Günlük panoya kopyalandı.");
    }

    private void OnOpenFolder(object? s, RoutedEventArgs e)
    {
        var item = QueueList.SelectedItem as QueueItem ?? _items.LastOrDefault(i => i.State == ItemState.Done);
        string? dir = item?.OutputFile != null ? Path.GetDirectoryName(item.OutputFile)
                    : _outDir ?? (item != null ? item.Folder : null);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) OpenPath(dir);
        else SetStatus("Açılacak klasör yok.");
    }

    private static string FormatSpan(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    private static void OpenPath(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS()) Process.Start("open", path);
            else Process.Start("xdg-open", path);
        }
        catch { /* açılamazsa sessizce geç */ }
    }
}

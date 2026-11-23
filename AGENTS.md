# AquaEncoder — proje notları (OpenCode / Claude için)

AquaFansub'ın kendi encoder'ı. Amaç: hiç bilmeyen biri bile hardsub (altyazı gömme) + intro ekleme
yapıp encode alabilsin. Motor: **ffmpeg**. Hedef: Windows + Linux, çok zayıf PC'lerde bile çalışsın.

## Yapı
- `src/AquaEncoder.Core` — arayüzsüz motor (NuGet bağımlılığı YOK, sadece .NET 8)
  - `Models.cs` — `EncodeSettings`, enum'lar (codec, arka uç, hız), `MediaInfo`, `EncodeProgress`
  - `FfmpegCommandBuilder.cs` — ayarlardan ffmpeg argümanı üretir (saf fonksiyon, test edilebilir)
  - `FfmpegRunner.cs` — ffprobe/ffmpeg çalıştırma, ilerleme okuma (`-progress pipe:1`), iptal, donanım tespiti
- `src/AquaEncoder.Cli` — motoru arayüzsüz denemek için küçük CLI
- `src/AquaEncoder.App` — Avalonia 11 arayüzü (koyu tema, tek pencere, code-behind)

## Önemli tasarım kararları
- **Altyazı sadece ana videoya basılır**, sonra intro ile `concat` edilir → altyazı zamanlaması kaymaz.
- Intro, ana videonun çözünürlüğüne/fps'ine uydurulur (oran bozulmaz, siyah bant eklenir).
  Intro'da ses yoksa sessizlik (`anullsrc`) üretilir.
- Donanım kodlayıcılar `-encoders` listesine güvenilmeden, **1 karelik deneme kodlamasıyla** tespit edilir.
- Altyazı filtresi yolları `EscapePath` ile kaçışlanır (Windows `C:\...` yolları için şart).
- ffmpeg, programın yanındaki `ffmpeg/` klasöründe ya da exe ile yan yana aranır; yoksa PATH.
- **Gömülü fontlar**: hardsub varsa `FfmpegRunner.ExtractFontsAsync` videodaki font attachment'larını (`-dump_attachment:INDEX`) geçici klasöre çıkarır,
  kullanıcı font klasörüyle birleştirip `fontsdir` olarak verir, encode sonunda (hata/iptal dahil) siler. `EncodeSettings.UseEmbeddedFonts` ile kapatılır.
- Kodlama süreci düşük öncelikle (BelowNormal) çalışır; PC donmasın.

## Derleme / çalıştırma
```
dotnet build
dotnet run --project src/AquaEncoder.App
dotnet run --project src/AquaEncoder.Cli -- --detect
dotnet run --project src/AquaEncoder.Cli -- --in v.mkv --sub s.ass --intro intro.mp4 --out o.mp4 --crf 23
```
Tek dosya yayın (ffmpeg'i exe'nin yanına koy):
```
dotnet publish src/AquaEncoder.App -c Release -r win-x64   --self-contained -p:PublishSingleFile=true
dotnet publish src/AquaEncoder.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## Arayüz (App) yapısı
- Konayuki tarzı: üstte araç çubuğu, ortada **kuyruk listesi**, sağda ayar sekmeleri (Kodlama / Altyazı & Intro / Ses & Çıkış), altta ilerleme + günlük.
- `App.axaml`: koyu/açık tema renkleri `ThemeDictionaries` içinde (`AqBg`, `AqPanel`, `AqCard`, `AqLine`, `AqDim`, `AqAccent`...). Her yerde `{DynamicResource Aq...}` kullan, sabit renk yazma.
- `QueueItem.cs`: kuyruk öğesi (INotifyPropertyChanged). `AppSettings.cs`: JSON ayar kaydı (%AppData%/AquaEncoder).
- `MainWindow.axaml.cs`: code-behind. Sürükle-bırak uzantıya göre video/altyazı ayırır, altyazıları isimle eşleştirir.
- Compiled binding KAPALI (csproj). Adlandırılmış kontroller için Avalonia'nın name generator'ı gerekir;
  `InitializeComponent`/alan hatası çıkarsa MainWindow ctor'una `AvaloniaXamlLoader.Load(this)` ekleyip alanları `FindControl` ile bağla.
- Core'a `PreviewSeconds` / `PreviewStartSeconds` eklendi (önizleme: çıkış tarafında -ss/-t, intro atlanır).

## Durum
- Core + CLI: derlendi, gerçek ffmpeg ile test edildi (hardsub, intro, x265, 480p, önizleme).
- App: Windows'ta derlendi ve çalışıyor (kullanıcı doğruladı). Çıkış dosyası önce geçici adla yazılıp taşınıyor (`aquaenc_*.part.mp4`), ffmpeg `-nostdin` ile çalışır.

## Yapılacaklar
> Ayrıntılı ve güncel plan: **ROADMAP.md** (faz faz, işaretli liste). Bir seferde tek adım yap.

1. App'i derle/çalıştır, hataları düzelt, ekran görüntüsüyle görünümü ince ayarla.
2. NVENC / QSV / AMF'yi gerçek GPU'lu PC'de dene.
3. Kuyruk öğesine çift tıklayınca öğeye özel ayar (kalite/intro) penceresi.
4. Kuyrukta sürükleyerek sıra değiştirme.
5. MKV çıkış, 10-bit, ses copy seçeneği.
6. İlk açılışta ffmpeg yoksa otomatik indirme.
7. `FfmpegCommandBuilder` için xUnit testleri.
8. Tek dosya publish: win-x64 ve linux-x64 (komutlar yukarıda).

<div align="center">

# 🎞️ AquaCoder

**AquaFansub için hazırlanmış, ffmpeg tabanlı hardsub + intro encoder.**
Hiç bilmeyen biri bile üç adımda altyazılı, intro'lu videoyu encode edebilsin.

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-informational)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![UI](https://img.shields.io/badge/UI-Avalonia%2011-8B5CF6)
![Engine](https://img.shields.io/badge/engine-ffmpeg-007808)
![Durum](https://img.shields.io/badge/durum-geliştiriliyor-orange)

</div>

---

## ✨ Özellikler

**Altyazı ve intro**
- 🔤 Hardsub: `.ass`, `.ssa`, `.srt` altyazıyı videoya gömer (Türkçe karakter ve boşluklu/özel karakterli yollar dahil)
- 🎬 Intro ekleme: ana videonun çözünürlüğüne ve fps'ine otomatik uyar, oran bozulmaz; intro'da ses yoksa sessizlik üretir
- ⏱️ Altyazı zamanlaması intro'dan etkilenmez (altyazı yalnızca ana videoya basılır)
- 🅰️ Özel font klasörü desteği

**Kodlama**
- 🖥️ İşlemci: **x264 / x265**
- 🚀 Donanım hızlandırma: **NVIDIA (NVENC)**, **Intel (Quick Sync)**, **AMD (AMF)**
- 🔍 Donanım otomatik taranır; yalnızca gerçekten çalışan kodlayıcılar listelenir
- 🎚️ CRF / CQ kalite ayarı, hız ön ayarı, çözünürlük (1080p / 720p / 480p)
- 🐢 **Zayıf PC modu**: hızlı ön ayar, sınırlı çekirdek, düşük öncelik (bilgisayar donmaz)

**Arayüz**
- 📋 Kuyruk tabanlı: birden fazla bölümü ekle, sırayla encode et
- 🖱️ Sürükle-bırak: video, altyazı veya klasör bırak; aynı isimli altyazılar otomatik eşleşir
- 🌗 Koyu / açık tema
- 👁️ Önizleme: seçtiğin saniyeden 10 saniyelik deneme encode'u
- 📊 İlerleme, fps, hız ve kalan süre tahmini
- 📝 Kopyalanabilir günlük
- 💾 Son kullanılan ayarlar otomatik kaydedilir

> Planlanan özellikler için [ROADMAP.md](ROADMAP.md) dosyasına bak.

---

## 🚀 Kullanım

1. **Videoyu ekle** — sürükle-bırak veya **＋ Video Ekle**
2. **Altyazıyı kontrol et** — aynı isimli altyazı otomatik gelir, gerekirse sağdaki *Altyazı & Intro* sekmesinden değiştir
3. **Intro seç** (isteğe bağlı) — tüm videolara eklenir
4. **▶ Başlat**

Emin değilsen: profil olarak **Dengeli**, kodlayıcı olarak listedeki ilk donanım seçeneğini (varsa) kullan.
Altyazının doğru göründüğünü hızlıca görmek için **◐ Önizle** kullan.

### Kalite ayarı (CRF)

| CRF | Anlamı |
|-----|--------|
| 18–19 | Çok yüksek kalite, büyük dosya |
| 20–23 | Yüksek kalite (önerilen aralık) |
| 24–27 | Dengeli |
| 28+ | Küçük dosya, belirgin kalite kaybı |

Donanım kodlayıcılar (NVENC, QSV, AMF) hızlıdır ama aynı kalite için işlemci (x264/x265) kodlamasından biraz daha büyük dosya üretir.

---

## 📦 Kurulum

### Gereksinimler
- Windows 10/11 veya Linux
- **ffmpeg** ve **ffprobe** (PATH'te ya da programın yanında)
  - Windows: `winget install Gyan.FFmpeg`
  - Ya da [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) üzerinden "full" yapıyı indirip `ffmpeg.exe` ve `ffprobe.exe` dosyalarını uygulamanın yanına koy

### Kaynaktan çalıştırma
Geliştirme için [.NET 8 SDK](https://dotnet.microsoft.com/download) gerekir.

```bash
git clone https://github.com/KULLANICI_ADIN/aquacoder.git
cd aquacoder
dotnet build
dotnet run --project src/AquaEncoder.App
```

### Tek dosya yayın

```bash
dotnet publish src/AquaEncoder.App -c Release -r win-x64   --self-contained -p:PublishSingleFile=true
dotnet publish src/AquaEncoder.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

Çıktı `src/AquaEncoder.App/bin/Release/net8.0/<platform>/publish/` altında oluşur.

---

## 🧰 Komut satırı aracı

Arayüz olmadan motoru denemek için küçük bir CLI var:

```bash
# Hangi donanım kodlayıcılar çalışıyor?
dotnet run --project src/AquaEncoder.Cli -- --detect

# Hardsub + intro
dotnet run --project src/AquaEncoder.Cli -- --in video.mkv --sub altyazi.ass --intro intro.mp4 --out cikti.mp4 --crf 23

# Donanım, H.265, 720p, önizleme (60. saniyeden 10 sn)
dotnet run --project src/AquaEncoder.Cli -- --in video.mkv --sub altyazi.ass --out o.mp4 --backend nvidia --h265 --height 720 --preview 10 --start 60
```

| Bayrak | Açıklama |
|--------|----------|
| `--in`, `--sub`, `--intro`, `--out` | Dosyalar |
| `--crf N` | Kalite (varsayılan 23) |
| `--backend` | `cpu` · `nvidia` · `qsv` · `amf` |
| `--h265` | H.265 / HEVC kullan |
| `--height` | `1080` · `720` · `480` |
| `--fonts` | Özel font klasörü |
| `--preview N --start S` | S. saniyeden N saniyelik önizleme |
| `--verbose` | ffmpeg komutunu ve çıktısını göster |

---

## 🗂️ Proje yapısı

```
src/
├── AquaEncoder.Core   # Arayüzsüz motor: komut oluşturucu, ffprobe, ilerleme, donanım tespiti
├── AquaEncoder.Cli    # Motoru test etmek için komut satırı aracı
└── AquaEncoder.App    # Avalonia arayüzü (kuyruk, ayarlar, tema)
```

**Tasarım notları**
- Altyazı yalnızca ana videoya basılır, sonra intro ile birleştirilir; böylece zamanlama kaymaz.
- Donanım kodlayıcılar, `ffmpeg -encoders` listesine güvenilmeden 1 karelik deneme kodlamasıyla doğrulanır.
- Çıkış önce geçici dosyaya yazılır, bitince asıl adına taşınır; hata veya iptalde yarım dosya kalmaz.
- Kodlama düşük öncelikte çalışır, bilgisayar donmaz.

---

## ❓ Sık karşılaşılan sorunlar

| Sorun | Çözüm |
|-------|-------|
| `ffmpeg bulunamadı` | ffmpeg ve ffprobe'u PATH'e ekle ya da uygulamanın yanına koy |
| Listede ekran kartı kodlayıcısı yok | Ekran kartı sürücüsünü güncelle; yine yoksa işlemci kodlayıcısını kullan |
| Altyazı yanlış fontla çıkıyor | *Altyazı & Intro* sekmesinden font klasörünü seç |
| Windows "bilgisayarınızı korudu" uyarısı | Yeni imzasız programlarda normaldir: *Ek bilgi → Yine de çalıştır* |
| Çıkış dosyası yazılamıyor | Başka bir çıkış klasörü seç; güvenlik yazılımının engellemediğini kontrol et |

Bir hata olursa alttaki **Günlük** bölümünü açıp **Günlüğü kopyala** ile içeriği paylaş.

---

## 🛣️ Yol haritası

Gömülü font çıkarma, akış seçimi, dosya adı şablonu ve CRC32, profiller, hedef dosya boyutu (2-pass), AV1, 10-bit, logo bindirme ve çok daha fazlası için [ROADMAP.md](ROADMAP.md).

---

<div align="center">

**AquaFansub** için ❤️ ile yapıldı

</div>

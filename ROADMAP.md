# AquaEncoder — Yol Haritası

Amaç: AquaFansub için, hiç bilmeyen birinin bile hardsub + intro encode alabildiği; ffmpeg'in yapabildiği
her şeyi sade bir arayüzde sunan, zayıf PC'lerde de rahat çalışan bir araç.

Her faz küçük, tek tek yapılabilen adımlardan oluşur. Her adım bitince: derle → gerçek bir videoyla test et → işaretle.
Durum: `[x]` yapıldı · `[ ]` yapılacak

---

## Faz 0 — Temel (yapıldı)
- [x] Core motoru: komut oluşturucu, ffprobe, ilerleme, iptal
- [x] Hardsub (.ass/.srt), intro ekleme (çözünürlük/fps/ses uyumlu)
- [x] CPU (x264/x265), NVIDIA, Intel QSV, AMD AMF; gerçek deneme kodlamasıyla donanım tespiti
- [x] CRF/CQ, hız ön ayarı, çözünürlük, zayıf PC modu
- [x] Kuyruk tabanlı arayüz, sürükle-bırak, açık/koyu tema, önizleme, günlük, ayar kaydı
- [x] Geçici dosyaya yazıp taşıma, `-nostdin`

---

## Faz 1 — Fansub için olmazsa olmazlar (ÖNCELİKLİ)
1. [x] **Gömülü fontları otomatik çıkar** — videodaki attachment fontlarını (`-dump_attachment`) geçici klasöre çıkar, altyazı filtresine `fontsdir` olarak ver. Özel stil fontları doğru görünür.
2. [ ] **Akış seçimi + medya bilgisi paneli** — ffprobe ile ses/altyazı akışlarını (dil, başlık, codec) listele; kullanıcı seçsin. Sayı ile değil isimle ("Japonca AAC").
3. [ ] **Videonun içindeki altyazıyı hardsub'a bas** — harici dosya yoksa gömülü ASS/SRT akışını seç (`subtitles=...:si=N`).
4. [ ] **Çıkış dosya adı şablonu** — `{isim}`, `{bolum}`, `{cozunurluk}`, `{codec}`, `{crc}`; **CRC32 otomatik ekleme** (`[AB12CD34]`, fansub standardı).
5. [ ] **Profiller** — ayarları isimle kaydet/yükle/dışa aktar ("AquaFansub 1080p", "720p web", "Telegram 480p"). Ekip aynı profili paylaşsın (JSON).
6. [ ] **ffmpeg yönetimi** — yoksa otomatik indir, sürüm göster, "ffmpeg yolu" ayarı, güncelle.
7. [ ] **Hedef dosya boyutu** — "100 MB olsun" yaz, bitrate hesaplanıp 2-pass/ABR ile uygulansın (Telegram/Discord sınırları için hazır seçenekler).
8. [ ] **Hata dostu mesajlar** — sık hatalar (izin, bozuk altyazı, eksik font, codec desteklenmiyor) için Türkçe açıklama + çözüm önerisi.
9. [ ] **Kuyruk iyileştirme** — sürükleyerek sıralama, öğeye özel ayar (çift tık), duraklat/devam, hatalıları yeniden dene.
10. [ ] **xUnit testleri** — komut oluşturucu, yol kaçışı, dosya adı şablonu, hedef boyut hesabı.

---

## Faz 2 — Kodlama seçenekleri (ffmpeg'in sunduğu)
- [ ] Çıkış kabı: MP4 / **MKV** (altyazı/font/bölüm koruyarak)
- [ ] Codec: **AV1** (SVT-AV1, NVENC/QSV AV1), VP9
- [ ] **10-bit** çıkış (`yuv420p10le`), HEVC 10-bit
- [ ] Bitrate modları: CRF / CQ / VBR / CBR / 2-pass
- [ ] **x264/x265 gelişmiş**: `tune animation`, deblock, aq-mode, psy-rd, `x265-params` (anime için hazır ayar seti)
- [ ] Ses: kopyala / AAC / Opus / FLAC; **ses normalizasyonu** (`loudnorm`); ses gecikmesi; kanal düzeni
- [ ] Çoklu ses akışı koruma (dil seçimli)
- [ ] Kare hızı dönüştürme, VFR→CFR
- [ ] GPU seçimi (birden fazla ekran kartı), donanım decode (`-hwaccel`)
- [ ] Thread / öncelik / bellek ayarı (zayıf PC için "ağır işlem" profili)

---

## Faz 3 — Görüntü ve altyazı araçları
- [ ] **Logo / watermark** bindirme (konum, boyut, opaklık, başlangıç-bitiş süresi)
- [ ] **Outro** ekleme (intro'ya ek olarak), videonun ortasına ek bölüm
- [ ] Altyazı: gecikme/offset, `force_style` ile SRT stili, çoklu altyazı, ASS font eksikse uyarı
- [ ] **Kırpma (crop)**: `cropdetect` ile otomatik siyah bant temizleme
- [ ] **Deinterlace** (yadif/bwdif), **denoise** (hqdn3d/nlmeans), **deband** (gradfun/deband), keskinleştirme
- [ ] **HDR → SDR** tonemap, renk uzayı düzeltme (bt601/bt709)
- [ ] Yükseltme (upscale) algoritmaları: lanczos/spline, `libplacebo`
- [ ] (İsteğe bağlı) Real-ESRGAN / waifu2x entegrasyonu — intro'nu zaten waifu2x ile üretmişsin; harici araçla köprü

---

## Faz 4 — Yardımcı araçlar (ayrı sekme)
- [ ] **Kes / birleştir** (trim, concat), kodlamadan hızlı kesme
- [ ] **Remux** (kodlamadan kap değiştir), **softsub mux** (altyazı + font + bölüm + dil/başlık etiketi ekleme)
- [ ] Altyazı çıkar, SRT ↔ ASS dönüştür; ses / font / bölüm çıkar
- [ ] **Bölüm (chapter) düzenleyici**
- [ ] Ekran görüntüsü, GIF/WebP, küçük resim şeridi
- [ ] **Kalite ölçümü**: VMAF / SSIM / PSNR; **yan yana karşılaştırma önizlemesi** (orijinal | encode)
- [ ] "ffmpeg komutunu göster / kopyala" + **özel ekstra argüman** kutusu (ileri kullanıcı)
- [ ] Aynı anda hem hardsub hem softsub çıktısı

---

## Faz 5 — Otomasyon ve kullanım kolaylığı
- [ ] **Klasör izleme** — klasöre düşen video + altyazı otomatik kuyruğa girsin
- [ ] Bitince bildirim / ses; bitince bilgisayarı kapat / uyut
- [ ] Çoklu iş paralelliği (güçlü PC'de 2 encode birlikte)
- [ ] Arayüz dili (TR/EN), ilk açılış sihirbazı ("Başlangıç: 3 adım")
- [ ] Tahmini boyut ve süre göstergesi (encode başlamadan)
- [ ] İstatistik: toplam encode süresi, tasarruf edilen boyut

---

## Faz 6 — Dağıtım
- [ ] Tek dosya yayın: win-x64, linux-x64 (self-contained)
- [ ] Uygulama ikonu, sürüm numarası, "Hakkında" penceresi
- [ ] Çökme günlüğü (dosyaya yaz, tek tıkla kopyala)
- [ ] Güncelleme kontrolü (GitHub releases)
- [ ] Windows kurulum paketi + **kod imzalama** (Defender/SmartScreen uyarılarını kalıcı çözer)
- [ ] Kullanım kılavuzu (kısa, resimli, Türkçe)

---

## Önerilen sıra (ilk 6 adım)
1. Gömülü fontları çıkar
2. Akış seçimi + medya bilgisi paneli
3. Gömülü altyazıyı hardsub'a basma
4. Dosya adı şablonu + CRC32
5. Profiller (kaydet/yükle)
6. Hedef dosya boyutu (2-pass)

Neden bu sıra: ilk üçü ekibin her bölümde yaşadığı gerçek sorunları (font, akış seçimi, harici altyazı olmaması) çözer;
sonraki üçü günlük iş akışını hızlandırır. Faz 2 ve sonrası bunların üstüne rahatça eklenir.

## Çalışma düzeni
- Bir seferde TEK adım. Her adımın sonunda: `dotnet build` + CLI ile gerçek test + kısa not.
- Motor mantığı `Core`'da, arayüz `App`'te kalsın. Core'daki her yeni özellik için CLI'dan denenebilir bir bayrak ekle.
- Ffmpeg komutuna dokunan her değişiklik, `FfmpegCommandBuilder` için bir test ile gelsin.

# SysScrub — Yol Haritası

> Bu belge dört bakış açısıyla yazıldı: **stratejist** sırayı kurdu, **analitik uzman**
> sayıları ve riskleri koydu, **yaratıcı** alternatifleri önerdi, **eleştirmen** her
> maddeyi "bu gerçekten böyle mi?" diye sınadı. Eleştirmenin itirazları silinmedi;
> maddelerin altında duruyor.

Son güncelleme: 2026-08-13 · Sürüm: 0.11.0-alpha

---

## 1. Bugün nerede duruyoruz

| Alan | Durum | Kanıt |
|---|---|---|
| Modüller | **9/9 çalışıyor** | Panel, Temizleyici, Registry, Sürücüler, Güncellemeler, Başlangıç, Programlar, Disk sağlığı, Disk analizi, Zaman tüneli, Ayarlar |
| Test | **453 test geçiyor** | `dotnet test` |
| Paketleme | **Çalışıyor** | `dist/` → exe + kurulum paketi, `build/publish.ps1` |
| Yayın | **Yapılmadı** | GitHub'a hiç push edilmedi, hiç Release yok |
| Çeviri | **6 dil altyapısı var, kapsam kısmi** | Karşılama turu + gezinme + Ayarlar tam; modül ekranları Türkçe |
| Arka plan modu | **Yok** | Uygulama kapanınca hiçbir şey çalışmıyor |
| Sürücü kurulumu | **Yok** | Buluyor, kuramıyor |

**Gerçek makinede doğrulananlar** — bunlar iddia değil, ölçüm:

- C:\ tamamı taranıyor: 335,7 GB · 2.134.737 dosya · 348.427 klasör · **78 saniye**
- `C:\Program Files` toplamı PowerShell'inkiyle **birebir** aynı, tarama **3,5 kat hızlı**
- NVMe sıcaklığı Windows'un kendi ölçümüyle **birebir** (47 °C)
- 182 programın **180**'inin kaldırma komutu doğru çözüldü; kalan 2'sinin kaldırıcısı gerçekten silinmiş
- Zamanlanmış görev kuruldu → sorgulandı → kaldırıldı, makinede iz bırakmadı

> **Eleştirmen:** "9/9 çalışıyor" cümlesi yanıltıcı olabilir. Sürücüler modülü sürücü
> *bulur* ama *kuramaz*; Panel modüllerin verisine henüz bağlı değil. Tablodaki
> "çalışıyor", "ekran gerçek veri gösteriyor ve iddia ettiği işi yapıyor" demek —
> "modül tamamlandı" demek değil. Eksikler aşağıda madde madde duruyor.

---

## 2. Sıra

Üç kuşak halinde. Her kuşak kendi içinde bitirilmeden sonrakine geçilmemeli.

### 🔴 Kuşak 1 — Yayına çıkmak (tahmini 1 gün)

Bu kuşak bitmeden yapılan hiçbir iş kimseye ulaşmıyor.

| # | İş | Neden şimdi | Emek | Risk |
|---|---|---|---|---|
| 1 | **GitHub'a push** | 13 commit yerel duruyor. Yedeklenmemiş tek kopya. | 10 dk | Yok |
| 2 | **README (tr + en) + ekran görüntüleri** | Repoya giren ilk kişi 30 saniyede ne olduğunu anlamalı. 9 ekran görüntüsü zaten üretildi. | 2 sa | Yok |
| 3 | **CI'ı yeşile boyamak** | `ci.yml` yazıldı ama hiç koşmadı. İlk koşuda kırılması normal. | 30 dk | Düşük |
| 4 | **v0.11.0-alpha Release** | `dist/` yalnızca senin denemen için; Releases herkes için. | 30 dk | Düşük |

> **Analitik:** Kod imzası olmadığı için indirilen `setup.exe` SmartScreen uyarısı
> verecek. Sertifika ~250–400 $/yıl. Şimdilik README'de ekran görüntülü açıklama
> yeterli; ilk yüz indirmeden sonra tekrar değerlendirilir.

> **Eleştirmen:** 2 numarayı "sonra yaparım" diye atlama isteği güçlü olacak.
> README'siz bir repo, çalışan bir uygulamayı bile ölü gösterir. Bu madde 1'den
> ayrılamaz.

---

### 🟠 Kuşak 2 — Uygulamayı tamamlamak (tahmini 5–7 gün)

| # | İş | Ne eksik | Emek | Risk |
|---|---|---|---|---|
| 5 | **Panel'i canlandır** | Bütün modüller veri üretiyor ama Panel hâlâ statik. Akıllı öneri kartları, son temizlik özeti, disk sağlığı rozeti buraya bağlanmalı. | 0,5 gün | Düşük |
| 6 | **Sürücü kurulum zinciri** | İmza doğrula → yedekle → geri yükleme noktası → kur → cihaz durumunu yeniden oku → bozulduysa geri al. | 1,5 gün | **Yüksek** |
| 7 | **Arka plan modu + sistem tepsisi** | Uygulama kapanınca hiçbir şey çalışmıyor. Asıl değer burada: kullanıcı açmayı unutsa bile diski ısındığında haberi olur. | 2 gün | Orta |
| 8 | **Modül ekranlarının çevirisi** | Altyapı hazır; ~1.300 dize kalan iş. Mekanik ama uzun. | 1,5 gün | Düşük |
| 9 | **Komut paleti (Ctrl+K)** | Uygulamayı anında modern hissettiren tek özellik. | 0,5 gün | Düşük |
| 10 | **Program artık taraması** | Kaldırma sonrası kurulum klasörünü buluyor; AppData/ProgramData/registry kalıntılarını taramıyor. | 0,5 gün | Orta |

> **Analitik — 6 numara neden yüksek riskli:** Yanlış sürücü kurmak makineyi
> açılmaz hâle getirebilir. Zincirdeki her adım (imza, yedek, geri yükleme noktası)
> pazarlık konusu değil. Sanal makinede en az üç tur denenmeden yayınlanmamalı.

> **Yaratıcı — 7 numara için alternatif:** Ayrı bir Windows servisi yazmak yerine
> ana uygulamanın kendisi tepsiye insin ve WPF görsel ağacını tamamen serbest
> bıraksın. Tek süreç, tek kod tabanı, tek güncelleme yolu. Hedef: **< 50 MB RAM,
> boşta %0 CPU** — ölçülüp README'ye yazılacak.

> **Eleştirmen:** 8 numara "mekanik" görünüyor ama 1.300 dizeyi anadili konuşanların
> incelemesi olmadan yayınlamak, beş dilde birden kötü bir izlenim bırakma riski.
> Öneri: kapsam göstergesi zaten var — çeviriler geldikçe yüzde yükselsin, bir
> seferde "tamam" denmesin. Katkı çağrısı README'ye konsun.

---

### 🟡 Kuşak 3 — Kapsam ve olgunluk (tahmini 5+ gün)

| # | İş | Not | Emek |
|---|---|---|---|
| 11 | Microsoft Update Catalog + üretici uç noktaları | Driver Booster paritesinin kalan katmanları. Resmî API'si yok, kırılgan — izole edilmeli, düşerse sessizce devre dışı kalmalı. | 2 gün |
| 12 | USB disk S.M.A.R.T. (SAT katmanı) | Harici diskler şu an "okunamıyor" diyor. Gerçek donanımla denenmeli. | 0,5 gün |
| 13 | Otomatik güncelleme | Dağıtımı GitHub olan bir uygulama için şart; yoksa herkes ilk indirdiği sürümde kalır. | 0,5 gün |
| 14 | Windows 11 debloat presetleri | Programlar modülünün üzerine kurulu. Geri kurulamayanlar için ayrı uyarı. | 0,5 gün |
| 15 | Sıcaklık geçmişi grafiği | Disk sağlığı arka planda örneklesin, grafik çizsin. 7 numaraya bağlı. | 0,5 gün |
| 16 | İlk açılış profil sihirbazı | Normal / Oyuncu / Teknisyen / Geliştirici. Karşılama turunun içine oturur. | 0,5 gün |
| 17 | Erişilebilirlik + DPI denetimi | %125–%200 ölçekleme, tam klavye gezinme, WCAG AA kontrast. | 1 gün |
| 18 | Teknisyen raporu (HTML/PDF) | Tek tuşla tam sistem sağlık raporu. Başkasının bilgisayarına bakan herkesin işine yarar. | 0,5 gün |

---

## 3. Bilinen eksikler ve sınırlar

Dürüstlük bölümü. Bunlar hata değil, henüz yapılmamış ya da bilerek yapılmamış şeyler.

| Konu | Durum | Neden |
|---|---|---|
| Kod imzası | Yok | Sertifika maliyeti. SmartScreen uyarısı çıkıyor. |
| USB disk S.M.A.R.T. | Okunamıyor | Üreticiye özel USB köprüleri (JMicron, ASMedia…) henüz yazılmadı. |
| Sürücü kurulumu | Yok | Kurulum zinciri güvenlik adımlarıyla birlikte yazılacak. |
| Modül ekranları çevirisi | Türkçe | Altyapı hazır, dizeler taşınmadı. Kapsam Ayarlar'da görünüyor. |
| Anadil incelemesi | Yapılmadı | de/ja/ko/zh-Hans çevirileri inceleme bekliyor. |
| Arka plan izleme | Yok | Uygulama kapalıyken hiçbir şey çalışmıyor. |
| RAID / donanım denetleyicileri | Desteklenmiyor | Intel RST, MegaRAID vb. yolları yazılmadı. |

---

## 4. Değişmeyecek ilkeler

Bunlar özellik değil, sınır. Yeni bir madde bunlardan biriyle çelişiyorsa madde değişir.

1. **Emin değilsek dokunmayız.** Bir dosyanın ya da kaydın gerçekten gereksiz olduğundan
   emin olamıyorsak listeye hiç almıyoruz. Az bulmak, yanlış silmekten iyidir.
2. **Her silme geri alınabilir.** Karantina + zaman tüneli + geri yükleme noktası.
   Kalıcı silme yalnızca kullanıcı açıkça isterse.
3. **Uydurma sayı yok.** Ölçemediğimiz şeyi tahmin edip göstermiyoruz. Açılış gecikmesi
   olay günlüğünden, program boyutu klasör taramasından, disk sıcaklığı diskin kendisinden.
4. **Bilmediğimize "iyi" demiyoruz.** Veri okunamadıysa durum "bilinmiyor" kalır.
   Yeşil rozet yanlış güven verir.
5. **Hesap yok, telemetri yok, reklam yok, ücretli sürüm duvarı yok.**
6. **Gösterilip de bağlanmamış kontrol yok.** Yapamadığımızı ekranda gizlemiyoruz.

---

## 5. Katkı vermek isteyenler için

En kolay giriş noktaları — hiçbiri kod mimarisi bilmeyi gerektirmiyor:

| Katkı | Nereye | Zorluk |
|---|---|---|
| **Çeviri** | `data/i18n/*.json` — tek dosya, düz anahtar/değer | Kolay |
| **Yeni dil** | Aynı klasöre yeni bir `<kültür>.json` — kod değişikliği gerekmez | Kolay |
| **Temizleme kuralı** | `data/rules/*.json` — yeni hedef eklemek JSON eklemek | Kolay |
| **S.M.A.R.T. özniteliği** | `data/smart-attributes.json` — üreticiye özel kimlikler | Kolay |
| Modül ekranı çevirisi | XAML'de sabit metni `{loc:Str Anahtar}` ile değiştir | Orta |
| Yeni registry tarayıcısı | `src/SysScrub.Core/RegistryCleaning/Scanners/` | Zor |

Çeviri katalogları için tutarlılık testleri var: eksik yer tutucu, fazladan anahtar
ve boş değer derleme zamanında yakalanıyor.

---

## 6. Sürüm hedefleri

| Sürüm | İçerik | Ne zaman |
|---|---|---|
| **0.11.0-alpha** | *Bugün.* 9 modül + çok dilli altyapı + karşılama turu | ✅ |
| 0.12.0-alpha | Kuşak 1 tamam: GitHub'da, README'li, ilk Release yayında | +1 gün |
| 0.15.0-beta | Kuşak 2 tamam: Panel canlı, sürücü kurulumu, arka plan modu, çeviri tamam | +1 hafta |
| **1.0.0** | Kuşak 3'ün büyük kısmı + erişilebilirlik denetimi + anadil incelemesi | +3 hafta |

> **Stratejist:** Bu tarihler tek kişilik yoğun çalışma varsayımıyla. Yarım günlük
> tempoda üçe katlanır. Önemli olan sıra, tarih değil.

> **Eleştirmen — son söz:** Bu yol haritasının en büyük riski teknik değil.
> Dokuz modül çalışıyor ve hiçbiri yayında değil. Kuşak 1 bir günlük iş ve tek
> başına projenin var olup olmadığını belirliyor. Kuşak 2'ye başlamadan önce
> bitirilmeli.

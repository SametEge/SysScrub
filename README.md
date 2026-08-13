<div align="center">

<img src="docs/assets/banner.png" alt="SysScrub" width="100%">

**Windows bakım, sürücü güncelleme ve disk sağlığı — tek uygulamada.**

[![Sürüm](https://img.shields.io/github/v/release/SametEge/SysScrub?include_prereleases&label=s%C3%BCr%C3%BCm&color=FF6B2C)](https://github.com/SametEge/SysScrub/releases/latest)
[![Lisans](https://img.shields.io/badge/lisans-MIT-FF6B2C)](LICENSE)
[![Derleme](https://github.com/SametEge/SysScrub/actions/workflows/ci.yml/badge.svg)](https://github.com/SametEge/SysScrub/actions/workflows/ci.yml)

### [⬇ Son sürümü indir](https://github.com/SametEge/SysScrub/releases/latest)

[English](README.en.md)

</div>

---

<img src="docs/assets/screens/dashboard.png" alt="SysScrub paneli" width="100%">

## Ne işe yarar

Üç ayrı programın işini, gerçekten tasarlanmış tek bir arayüzde topluyor.

| | |
|---|---|
| 🧹 **Temizleyici** | Windows, tarayıcı ve uygulama artıklarını kural tabanlı tarar. Sildiği her şey karantinaya gider, tek tıkla geri gelir. |
| 🗂 **Registry** | Hedefi kaybolmuş kayıtlar için on iki tarayıcı. Hiçbir şey silinmeden önce `.reg` yedeği ve geri yükleme noktası alınır. |
| ⚙️ **Sürücüler** | Donanımını tanır, eski sürücüleri bulur. Güncellemeler Windows Update'ten gelir — WHQL imzalı ve Microsoft tarafından o donanım için onaylanmış. |
| 📥 **Güncellemeler** | winget üzerinden kurulu programların yeni sürümleri. Her paket kendi üreticisinin kaynağından iner. |
| 🚀 **Başlangıç** | Açılışta çalışan her şey tek listede. Kapatma Windows'un kendi mekanizmasını kullanır, Görev Yöneticisi ile senkron kalır. |
| 📦 **Programlar** | Toplu kaldırma. Sonuç, kaydın gerçekten kaybolup kaybolmadığına bakılarak doğrulanır — çıkış kodu güvenilir değil. |
| 💽 **Disk sağlığı** | S.M.A.R.T. ve NVMe sağlık verisini okur: sıcaklık, açık kalma süresi, yazılan toplam veri, kalan ömür. Ham değerin yanında ne anlama geldiğini de yazar. |
| 📊 **Disk analizi** | Alanı ne yiyor? Treemap görünümü, en büyük dosyalar ve üç aşamalı yinelenen dosya bulucu. |
| 🕓 **Zaman tüneli** | Uygulamanın sistemde yaptığı her değişiklik tek bir kronolojik kayıtta. İstediğin noktaya geri dön. |

## Neden bir tane daha temizleyici

Mevcutların hepsinde rahatsız edici bir şey var: abartılı kazanç sayıları, geri alınamayan
silmeler, arka planda şişen servisler, ücretli sürüm duvarları, telemetri.

SysScrub'ın duruşu:

- **Tarama hiçbir şey silmez.** Her modül önce okur, ne bulduğunu ve neden bulduğunu gösterir,
  bekler. Neyin gideceğine sen karar verirsin.
- **Hiçbir şey geri alınamaz değil.** Temizlik, registry, sürücü, başlangıç — her değişiklik
  tek bir zaman tünelinde tutulur, herhangi bir noktaya dönülür.
- **Sayılar gerçek.** Kazanılan alan tahmin edilmez, işlem öncesi ve sonrası diskten ölçülür.
  Uydurma "sistem %40 hızlandı" yok.
- **Bilmediğinde bilmediğini söyler.** S.M.A.R.T. verisi okunamayan bir disk listeden
  kaybolmaz ve yeşil rozet almaz — neden okunamadığını yazar.
- **Hesap yok, reklam yok, telemetri yok, ücretli sürüm yok.**

---

## Modüller

### Temizleyici

<img src="docs/assets/screens/cleaner.png" alt="Temizleyici" width="100%">

Windows, tarayıcılar, uygulamalar, oyun platformları, geliştirici araçları ve gizlilik
izlerini kapsayan 48 kural. Her biri neyi sildiğini ve sonucunun ne olacağını yazar —
rahatsız edici kısımlar dâhil ("Windows.old silinince önceki Windows sürümüne geri
dönülemez").

Kurallar **koda gömülü değil, veri**: [`data/rules/*.json`](data/rules) içinde yaşıyorlar.
Yeni bir temizlik hedefi eklemek JSON eklemek demek, programı değiştirmek değil.

Tek bir dosya silinmeden önce bir güvenlik denetiminden geçiyor: korumalı Windows klasörleri,
belgelerin, bağlantı noktaları (junction ve symlink asla takip edilmez) ve bulut yer tutucusu
dosyalar reddediliyor. Düz bir temp klasörü dışındaki her şey önce karantinaya gidiyor.

### Registry

<img src="docs/assets/screens/registry.png" alt="Registry temizleyici" width="100%">

On iki tarayıcı: paylaşılan DLL sayaçları, dosya uzantıları, ProgID ve CLSID kayıtları, COM
bileşenleri, tip kütüphaneleri, kabuk uzantıları, kaldırma girdileri, uygulama yolları,
başlangıç kayıtları, MUICache, yükleyici klasörleri ve ses olayları.

Her bulgu tam anahtar yolunu **ve neden ölü sayıldığını** gösteriyor. Silmeden önce etkilenen
her anahtarın `.reg` dışa aktarımı yazılıyor, üstüne bir sistem geri yükleme noktası
alınıyor. Yedek başarısız olursa hiçbir şey silinmiyor.

Windows'un çalışması için gereken anahtarlar — servisler, DriverStore, WinSxS, bileşen
bakımı, .NET, Defender — sabit kodlu bir dokunulmaz listede.

### Sürücüler

<img src="docs/assets/screens/drivers.png" alt="Sürücü güncelleme" width="100%">

SetupAPI ile donanım envanteri, ardından kaynak olarak Windows Update. Liste iki dürüst
gruba ayrılıyor: Windows Update'in gerçekten yenisini sunduğu sürücüler ve iki yıldan eski
olup hiçbir kaynağın yenisini sunmadığı sürücüler. İkinci gruba "güncel değil" değil, "eski
olabilir" deniyor — çünkü bilmiyoruz.

Hiçbir şey kurulmadan önce tüm üçüncü parti sürücüler tek tıkla bir yedek klasörüne
aktarılabiliyor.

### Disk sağlığı

<img src="docs/assets/screens/disk-health.png" alt="Disk sağlığı" width="100%">

NVMe sağlık günlüğü (sayfa 0x02) ve ATA S.M.A.R.T. doğrudan diskten okunuyor. Sıcaklık, açık
kalma süresi, açılma sayısı, yazılan toplam veri, kalan ömür, yedek bloklar, güvensiz
kapanmalar, düzeltilemeyen hatalar — her birinin yanında sade bir okuma ile.

Üreticiye özel öznitelik anlamları [`data/smart-attributes.json`](data/smart-attributes.json)
içinde; yeni bir üreticiyi desteklemek kod değişikliği değil, tablo satırı eklemek.

### Disk analizi

<img src="docs/assets/screens/disk-analysis.png" alt="Disk analizi" width="100%">

Tüm diskin squarified treemap görünümü, en büyük dosyalar ve dosya türü dağılımı. Salt
okunur: hiçbir dosya silinmiyor, hatta açılmıyor. Bulut dosyaları indirilmiyor — diskte yer
kaplamadıkları için sayılmıyorlar da. Okunamayan klasörler sessizce atlanmıyor, sayılıp
bildiriliyor.

Yinelenen dosya bulucu üç aşamada karşılaştırıyor — boyut, sonra ilk ve son 4 KB, sonra tam
SHA-256 — böylece yalnızca zorunlu olduğu kadarını hash'liyor.

### Başlangıç ve Programlar

<img src="docs/assets/screens/startup.png" alt="Başlangıç yöneticisi" width="100%">

Run ve RunOnce anahtarları (her iki registry görünümü), başlangıç klasörleri, oturum açma
tetikleyicili zamanlanmış görevler ve Microsoft dışı otomatik başlayan servisler. Kapatma,
Görev Yöneticisi'nin kullandığı `StartupApproved` deposuna yazıyor; ikisi asla çelişmiyor.
Açılış gecikmesi tahmin değil, Windows'un Diagnostics-Performance olay günlüğünden okunan
gerçek ölçüm.

Kaldırıcı, her programın kendi kaldırma programını çalıştırıyor ve sonucu registry kaydının
gerçekten kaybolup kaybolmadığına bakarak doğruluyor.

### Zaman tüneli

<img src="docs/assets/screens/timeline.png" alt="Zaman tüneli" width="100%">

Her çalıştırma kaydediliyor: ne silindi, kaç bayt, hangi kural, geri alınabilir mi.
Karantinaya alınan temizlikler tek tıkla geri geliyor.

---

## Kurulum

**Kurulumlu:** [Releases](https://github.com/SametEge/SysScrub/releases/latest) sayfasından
`SysScrub-Setup-*.exe` dosyasını indirip çalıştır.

**Taşınabilir:** `SysScrub-*-portable-x64.zip` dosyasını çıkart ve çalıştır — kurulum
gerekmez. Klasörün yanına boş bir `portable.flag` dosyası koyarsan uygulama tüm ayar ve
günlüklerini kendi klasöründe tutar, sisteme hiçbir şey yazmaz (USB'den çalıştırmak için).

> **SmartScreen uyarısı:** Uygulama kod imzalama sertifikasıyla imzalı olmadığı için Windows
> "bilinmeyen yayıncı" uyarısı verir. *Daha fazla bilgi → Yine de çalıştır* ile geçebilirsin.
> Kaynak kodun tamamı burada; istersen kendin derleyebilirsin.

**Gereksinimler:** Windows 10 1809 veya üstü (64-bit). Kurulum paketi eksikse .NET 8 Masaüstü
Çalışma Zamanı'nı indirmeyi önerir; taşınabilir self-contained sürüm hiçbir ön koşul istemez.

Uygulama yönetici hakkıyla çalışır — Windows Update önbelleği, servis durdurma ve S.M.A.R.T.
okuma bunsuz mümkün değil.

**Güncellemeler** günde bir kez bu deponun yayınlarına bakılarak denetlenir ve Ayarlar
ekranından kurulur. İnen paket, yayınla birlikte gelen `SHA256SUMS.txt` ile doğrulanır; özet
tutmazsa dosya silinir ve hiçbir şey çalıştırılmaz. Denetim yalnızca bir sürüm numarası okur,
hiçbir şey göndermez — kapatılabilir.

## Diller

Arayüz **Türkçe, İngilizce, Almanca, Japonca, Korece ve Basitleştirilmiş Çince** olarak
geliyor; 48 temizleme kuralının açıklamaları dâhil. İlk açılışta dil Windows ayarından
seçilir; istediğin zaman değiştirilir ve yeniden başlatmadan anında uygulanır.

Kataloglar [`data/i18n/`](data/i18n) altında düz JSON — bir dile katkı vermek tek dosya
göndermek demek. Almanca, Japonca, Korece ve Çince çeviriler anadil incelemesi bekliyor.

## Durum

Aktif geliştirme aşamasında, şu an `0.14.0-alpha`. Dokuz modül çalışıyor ve gerçek sistem
verisi okuyor. Eksik kalanlar [docs/ROADMAP.md](docs/ROADMAP.md) içinde dürüstçe listeli:

| Yapıldı | Henüz değil |
|---|---|
| Temizleyici · Registry · Sürücüler · Yazılım güncelleyici | Arka plan modu ve sistem tepsisi |
| Başlangıç · Programlar · Disk sağlığı · Disk analizi | Komut paleti (Ctrl+K) |
| Zaman tüneli · altı dil · otomatik güncelleme | Windows Update dışındaki sürücü kaynakları |

## Kaynaktan derleme

```powershell
git clone https://github.com/SametEge/SysScrub.git
cd SysScrub
dotnet build
dotnet run --project src/SysScrub.App
```

`dist/` klasörünü ve kurulum paketini üretmek için:

```powershell
./build/publish.ps1 -SelfContained
```

Kurulum paketi için [Inno Setup 6](https://jrsoftware.org/isdl.php) gerekir
(`winget install JRSoftware.InnoSetup`). Kurulu değilse o adım atlanır, taşınabilir çıktılar
yine üretilir.

## Proje yapısı

```
src/SysScrub.Core    motor — tarama, güvenlik, sürücü ve disk katmanları, sıfır UI bağımlılığı
src/SysScrub.App     WPF arayüz, tasarım sistemi, çeviri
src/SysScrub.Cli     zamanlanmış/sessiz temizlik ve teknisyen raporu
tests/               495 test: güvenlik denetimi, kural motoru, S.M.A.R.T. ayrıştırma, kataloglar
data/rules           temizleme kuralları, JSON
data/i18n            arayüz çevirileri, JSON
build/               yayın betikleri ve sürüm numarası
installer/           Inno Setup betiği ve sihirbaz görselleri
```

## Katkı

Hata bildirimi ve öneriler için [issue açabilirsin](https://github.com/SametEge/SysScrub/issues).

İki şey için hiç C# gerekmiyor:

- **Temizleme kuralı** — [`data/rules/`](data/rules) altına bir JSON girdisi ekle
- **Çeviri** — [`data/i18n/`](data/i18n) içindeki tek bir dosyayı düzenle

## Lisans

[MIT](LICENSE) · Üçüncü taraf bildirimleri için [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

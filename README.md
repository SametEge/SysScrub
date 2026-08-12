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

## Ne işe yarar

Üç ayrı programın işini tek ve düzgün bir arayüzde topluyor:

| | |
|---|---|
| 🧹 **Temizlik** | Windows, tarayıcı ve uygulama artıklarını kural tabanlı tarar. Sildiği her şey karantinaya gider, tek tıkla geri gelir. |
| ⚙️ **Sürücü güncelleme** | Donanımını tanır, eski sürücüleri bulur, yedekler ve günceller. Her sürücü menşeindeki resmi kaynaktan, imzası doğrulanarak gelir. |
| 💽 **Disk sağlığı** | S.M.A.R.T. verisini okur: sıcaklık, açık kalma süresi, yazılan toplam veri, kalan ömür. Ham değerin yanında ne anlama geldiğini de yazar. |
| 🚀 **Başlangıç yönetimi** | Açılışta çalışan her şey tek listede. Etkisi tahmin değil, Windows'un olay günlüğünden okunan gerçek gecikme. |
| 📦 **Program kaldırıcı** | Toplu kaldırma ve kaldırma sonrası artık taraması. |
| 📊 **Disk analizi** | Alanı ne yiyor? Treemap görselleştirme, en büyük dosyalar, yinelenen dosya bulucu. |

## Neden bir tane daha temizleyici

Mevcutların hepsinde rahatsız edici bir şey var: abartılı kazanç sayıları, geri alınamayan
silmeler, arka planda şişen servisler, ücretli sürüm duvarları, telemetri.

SysScrub'ın duruşu:

- **Hiçbir şey geri alınamaz değil.** Temizlik, registry, sürücü, başlangıç — sistemde yapılan
  her değişiklik tek bir zaman tünelinde tutulur, herhangi bir noktaya geri dönülür.
- **Sayılar gerçek.** Kazanılan alanı ve açılış süresini işlem öncesi/sonrası ölçüp gösteririz.
  Uydurma "sistem %40 hızlandı" yok.
- **Ne yaptığını söyler.** Her kural, her S.M.A.R.T. özniteliği, her öneri için tek tık açıklama.
- **Hesap yok, reklam yok, telemetri yok, ücretli sürüm yok.**

## Kurulum

**Kurulumlu:** [Releases](https://github.com/SametEge/SysScrub/releases/latest) sayfasından
`SysScrub-Setup-*.exe` dosyasını indirip çalıştır.

**Taşınabilir:** `SysScrub-*-portable-x64.zip` dosyasını çıkart ve çalıştır — kurulum gerekmez.
Klasörün yanına boş bir `portable.flag` dosyası koyarsan uygulama tüm ayar ve günlüklerini
kendi klasöründe tutar, sisteme hiçbir şey yazmaz (USB'den çalıştırmak için).

> **SmartScreen uyarısı:** Uygulama kod imzalama sertifikasıyla imzalı olmadığı için Windows
> "bilinmeyen yayıncı" uyarısı verir. *Daha fazla bilgi → Yine de çalıştır* ile geçebilirsin.
> Kaynak kodun tamamı burada; istersen kendin derleyebilirsin.

**Gereksinimler:** Windows 10 1809 veya üstü (64-bit). Kurulum paketi eksikse .NET 8 Masaüstü
Çalışma Zamanı'nı indirmeyi önerir; taşınabilir self-contained sürüm hiçbir ön koşul istemez.

Uygulama yönetici hakkıyla çalışır — Windows Update önbelleği, servis durdurma ve sürücü
kurulumu bunsuz mümkün değil.

## Durum

Aktif geliştirme aşamasında. Şu an **Faz 0** tamamlandı: uygulama açılıyor, gerçek sistem
verisini okuyor, yayın hattı çalışıyor. Modüller sırayla devreye alınıyor ve her modül
kendi ekranında hangi fazda geleceğini söylüyor.

| Faz | İçerik | Durum |
|---|---|---|
| 0 | Tasarım sistemi, uygulama kabuğu, panel, yayın hattı | ✅ |
| 1 | Temizleyici motoru, güvenlik çekirdeği, zaman tüneli | ⏳ |
| 2 | Registry temizleyici | |
| 3–4 | Sürücü güncelleme | |
| 5 | Başlangıç yöneticisi, program kaldırıcı | |
| 6 | Disk sağlığı (S.M.A.R.T.) | |
| 7 | Arka plan modu ve sistem tepsisi | |
| 8 | Disk analizi | |
| 9 | Çoklu dil, komut paleti, v1.0 | |

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
src/SysScrub.App     WPF arayüz, tasarım sistemi, tepsi ve arka plan modu
src/SysScrub.Cli     zamanlanmış/sessiz temizlik ve teknisyen raporu
tools/               marka görsellerini üreten geliştirme araçları
build/               yayın betikleri ve sürüm numarası
installer/           Inno Setup betiği ve sihirbaz görselleri
```

## Katkı

Hata bildirimi ve öneriler için [issue açabilirsin](https://github.com/SametEge/SysScrub/issues).
Temizleme kuralları JSON dosyalarında tutulduğu için yeni bir temizlik hedefi eklemek
kod değişikliği gerektirmez — `data/rules/` altına bir kural eklemek yeterli.

## Lisans

[MIT](LICENSE) · Üçüncü taraf bildirimleri için [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

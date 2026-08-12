# Değişiklik günlüğü

Bu dosyanın biçimi [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) temel alır.
Sürüm numaralandırması [Semantic Versioning](https://semver.org/lang/tr/) kurallarına uyar.

Release notları bu dosyanın ilgili sürüm bölümünden otomatik üretilir.

---

## [0.1.0-alpha] — 2026-08-12

İlk çalışan iskelet. Uygulama açılıyor, gerçek sistem verisini okuyor ve
yayın hattı uçtan uca doğrulanmış durumda. Temizleme, sürücü ve disk sağlığı
motorları henüz devrede değil.

### Eklenenler

- **Tasarım sistemi** — Grafit & Sinyal renk kimliği, koyu ve açık tema, 4px ızgara,
  tipografi ölçeği, kontrol stilleri, hareket dili ve kendi ikon setimiz
- **Uygulama kabuğu** — Mica arka planlı kenarlıksız pencere, yan navigasyon,
  sistem temasını takip eden tema geçişi
- **Panel** — işletim sistemi, makine, açık kalma süresi, sistem diski doluluğu,
  bellek kullanımı ve bağlı disklerin listesi (gerçek veriden)
- **Tarama halkası** — uygulamanın imza görseli, özel çizim
- **Taşınabilir mod altyapısı** — çalıştırılabilir dosyanın yanında `portable.flag`
  varsa tüm veri uygulama klasöründe tutulur, sisteme hiçbir şey yazılmaz
- **Yayın hattı** — `build/publish.ps1` ile taşınabilir exe ve kurulum paketi üretimi,
  GitHub Actions üzerinde derleme, test ve Release yayını
- **Marka görselleri** — çok boyutlu uygulama ikonu, banner ve kurulum sihirbazı
  görselleri kod üzerinden üretiliyor (`tools/SysScrub.IconGen`)

### Bilinen sınırlar

- Temizleyici, Registry, Sürücüler, Disk sağlığı ve diğer modüller henüz boş;
  her biri hangi fazda geleceğini ekranda söylüyor
- Uygulama kod imzalı değil; indirilen dosyada SmartScreen uyarısı çıkar

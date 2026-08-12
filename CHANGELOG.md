# Değişiklik günlüğü

Bu dosyanın biçimi [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) temel alır.
Sürüm numaralandırması [Semantic Versioning](https://semver.org/lang/tr/) kurallarına uyar.

Release notları bu dosyanın ilgili sürüm bölümünden otomatik üretilir.

---

## [0.2.0-alpha] — 2026-08-12

Temizleyici çalışır durumda. Tarama, seçim, silme, karantina ve geri alma zinciri
uçtan uca kuruldu ve test edildi.

### Eklenenler

- **Güvenlik çekirdeği** — silinecek her yol, silme anında denetimden geçiyor:
  - `PathResolver`: kurallar mutlak yol içermez, sembolik kökler kullanır; joker
    karakterli segmentler (tarayıcı profilleri) çalışma anında genişletilir
  - `SafetyGuard`: System32/WinSxS/DriverStore gibi Windows bileşenleri, kullanıcı
    belgeleri, bağlantı noktaları (junction/symlink), OneDrive bulut yer tutucuları,
    ağ ve aygıt yolları, uygulamanın kendi verisi — hepsi reddediliyor
- **Kural motoru** — kurallar koda gömülü değil, JSON. Yeni bir temizlik hedefi
  eklemek kod değişikliği değil, dosya eklemek. Kullanıcı `%ProgramData%\SysScrub\rules`
  altında aynı kimlikli kuralı ezerek düzeltebilir; bozuk bir kural yalnızca kendini düşürür
- **48 temizleme kuralı** — Windows (geçici dosyalar, Update önbelleği, Windows.old,
  bellek dökümleri, hata raporları, günlükler, küçük resim/gölgelendirici/yazı tipi
  önbellekleri, Geri Dönüşüm Kutusu), Chromium ve Firefox tabanlı tarayıcılar,
  Discord/Spotify/Teams/Slack/Zoom/Adobe/Office/VS Code, Steam/Epic/Battle.net/EA/Ubisoft,
  geliştirici önbellekleri ve gizlilik grubu — her biri Türkçe ve İngilizce açıklamalı
- **Tarama motoru** — paralel, iptal edilebilir, ilerleme raporlu; bağlantı noktalarının
  içine girmez, erişilemeyen dalları sessizce atlar
- **Silme motoru** — kalıcı / Geri Dönüşüm Kutusu / karantina modları, salt-okunur
  bayrağını temizleyip yeniden deneme, kilitli dosyaları yeniden başlatmada silinmek
  üzere işaretleme, Windows Update servisini geçici durdurma, boşalan klasörleri toplama
- **Karantina ve geri alma** — silinen dosyalar bildirimle birlikte saklanır, tek tıkla
  eski yerine döner; hedefte yeni bir dosya varsa üzerine yazılmaz
- **Zaman tüneli** — her işlemin kaydı, dosya bazında ayrıntı ve geri alma düğmesi
- **Kanıtlı öncesi/sonrası** — kazanç, silinen dosyaların toplamı yanında diskin
  gerçek boş alanından da ölçülüp gösteriliyor
- **Komut satırı** — `scan`, `clean` (varsayılan kuru çalıştırma, silmek için `--apply`),
  `rules`, `history`, `undo`
- **122 birim testi** — güvenlik denetimi, yol çözümleme, glob eşleştirme, kural yükleme,
  tarama süzgeçleri, karantina turu ve silme davranışları

### Düzeltmeler

- Komut satırı aracının adı `sysscrub-cli.exe` oldu: Windows dosya adlarında büyük/küçük
  harf ayrımı yapmadığı için eski ad arayüzün `SysScrub.exe` dosyasının üzerine yazıyordu
- Tek dosya yayınında taşınabilir mod yolu çözümlenemiyordu (`Assembly.Location` boş döner)

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

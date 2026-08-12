# Değişiklik günlüğü

Bu dosyanın biçimi [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) temel alır.
Sürüm numaralandırması [Semantic Versioning](https://semver.org/lang/tr/) kurallarına uyar.

Release notları bu dosyanın ilgili sürüm bölümünden otomatik üretilir.

---

## [0.4.0-alpha] — 2026-08-12

Sürücüler modülünün çekirdeği çalışır durumda: donanım envanteri, sorunlu cihaz
tespiti, eski sürücü listesi, Windows Update sorgusu ve sürücü yedekleme.

### Eklenenler

- **Donanım envanteri** — `Win32_PnPEntity` ve `Win32_PnPSignedDriver` birleştirilerek
  her cihazın adı, sınıfı, üreticisi, sürücü sürümü, tarihi, sağlayıcısı, INF dosyası,
  imza durumu ve donanım kimlikleri okunur. Cihaz sınıfları Türkçeleştirilmiş
- **Sorunlu cihaz tespiti** — Aygıt Yöneticisi sorun kodları Türkçe açıklamaya çevrilir
  ("Sürücü yüklü değil", "Cihaz bir sorun bildirdiği için Windows onu durdurdu").
  Sorunlu cihazı olan gruplar listede üstte ve açık gelir
- **Eski sürücü listesi** — iki yıldan eski üretici sürücüleri, yaşıyla birlikte.
  Microsoft'un genel sürücüleri bu listeye girmez; onların eski olması normal
- **Windows Update sürücü araması** — WUA COM arayüzü üzerinden çevrimiçi sorgu.
  Sürücü güncellemeleri grup ilkesi veya Windows ayarıyla kapatılmışsa bu ayırt
  edilip söylenir; boş sonucu "sürücülerin güncel" diye göstermek yanıltıcı olurdu
- **Sürücü yedekleme** — `pnputil /export-driver` ile tüm üçüncü parti sürücü
  paketleri tarih damgalı klasöre aktarılır
- **DriverStore envanteri** — `pnputil /enum-drivers` ayrıştırıcısı; çıktı
  yerelleştirilmiş olduğu için etiket adı yerine alan sırası kullanılır
- **Sürücüler ekranı** — özet sayaçlar, sorunlu cihaz kartı, eski sürücü listesi,
  sınıfa göre gruplanmış tüm cihazlar, işlem örtüsü
- **Komut satırı** — `drivers`, `drivers --problems`, `drivers --updates`,
  `drivers --backup`, `drivers --verbose`

### Kapsam notu

Güncelleme kaynağı yalnızca Windows Update. Oradan gelen her sürücü WHQL imzalı ve
Microsoft tarafından o donanıma uygun bulunmuş. Üçüncü parti sürücü aynası
tutulmuyor — DriverBooster tarzı uygulamaları güvenilmez yapan şey tam olarak o.
Microsoft Update Catalog ve üretici uç noktaları bir sonraki fazda geliyor.

---

## [0.3.0-alpha] — 2026-08-12

Registry temizleyici çalışır durumda. Modülün tamamı güvenlik katmanı önce
yazılarak kuruldu; tarayıcılar sonra geldi.

### Eklenenler

- **Yol çözümleyici** — registry değerlerindeki dosya yollarını çözer: tırnaklı,
  argümanlı, `%değişkenli%`, kaynak indeksli (`shell32.dll,-123`), `@` önekli
  dolaylı dizeler, `rundll32` çağrıları. Boşluklu tırnaksız yollarda var olan en
  uzun öneki arar. 32-bit kayıtlar `System32` yazıp dosyayı `SysWOW64`'e koyduğu
  için ikizini de dener. **Çözemediği durumda "yok" değil "bilinmiyor" der** ve
  bulgu üretmez — registry temizleyicilerin çalışan kaydı silmesinin bir numaralı
  sebebi bu ayrımın yapılmaması
- **Registry guard** — iki katman: yasak ağaçlar (HKLM\SYSTEM, SECURITY, SAM,
  bileşen bakımı, .NET, Defender, Cryptography, Winlogon, grup ilkeleri) ve izinli
  kapsam. İzinli kapsam varsayılanı "hayır" yapar. HKCR kabul edilmez: birleştirilmiş
  görünüm olduğu için hangi kovana yazıldığı belirsiz kalır
- **12 tarayıcı** — eksik paylaşılan DLL kayıtları, geçersiz uygulama yolları,
  hedefi olmayan başlangıç kayıtları, MUICache artıkları, kırık yükleyici klasörleri,
  onaylı kabuk uzantısı artıkları, sahipsiz dosya uzantıları, geçersiz program türleri,
  kayıp COM bileşenleri, kırık tip kütüphaneleri, ölü kaldırma girdileri, sahipsiz
  ses olayları. Her bulgu tam anahtar yolunu, neden ölü sayıldığını ve neye işaret
  ettiğini taşır
- **.reg yedekleme** — kendi yazıcımız; tüm veri türlerini (string, expand, multi,
  dword, qword, binary) ve tek değer yedeklemeyi destekler. `reg.exe export` yalnızca
  anahtarın tamamını alabildiği için kullanılmadı
- **Sistem geri yükleme noktası** — ikinci güvenlik ağı. Sistem Koruması kapalıysa
  ya da Windows aynı gün ikinci noktayı reddettiyse durum ayırt edilip bildirilir
- **Registry ekranı** — tarayıcı bazında bulgular, her tarayıcının ne aradığının
  açıklaması, ayrıntıda tam anahtar yolu ve gerekçe, işlem örtüsü, sonuç ve geri alma
- **Komut satırı** — `registry` (kuru çalıştırma), `registry --apply`

### Kapsam kararı

Güvenlik duvarı kuralları tarayıcısı v1'e alınmadı. Kurallar `HKLM\SYSTEM` altında
duruyor ve en tehlikeli kovana istisna açmak güvenlik katmanını anlamsızlaştırırdı.
Yerine onaylı kabuk uzantısı tarayıcısı eklendi; tarayıcı sayısı yine 12.

### Düzeltmeler

- HKCU kayıtları iki kez listeleniyordu: WOW64 yönlendirmesi HKCU'da geçerli
  olmadığı hâlde iki görünüm de taranıyordu. Yönlendirmenin geçerli olduğu yollar
  ayırt ediliyor, üstüne motorda yinelenen ayıklaması var

---

## [0.2.1-alpha] — 2026-08-12

### Düzeltmeler

- **Temizlik sırasında uygulama yanıt vermiyordu.** Silme baştan sona eşzamanlı
  dosya sistemi işi (`File.Delete`, `File.Move`, kabuk çağrıları) ve hiçbir bekleme
  noktası gerçekten geri dönmüyordu; iş arayüz iş parçacığında çalıştığı için pencere
  on binlerce dosya boyunca donuyordu. Silme artık havuz iş parçacığına alınıyor.
- **İlerleme dosya başına raporlanıyordu.** 60.000 dosya, arayüze 60.000 gönderi
  demekti. Rapor sıklığı sınırlandı; kural değişimlerinde ve bitişte her zaman raporlanıyor.
- Yetki uyarısı, yönetici hakkı varken de gösteriliyordu; artık yalnızca eksikken
  çıkıyor ve tek tıkla yükseltme sunuyor.

### Eklenenler

- **İşlem örtüsü** — tarama ve temizlik sırasında ekranı kaplayan ilerleme kartı:
  gerçek yüzde halkası, işlenen dosya sayısı, o ana kadar kurtarılan alan, hangi
  kuralda olunduğu ve iptal düğmesi. Altındaki listeye yanlışlıkla tıklanmasını da engeller.
- Yönetici hakkı gerektiren kurallar, taranamadıklarında sebebini satırın kendisinde
  yazıyor; işaretli ama boyutsuz satır bozukmuş gibi görünmüyor
- Panel'deki temizlik kartı Temizleyici ile aynı görünüm modelini paylaşıyor:
  orada başlatılan tarama diğer ekranda hazır bekliyor

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

# Değişiklik günlüğü

Bu dosyanın biçimi [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) temel alır.
Sürüm numaralandırması [Semantic Versioning](https://semver.org/lang/tr/) kurallarına uyar.

Release notları bu dosyanın ilgili sürüm bölümünden otomatik üretilir.

---

## [0.8.0-alpha] — 2026-08-13

Disk sağlığı çalışır durumda.

### Eklenenler

- **NVMe sağlık günlüğü** — `IOCTL_STORAGE_QUERY_PROPERTY` üzerinden log sayfası
  0x02 okunuyor. Modern dahili SSD'lerin tamamını kapsıyor, ek sürücü gerekmiyor.
  Sıcaklık, kalan yedek blok ve üreticinin uyarı eşiği, tüketilen yazma ömrü,
  yazılan/okunan toplam veri, açık kalma süresi, açılma sayısı, ani kapanma,
  düzeltilemeyen veri hatası ve denetleyicinin kendi kritik uyarı bitleri
- **ATA S.M.A.R.T.** — `SMART_RCV_DRIVE_DATA` ile öznitelik ve eşik tabloları.
  Eşikler kimliğe göre eşleştiriliyor, konuma göre değil: iki tablonun sırası
  aynı olmak zorunda değil
- **Öznitelik tablosu veri olarak** (`data/smart-attributes.json`) — aynı öznitelik
  kimliği üreticiden üreticiye farklı anlama geliyor. Tablo derlemeye kaynak olarak
  gömülü; yeni üretici desteği kod değişikliği değil, satır eklemek
- **Sağlık ekranı** — üstte disk seçici şerit, büyük kalan ömür halkası, tek cümlelik
  gerekçe, yazma ömrü çubuğu, ölçüm kutuları ve tam S.M.A.R.T. tablosu. Her satırda
  sade Türkçe açıklama ipucu olarak duruyor
- **`disk` komutu** — `sysscrub-cli disk`; `--verbose` ek sıcaklık sensörlerini de yazar

### Bilmediğimize "iyi" demiyoruz

S.M.A.R.T. okunamadıysa durum "bilinmiyor" kalıyor ve nedeni yazılıyor. Yeşil rozet
göstermek kullanıcıyı yanlış güvene sokar. Okunamayan disk listeden de düşmüyor.

Kullanıcı "Reallocated Sector Count: 0x000000000000" yerine "Bozuk sektör yok"
görüyor; ham değer bir tık uzakta, tabloda duruyor.

Bileşik sıcaklık sağlık kararını veriyor; ek sensörler yalnızca gösteriliyor.
Bileşik sıcaklık üreticiye özel bir hesap ve sensörlerin en yükseği olmak zorunda
değil — sensör okumasını karara katmak yanlış alarm üretirdi.

### Kapsam

Kimlik bilgisi (model, kapasite, veri yolu, bellenim) yönetici olmadan da okunuyor;
sağlık verisi diske komut göndermeyi gerektirdiği için yönetici istiyor. Uygulama
zaten yönetici olarak açılıyor, komut satırında gerekiyorsa uyarı veriliyor.

USB kutularının çoğu ATA komutlarını geçirmiyor. O diskler için "bu bağlantı
üzerinden S.M.A.R.T. okunamıyor" deniyor; üreticiye özel USB köprüleri sonraki
sürümlerde.

Gerçek makinede doğrulandı: iki NVMe SSD okundu, sıcaklık Windows'un kendi
ölçümüyle birebir tuttu.

---

## [0.7.0-alpha] — 2026-08-13

Program kaldırıcı çalışır durumda.

### Eklenenler

- **Program envanteri** — Uninstall kayıtları (HKLM 64 ve 32 bit görünüm + HKCU) ve
  Store paket deposu tek listede. `Win32_Product` WMI sınıfı **bilerek kullanılmıyor**:
  sorgulandığında her MSI paketini yeniden yapılandırıyor, dakikalarca sürüyor ve
  olay günlüğünü şişiriyor
- **Gerçek boyut ölçümü** — kayıttaki `EstimatedSize` çoğu programda hiç yok, olanların
  bir kısmı kurulum anından kalma. Kurulum klasörleri taranarak ölçülüyor. Ölçüm
  listeyi bekletmiyor: envanter hemen geliyor, boyutlar arkada dolduruyor. Ölçülen
  değer parlak, kayıttan gelen tahmin sönük gösteriliyor
- **Tekil ve toplu kaldırma** — yayıncının sessiz komutu varsa o kullanılıyor,
  MSI paketleri sessiz kaldırmaya çevriliyor
- **Kaldırıcı dosyası kayıp uyarısı** — kaydı duran ama kaldırıcısı silinmiş
  programlar işaretleniyor; kullanıcı düğmeye basıp hata almadan önce görüyor
- **Kaldırma sonrası artık klasör** — kurulum klasörü yerinde kaldıysa boyutuyla
  bildiriliyor ve tek tıkla Geri Dönüşüm Kutusu'na taşınabiliyor. Kalıcı silme değil:
  yanlış klasörse geri alınabilir
- **Arama, sıralama ve bileşen süzgeci** — ada/yayıncıya göre arama, boyut/ad/tarih
  sıralaması, Windows'un gizlediği bileşenleri gösterme seçeneği
- **`programs` komutu** — `sysscrub-cli programs`; `--size` gerçek boyutu ölçer,
  `--all` gizli bileşenleri de listeler, `--search` süzer

### Kaldırma sonucu çıkış koduna göre belirlenmiyor

Kaldırıcıların bir kısmı işi alt sürece devredip hemen sıfır dönüyor, bir kısmı
kullanıcı iptal ettiğinde de sıfır dönüyor. Tek güvenilir kanıt kaydın kaybolması;
sonuç ona bakarak veriliyor.

Süreç ağacının tamamı bekleniyor. Inno Setup kaldırıcıları kendini geçici klasöre
kopyalayıp oradan çalıştırıyor ve hemen çıkıyor — yalnızca başlatılan süreç
beklenirse kaldırma daha başlamadan "bitti" denirdi. Başlatılan süreç bir Windows
iş nesnesine (job object) atanıyor, alt süreçler işi devralıyor ve işteki etkin
süreç sayısı sıfırlanana kadar bekleniyor.

Sessiz MSI kaldırmasına `/norestart` ekleniyor: varsayılan davranış bilgisayarı
sormadan yeniden başlatabiliyor.

### Düzeltmeler

- Ayrıştırılamayan kaldırma komutu geçerlilik denetiminde hata veriyordu

---

## [0.6.0-alpha] — 2026-08-13

Başlangıç yöneticisi çalışır durumda.

### Eklenenler

- **Başlangıç envanteri** — açılışta çalışan her şey tek listede: HKCU/HKLM Run ve
  RunOnce (32 ve 64 bit görünüm), kullanıcı ve ortak Başlangıç klasörleri, oturum
  açma tetikleyicili zamanlanmış görevler, otomatik başlayan Microsoft dışı servisler.
  Görev Yöneticisi son iki grubu göstermiyor; oysa yavaş açılışın sebebi çoğu zaman onlar
- **Gerçek açılış gecikmesi** — etki sütunu tahmin değil. Windows her açılışta hangi
  uygulamanın ne kadar geciktirdiğini Tanılama-Performans günlüğüne yazıyor (olay 101);
  son 200 açılışın ortalaması okunuyor. Günlük okunamazsa sütun boş kalır, uydurma
  bir değer üretilmez
- **Hedefi kaybolmuş öğe tespiti** — çalıştırdığı dosya artık olmayan kayıtlar
  işaretleniyor; Windows her açılışta boşuna arıyor demek
- **`startup` komutu** — `sysscrub-cli startup` listeler, `--all` kapalıları da
  gösterir, `--disable`/`--enable <ad>` değiştirir
- **Dolaylı dize çözümleyici** — servis adları registry'de `@dosya.dll,-245` biçiminde
  kaynak göstergesi olarak duruyor; çözülmezse kullanıcı servis adı yerine onu görür

### Devre dışı bırakma kaydı silmez

Görev Yöneticisi bir öğeyi kapatırken Run kaydını silmez; ayrı bir `StartupApproved`
anahtarına 12 baytlık bir durum yazar. Biz de aynısını yapıyoruz. İki faydası var:
kayıt yerinde kaldığı için işlem her zaman geri alınabilir, ve Görev Yöneticisi ile
senkron kalıyoruz — iki araç birbirini ezmiyor.

Servisler yalnızca gösteriliyor. Bir servisin başlangıç türünü değiştirmek bambaşka
bir risk sınıfı ve sistem kararlılığını doğrudan etkileyebiliyor.

Her açma/kapama zaman tüneline yazılıyor.

---

## [0.5.0-alpha] — 2026-08-12

Yazılım güncelleyici çalışır durumda ve sürücü listesi yeniden düzenlendi.

### Eklenenler

- **winget yazılım güncelleyici** — kurulu programların yeni sürümlerini bulur,
  seçmeli veya toplu günceller. Her satırda ad, paket kimliği, kaynak rozeti
  (winget / Microsoft Store) ve `kurulu → yeni` sürüm gösterilir
- **Dile bağlı olmayan çıktı ayrıştırıcısı** — winget'in `upgrade` komutunun JSON
  çıktısı yok; tablo sabit genişlikli ve başlıklar Windows'un diline göre değişiyor.
  Ayrıştırıcı başlık ADINA değil, başlık satırındaki sütun başlangıç konumlarına
  bakıyor. 12 birim testi bunu doğruluyor (gerçek çıktı, Türkçe başlıklar, sütunu
  tam dolduran sürüm numaraları, ilerleme animasyonu satırları)
- **Yükseltilmiş süreçte winget bulma** — uygulama yönetici olarak çalıştığı için
  PATH'teki uygulama takma adı bazı kurulumlarda çözülemiyor; WindowsApps altındaki
  gerçek dosya da aranıyor
- **Sürücü listesi yeniden düzenlendi** — sınıfa göre ağaç yerine sürücü başına tek
  satır: kategori, cihaz adı, kurulu sürüm ve tarih, kullanılabilir sürüm ve tarih.
  Güncel olanlar altta katlanmış bölümde
- **DriverStatusMatcher** — envanterle Windows Update sonucunu donanım kimliği
  üzerinden eşleştirir. Üç durum: güncel, güncelleme var, eski olabilir

### Dürüstlük ayrımı

Sürücülerde "güncel değil" yalnızca Windows Update gerçekten yenisini sunduğunda
deniyor. Sadece yaşa bakıp eski olduğunu düşündüklerimize "eski olabilir" deniyor
ve bölüm başlığı da buna göre değişiyor — bilmediğimiz bir şeyi iddia etmiyoruz.

### Düzeltmeler

- Aynı sayfa şablonu iki kez tanımlandığı için uygulama açılmıyordu
- Cihaz sınıfı ikonları düzeltildi, eksik sınıf adları Türkçeleştirildi

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

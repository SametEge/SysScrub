# Üçüncü taraf bildirimleri

SysScrub aşağıdaki açık kaynak projeleri kullanır veya onlardan yararlanır.
Hepsi SysScrub'ın MIT lisansıyla uyumludur.

---

## CrystalDiskInfo

Disk sağlığı modülünün S.M.A.R.T. erişim yöntemleri ve üreticiye özel öznitelik
yorumlaması, CrystalDiskInfo'nun kamuya açık kaynak kodundan yararlanılarak yazılmıştır.

- Proje: https://github.com/hiyohiyo/CrystalDiskInfo
- Telif: Copyright (c) 2008-2025 hiyohiyo (Crystal Dew World)
- Lisans: MIT License

---

## WPF-UI

Fluent tasarımlı WPF kontrol kütüphanesi.

- Proje: https://github.com/lepoco/wpfui
- Telif: Copyright (c) 2021-2025 Leszek Pomianowski and WPF UI Contributors
- Lisans: MIT License

---

## CommunityToolkit.Mvvm

MVVM altyapısı ve kaynak üreticileri.

- Proje: https://github.com/CommunityToolkit/dotnet
- Telif: Copyright (c) .NET Foundation and Contributors
- Lisans: MIT License

---

## Serilog

Yapılandırılmış günlükleme.

- Proje: https://github.com/serilog/serilog
- Telif: Copyright (c) Serilog Contributors
- Lisans: Apache License 2.0

---

## Inno Setup

Kurulum paketi üreticisi (yalnızca derleme zamanı aracı, dağıtılan uygulamaya dahil değil).

- Proje: https://jrsoftware.org/isinfo.php
- Telif: Copyright (c) 1997-2025 Jordan Russell
- Lisans: Inno Setup License

---

## Sürücü ve güncelleme kaynakları

SysScrub sürücü dosyalarını kendi sunucularında barındırmaz ve yeniden dağıtmaz.
Tüm sürücüler indirildikleri anda menşeindeki resmi kaynaktan alınır:

- Windows Update / Microsoft Update kataloğu (Microsoft)
- Üreticilerin kendi resmi dağıtım adresleri (NVIDIA, AMD, Intel vb.)

İndirilen her paketin Authenticode imzası kurulumdan önce doğrulanır; imzası
geçersiz olan hiçbir paket kurulmaz.

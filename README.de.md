<div align="center">

<img src="docs/assets/banner.png" alt="SysScrub" width="100%">

**Windows-Wartung, Treiberupdates und Laufwerkszustand — in einer App.**

[![Release](https://img.shields.io/github/v/release/SametEge/SysScrub?include_prereleases&color=FF6B2C)](https://github.com/SametEge/SysScrub/releases/latest)
[![Lizenz](https://img.shields.io/badge/lizenz-MIT-FF6B2C)](LICENSE)
[![Build](https://github.com/SametEge/SysScrub/actions/workflows/ci.yml/badge.svg)](https://github.com/SametEge/SysScrub/actions/workflows/ci.yml)

### [⬇ Neueste Version herunterladen](https://github.com/SametEge/SysScrub/releases/latest)

[English](README.md) · [Türkçe](README.tr.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [简体中文](README.zh-Hans.md)

</div>

---

<img src="docs/assets/screens/dashboard.png" alt="SysScrub-Übersicht" width="100%">

## Was es macht

Die Arbeit von drei getrennten Programmen, in einer Oberfläche, die tatsächlich gestaltet
wurde.

| | |
|---|---|
| 🧹 **Bereinigung** | Regelgesteuerter Scan von Windows-, Browser- und Anwendungsresten. Alles Gelöschte landet in der Quarantäne und kommt mit einem Klick zurück. |
| 🗂 **Registry** | Zwölf Scanner für Einträge, deren Ziel verschwunden ist. Vor jedem Entfernen werden eine `.reg`-Sicherung und ein Wiederherstellungspunkt angelegt. |
| ⚙️ **Treiber** | Erkennt deine Hardware und findet veraltete Treiber. Updates kommen von Windows Update — WHQL-signiert und von Microsoft für diese Hardware freigegeben. |
| 📥 **Updates** | Neuere Versionen installierter Programme über winget. Jedes Paket wird von der Quelle seines eigenen Herstellers geladen. |
| 🚀 **Autostart** | Alles, was beim Start läuft, in einer Liste. Das Deaktivieren nutzt den Mechanismus von Windows selbst und bleibt so mit dem Task-Manager synchron. |
| 📦 **Programme** | Deinstallation im Stapel. Das Ergebnis wird daran geprüft, ob der Eintrag wirklich verschwunden ist — der Exit-Code ist nicht verlässlich. |
| 💽 **Laufwerkszustand** | Liest S.M.A.R.T.- und NVMe-Zustandsdaten: Temperatur, Betriebsstunden, geschriebene Datenmenge, verbleibende Lebensdauer. Neben dem Rohwert steht, was er bedeutet. |
| 📊 **Speicheranalyse** | Was frisst deinen Speicher? Treemap-Ansicht, größte Dateien und ein dreistufiger Duplikatfinder. |
| 🕓 **Verlauf** | Jede Änderung, die die App an deinem System vorgenommen hat, in einer chronologischen Aufzeichnung. Von jedem Punkt aus rückgängig zu machen. |

## Warum noch ein Bereinigungsprogramm

Jedes vorhandene macht etwas Ärgerliches: aufgeblähte „gesparter Speicher"-Zahlen, Löschungen,
die sich nicht rückgängig machen lassen, überladene Hintergrunddienste, Bezahlschranken,
Telemetrie.

Wofür SysScrub steht:

- **Ein Scan löscht nie.** Jedes Modul liest zuerst, zeigt dir, was es gefunden hat und warum,
  und wartet. Du entscheidest, was verschwindet.
- **Nichts ist unumkehrbar.** Bereinigung, Registry, Treiber, Autostart — jede Änderung liegt
  in einem einzigen Verlauf, aus dem du zurückrollen kannst.
- **Die Zahlen sind echt.** Der zurückgewonnene Speicher wird vorher und nachher auf dem
  Laufwerk gemessen, nicht geschätzt. Kein erfundenes „dein System ist 40 % schneller".
- **Wenn es etwas nicht weiß, sagt es das.** Ein Laufwerk, dessen S.M.A.R.T. sich nicht lesen
  lässt, verschwindet nicht aus der Liste und bekommt kein grünes Abzeichen — es nennt den
  Grund.
- **Kein Konto, keine Werbung, keine Telemetrie, keine Bezahlversion.**

---

## Die Module

### Bereinigung

<img src="docs/assets/screens/cleaner.png" alt="Bereinigung" width="100%">

48 Regeln für Windows, Browser, Anwendungen, Gaming-Plattformen, Entwicklerwerkzeuge und
Datenschutzspuren. Jede nennt, was sie entfernt und welche Folge das hat — auch die
unangenehmen Teile („nach dem Entfernen von Windows.old ist keine Rückkehr zur vorherigen
Windows-Version mehr möglich").

Regeln sind **Daten, kein Code**: sie liegen in [`data/rules/*.json`](data/rules). Ein neues
Bereinigungsziel bedeutet einen JSON-Eintrag, keine Programmänderung.

Bevor eine einzige Datei gelöscht wird, durchläuft sie eine Sicherheitsprüfung, die geschützte
Windows-Verzeichnisse, deine Dokumente, Reparse Points (Junctions und Symlinks werden nie
verfolgt) und Cloud-Platzhalterdateien ablehnt. Alles außerhalb eines reinen Temp-Ordners geht
zuerst in die Quarantäne.

### Registry

<img src="docs/assets/screens/registry.png" alt="Registry-Bereinigung" width="100%">

Zwölf Scanner: Zähler gemeinsamer DLLs, Dateizuordnungen, ProgID- und CLSID-Einträge,
COM-Server, Typbibliotheken, Shell-Erweiterungen, Deinstallationseinträge, Anwendungspfade,
Autostart-Einträge, MUICache, Installer-Ordner und Sound-Ereignisse.

Jeder Fund zeigt den vollständigen Schlüsselpfad **und warum er als tot gilt**. Vor dem
Löschen wird ein `.reg`-Export jedes betroffenen Schlüssels geschrieben, dazu ein
Systemwiederherstellungspunkt. Schlägt die Sicherung fehl, wird nichts gelöscht.

Schlüssel, die Windows zum Laufen braucht — Dienste, DriverStore, WinSxS, Component Servicing,
.NET, Defender — stehen auf einer fest verdrahteten Nicht-anfassen-Liste.

### Treiber

<img src="docs/assets/screens/drivers.png" alt="Treiberupdates" width="100%">

Hardwareinventar über SetupAPI, dann Windows Update als Quelle. Die Liste trennt zwei ehrliche
Kategorien: Treiber, für die Windows Update tatsächlich eine neuere Version anbietet, und
Treiber, die älter als zwei Jahre sind und für die keine Quelle ein Update anbietet. Die zweite
Gruppe heißt „möglicherweise veraltet" — nicht „veraltet", weil wir es nicht wissen.

Alle Treiber von Drittanbietern lassen sich mit einem Klick in einen Sicherungsordner
exportieren, bevor irgendetwas installiert wird.

### Laufwerkszustand

<img src="docs/assets/screens/disk-health.png" alt="Laufwerkszustand" width="100%">

NVMe-Zustandsprotokoll (Seite 0x02) und ATA-S.M.A.R.T., direkt vom Laufwerk gelesen.
Temperatur, Betriebsstunden, Einschaltzyklen, geschriebene Datenmenge, verbleibende
Lebensdauer, Reserveblöcke, unsaubere Abschaltungen, unkorrigierbare Fehler — jeweils mit einer
Lesart in einfacher Sprache daneben.

Herstellerspezifische Attributbedeutungen liegen in
[`data/smart-attributes.json`](data/smart-attributes.json), sodass die Unterstützung eines
neuen Herstellers eine Tabellenzeile ist und keine Codeänderung.

### Speicheranalyse

<img src="docs/assets/screens/disk-analysis.png" alt="Speicheranalyse" width="100%">

Squarified Treemap des gesamten Laufwerks, größte Dateien und Aufteilung nach Dateityp. Nur
lesend: keine Datei wird gelöscht oder auch nur geöffnet. Cloud-Dateien werden nicht
heruntergeladen — da sie keinen Speicher belegen, werden sie auch nicht mitgezählt. Ordner, die
nicht gelesen werden konnten, werden gezählt und gemeldet statt stillschweigend übersprungen.

Der Duplikatfinder vergleicht in drei Stufen — Größe, dann die ersten und letzten 4 KB, dann
ein vollständiger SHA-256 — und hasht so nur, was er muss.

### Autostart und Programme

<img src="docs/assets/screens/startup.png" alt="Autostart-Verwaltung" width="100%">

Run- und RunOnce-Schlüssel (beide Registry-Ansichten), Autostart-Ordner, durch Anmeldung
ausgelöste geplante Aufgaben und automatisch startende Dienste, die nicht von Microsoft
stammen. Das Deaktivieren schreibt in denselben `StartupApproved`-Speicher, den auch der
Task-Manager nutzt — die beiden widersprechen sich nie. Die Startverzögerung wird nicht
geschätzt, sondern aus dem Diagnostics-Performance-Ereignisprotokoll von Windows gelesen.

Die Deinstallation ruft das Deinstallationsprogramm des jeweiligen Programms auf und prüft das
Ergebnis daran, ob der Registrierungseintrag tatsächlich verschwunden ist.

### Verlauf

<img src="docs/assets/screens/timeline.png" alt="Verlauf" width="100%">

Jeder Durchlauf wird festgehalten: was entfernt wurde, wie viele Bytes, welche Regel und ob es
sich rückgängig machen lässt. Bereinigungen in Quarantäne kommen mit einem Klick zurück.

---

## Installation

**Installer:** Lade `SysScrub-Setup-*.exe` aus den
[Releases](https://github.com/SametEge/SysScrub/releases/latest) und führe die Datei aus.

**Portabel:** Entpacke `SysScrub-*-portable-x64.zip` und starte es — keine Installation nötig.
Lege eine leere Datei `portable.flag` neben die ausführbare Datei, und die App behält alle
Einstellungen und Protokolle in ihrem eigenen Ordner, ohne etwas ins System zu schreiben
(nützlich vom USB-Stick).

> **SmartScreen-Warnung:** Die App ist nicht mit einem Codesignaturzertifikat signiert, deshalb
> zeigt Windows die Warnung „unbekannter Herausgeber". Nutze *Weitere Informationen → Trotzdem
> ausführen*. Der gesamte Quellcode liegt hier, falls du lieber selbst baust.

**Voraussetzungen:** Windows 10 1809 oder neuer (64-Bit). Der Installer bietet an, die .NET 8
Desktop Runtime nachzuladen, falls sie fehlt; der self-contained portable Build braucht keine
Voraussetzungen.

Die App läuft mit erhöhten Rechten — der Windows-Update-Cache, das Stoppen von Diensten und das
Lesen von S.M.A.R.T. sind anders nicht möglich.

**Updates** werden einmal täglich gegen die Releases dieses Repositorys geprüft und lassen sich
im Einstellungsbildschirm installieren. Das geladene Paket wird gegen die mit dem Release
veröffentlichte `SHA256SUMS.txt` geprüft; stimmt der Hash nicht, wird die Datei gelöscht und
nichts ausgeführt. Die Prüfung liest eine Versionsnummer und sendet nichts — sie lässt sich
abschalten.

## Sprachen

Die Oberfläche gibt es auf **Türkisch, Englisch, Deutsch, Japanisch, Koreanisch und
vereinfachtem Chinesisch**, einschließlich aller 48 Regelbeschreibungen. Beim ersten Start wird
die Sprache aus deiner Windows-Einstellung übernommen; sie lässt sich jederzeit wechseln und
gilt sofort, ohne Neustart.

Die Kataloge sind schlichtes JSON in [`data/i18n/`](data/i18n) — zu einer Sprache beizutragen
heißt, eine Datei zu schicken. Die deutsche, japanische, koreanische und chinesische Übersetzung
warten noch auf muttersprachliche Durchsicht.

## Status

In aktiver Entwicklung, derzeit bei `0.14.0-alpha`. Neun Module funktionieren und lesen echte
Systemdaten. Was noch fehlt, steht ehrlich in [docs/ROADMAP.md](docs/ROADMAP.md):

| Fertig | Noch nicht |
|---|---|
| Bereinigung · Registry · Treiber · Software-Updates | Hintergrundmodus und Infobereich |
| Autostart · Programme · Laufwerkszustand · Speicheranalyse | Befehlspalette (Strg+K) |
| Verlauf · sechs Sprachen · Auto-Update | Treiberquellen außer Windows Update |

## Aus dem Quellcode bauen

```powershell
git clone https://github.com/SametEge/SysScrub.git
cd SysScrub
dotnet build
dotnet run --project src/SysScrub.App
```

Um `dist/` und den Installer zu erzeugen:

```powershell
./build/publish.ps1 -SelfContained
```

Der Installer-Schritt braucht [Inno Setup 6](https://jrsoftware.org/isdl.php)
(`winget install JRSoftware.InnoSetup`). Ohne das wird der Schritt übersprungen und die
portablen Ausgaben entstehen trotzdem.

## Aufbau

```
src/SysScrub.Core    Engine — Scannen, Sicherheit, Treiber- und Laufwerksschichten, keine UI-Abhängigkeiten
src/SysScrub.App     WPF-Oberfläche, Designsystem, Lokalisierung
src/SysScrub.Cli     geplante/stille Bereinigung und Technikerbericht
tests/               496 Tests: Sicherheitsprüfung, Regelmodul, S.M.A.R.T.-Auswertung, Kataloge
data/rules           Bereinigungsregeln als JSON
data/i18n            Oberflächenübersetzungen als JSON
build/               Release-Skripte und Versionsnummer
installer/           Inno-Setup-Skript und Assistentenbilder
```

## Mitmachen

Fehlerberichte und Vorschläge sind als
[Issues](https://github.com/SametEge/SysScrub/issues) willkommen.

Zwei Dinge brauchen überhaupt kein C#:

- **Eine Bereinigungsregel** — füge einen JSON-Eintrag in [`data/rules/`](data/rules) hinzu
- **Eine Übersetzung** — bearbeite eine Datei in [`data/i18n/`](data/i18n)

## Lizenz

[MIT](LICENSE) · Hinweise zu Drittanbietern in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

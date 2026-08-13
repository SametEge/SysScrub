<div align="center">

<img src="docs/assets/banner.png" alt="SysScrub" width="100%">

**Windows maintenance, driver updates and disk health — in one app.**

[![Release](https://img.shields.io/github/v/release/SametEge/SysScrub?include_prereleases&color=FF6B2C)](https://github.com/SametEge/SysScrub/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-FF6B2C)](LICENSE)
[![Build](https://github.com/SametEge/SysScrub/actions/workflows/ci.yml/badge.svg)](https://github.com/SametEge/SysScrub/actions/workflows/ci.yml)

### [⬇ Download the latest release](https://github.com/SametEge/SysScrub/releases/latest)

[Türkçe](README.md)

</div>

---

<img src="docs/assets/screens/dashboard.png" alt="SysScrub dashboard" width="100%">

## What it does

Three separate programs' worth of work, in one interface that was actually designed.

| | |
|---|---|
| 🧹 **Cleaner** | Rule-driven scan of Windows, browser and application leftovers. Everything deleted goes to quarantine and comes back with one click. |
| 🗂 **Registry** | Twelve scanners for entries whose target has disappeared. A `.reg` backup and a restore point are taken before anything is removed. |
| ⚙️ **Drivers** | Identifies your hardware and finds outdated drivers. Updates come from Windows Update — WHQL-signed and cleared by Microsoft for your hardware. |
| 📥 **Updates** | Newer versions of installed programs through winget. Every package is downloaded from its own publisher's source. |
| 🚀 **Startup** | Everything that runs at boot, in one list. Disabling uses Windows' own mechanism, so it stays in sync with Task Manager. |
| 📦 **Programs** | Batch uninstall. The result is verified by checking whether the entry actually disappeared — the exit code isn't reliable. |
| 💽 **Disk health** | Reads S.M.A.R.T. and NVMe health data: temperature, power-on hours, total bytes written, remaining life. Next to the raw value it tells you what it means. |
| 📊 **Disk analysis** | What's eating your space? Treemap view, largest files, and a three-stage duplicate finder. |
| 🕓 **Timeline** | Every change the app made to your system, in one chronological record. Undo from any point. |

## Why another cleaner

Every existing one does something irritating: inflated "space saved" numbers, deletions you
can't undo, bloated background services, paywalls, telemetry.

Where SysScrub stands:

- **Scanning never deletes.** Every module reads first, shows you what it found and why, and
  waits. You decide what goes.
- **Nothing is irreversible.** Cleaning, registry, drivers, startup — every change lives in a
  single timeline you can roll back from.
- **The numbers are real.** Space reclaimed is measured on disk before and after, not
  estimated. No invented "your system is 40% faster".
- **When it doesn't know, it says so.** A drive whose S.M.A.R.T. can't be read doesn't vanish
  from the list and doesn't get a green badge — it says why it couldn't be read.
- **No account, no ads, no telemetry, no paid tier.**

---

## The modules

### Cleaner

<img src="docs/assets/screens/cleaner.png" alt="Cleaner" width="100%">

48 rules across Windows, browsers, applications, gaming platforms, developer tools and
privacy traces. Each one states what it removes and what the consequence is — including the
uncomfortable parts ("once Windows.old is removed you can no longer roll back to the previous
Windows version").

Rules are **data, not code**: they live in [`data/rules/*.json`](data/rules). Adding a new
cleaning target means adding a JSON entry, not changing the program.

Before a single file is deleted it passes a safety check that refuses protected Windows
directories, your documents, reparse points (junctions and symlinks are never followed), and
cloud placeholder files. Anything outside a plain temp folder goes to quarantine first.

### Registry

<img src="docs/assets/screens/registry.png" alt="Registry cleaner" width="100%">

Twelve scanners: shared DLL counters, file associations, ProgID and CLSID entries, COM
servers, type libraries, shell extensions, uninstall entries, application paths, startup
entries, MUICache, installer folders and sound events.

Every finding shows the full key path **and why it's considered dead**. A `.reg` export of
every affected key is written before deletion, plus a system restore point. If the backup
fails, nothing is deleted.

Keys Windows needs to run — services, DriverStore, WinSxS, component servicing, .NET,
Defender — are on a hard-coded never-touch list.

### Drivers

<img src="docs/assets/screens/drivers.png" alt="Driver updates" width="100%">

Hardware inventory via SetupAPI, then Windows Update as the source. The list separates two
honest categories: drivers Windows Update actually offers a newer version for, and drivers
older than two years that no source offers an update for. The second group is labelled
"possibly outdated" — not "outdated", because we don't know.

All third-party drivers can be exported to a backup folder with one click before anything is
installed.

### Disk health

<img src="docs/assets/screens/disk-health.png" alt="Disk health" width="100%">

NVMe health log (page 0x02) and ATA S.M.A.R.T. read directly from the drive. Temperature,
power-on hours, power cycles, total bytes written, remaining life, spare blocks, unsafe
shutdowns, uncorrectable errors — with a plain-language reading next to each.

Vendor-specific attribute meanings live in [`data/smart-attributes.json`](data/smart-attributes.json),
so supporting a new manufacturer is a table row, not a code change.

### Disk analysis

<img src="docs/assets/screens/disk-analysis.png" alt="Disk analysis" width="100%">

Squarified treemap of the whole drive, largest files, and file-type breakdown. Read-only: no
file is deleted or even opened. Cloud files aren't downloaded — since they take no space on
disk, they aren't counted either. Folders that couldn't be read are counted and reported
rather than silently skipped.

The duplicate finder compares in three stages — size, then the first and last 4 KB, then a
full SHA-256 — so it only hashes what it has to.

### Startup and Programs

<img src="docs/assets/screens/startup.png" alt="Startup manager" width="100%">

Run and RunOnce keys (both registry views), startup folders, logon-triggered scheduled tasks
and non-Microsoft auto-start services. Disabling writes to the same `StartupApproved` store
Task Manager uses, so the two never disagree. Boot delay isn't guessed — it's the measured
value from Windows' Diagnostics-Performance event log.

The uninstaller runs each program's own uninstaller and then verifies the result by checking
whether the registry entry actually disappeared.

### Timeline

<img src="docs/assets/screens/timeline.png" alt="Timeline" width="100%">

Every run is recorded: what was removed, how many bytes, which rule, and whether it can be
undone. Quarantined cleanups restore with one click.

---

## Install

**Installer:** grab `SysScrub-Setup-*.exe` from
[Releases](https://github.com/SametEge/SysScrub/releases/latest) and run it.

**Portable:** extract `SysScrub-*-portable-x64.zip` and run it — no installation needed.
Drop an empty `portable.flag` file next to the executable and the app keeps all settings and
logs in its own folder, writing nothing to the system (useful from a USB stick).

> **SmartScreen warning:** the app isn't signed with a code-signing certificate, so Windows
> shows an "unknown publisher" warning. Use *More info → Run anyway*. All source is here if
> you'd rather build it yourself.

**Requirements:** Windows 10 1809 or newer (64-bit). The installer offers to fetch the .NET 8
Desktop Runtime if it's missing; the self-contained portable build needs no prerequisites.

The app runs elevated — the Windows Update cache, stopping services and reading S.M.A.R.T.
aren't possible otherwise.

**Updates** are checked once a day against this repository's releases and can be installed
from the Settings screen. The downloaded package is verified against the `SHA256SUMS.txt`
published with the release; if the hash doesn't match, the file is deleted and nothing runs.
The check reads a version number and sends nothing — it can be turned off.

## Languages

The interface ships in **Turkish, English, German, Japanese, Korean and Simplified Chinese**,
including all 48 cleaning rule descriptions. On first run the language is picked from your
Windows setting; it can be changed at any time and applies immediately, without a restart.

Catalogs are plain JSON in [`data/i18n/`](data/i18n) — contributing a language means sending
one file. The German, Japanese, Korean and Chinese translations are awaiting native review.

## Status

Under active development, currently at `0.14.0-alpha`. Nine modules are functional and read
real system data. What's still missing is listed honestly in [docs/ROADMAP.md](docs/ROADMAP.md):

| Done | Not yet |
|---|---|
| Cleaner · Registry · Drivers · Software updates | Background mode and system tray |
| Startup · Programs · Disk health · Disk analysis | Command palette (Ctrl+K) |
| Timeline · six languages · auto-update | Driver sources beyond Windows Update |

## Building from source

```powershell
git clone https://github.com/SametEge/SysScrub.git
cd SysScrub
dotnet build
dotnet run --project src/SysScrub.App
```

To produce `dist/` and the installer:

```powershell
./build/publish.ps1 -SelfContained
```

The installer step needs [Inno Setup 6](https://jrsoftware.org/isdl.php)
(`winget install JRSoftware.InnoSetup`). Without it that step is skipped and the portable
outputs are still produced.

## Layout

```
src/SysScrub.Core    engine — scanning, safety, driver and disk layers, zero UI dependencies
src/SysScrub.App     WPF interface, design system, localisation
src/SysScrub.Cli     scheduled/silent cleaning and technician report
tests/               495 tests: safety guard, rule engine, S.M.A.R.T. parsing, catalogs
data/rules           cleaning rules as JSON
data/i18n            interface translations as JSON
build/               release scripts and version number
installer/           Inno Setup script and wizard images
```

## Contributing

Bug reports and suggestions are welcome as
[issues](https://github.com/SametEge/SysScrub/issues).

Two things need no C# at all:

- **A cleaning rule** — add a JSON entry to [`data/rules/`](data/rules)
- **A translation** — edit one file in [`data/i18n/`](data/i18n)

## License

[MIT](LICENSE) · Third-party notices in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

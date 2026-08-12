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

## What it does

Three separate programs' worth of work, in one interface that was actually designed:

| | |
|---|---|
| 🧹 **Cleaning** | Rule-driven scan of Windows, browser and application leftovers. Everything deleted goes to quarantine and comes back with one click. |
| ⚙️ **Driver updates** | Identifies your hardware, finds outdated drivers, backs them up and updates them. Every driver comes from its official source with its signature verified. |
| 💽 **Disk health** | Reads S.M.A.R.T. data: temperature, power-on hours, total bytes written, remaining life. Next to the raw value it also tells you what it means. |
| 🚀 **Startup manager** | Everything that runs at boot, in one list. Impact isn't guessed — it's the real delay read from the Windows event log. |
| 📦 **Uninstaller** | Batch uninstall plus a leftover scan afterwards. |
| 📊 **Disk analysis** | What's eating your space? Treemap visualisation, largest files, duplicate finder. |

## Why another cleaner

Every existing one does something irritating: inflated "space saved" numbers, deletions you
can't undo, bloated background services, paywalls, telemetry.

Where SysScrub stands:

- **Nothing is irreversible.** Cleaning, registry, drivers, startup — every change to your
  system lives in a single timeline you can roll back from any point.
- **The numbers are real.** Space reclaimed and boot time are measured before and after.
  No invented "your system is 40% faster".
- **It tells you what it's doing.** One click explains any rule, any S.M.A.R.T. attribute,
  any suggestion.
- **No account, no ads, no telemetry, no paid tier.**

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

The app runs elevated — the Windows Update cache, stopping services and installing drivers
aren't possible otherwise.

## Status

Under active development. **Phase 0 is complete**: the app launches, reads real system data,
and the release pipeline works end to end. Modules land one at a time, and each one tells you
on its own screen which phase it arrives in.

| Phase | Contents | Status |
|---|---|---|
| 0 | Design system, app shell, dashboard, release pipeline | ✅ |
| 1 | Cleaning engine, safety core, timeline | ⏳ |
| 2 | Registry cleaner | |
| 3–4 | Driver updates | |
| 5 | Startup manager, uninstaller | |
| 6 | Disk health (S.M.A.R.T.) | |
| 7 | Background mode and system tray | |
| 8 | Disk analysis | |
| 9 | Localisation, command palette, v1.0 | |

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
src/SysScrub.App     WPF interface, design system, tray and background mode
src/SysScrub.Cli     scheduled/silent cleaning and technician report
tools/               development tools that generate the brand assets
build/               release scripts and version number
installer/           Inno Setup script and wizard images
```

## Contributing

Bug reports and suggestions are welcome as
[issues](https://github.com/SametEge/SysScrub/issues). Cleaning rules live in JSON files, so
adding a new cleaning target needs no code change — dropping a rule into `data/rules/` is enough.

## License

[MIT](LICENSE) · Third-party notices in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

<div align="center">

# Comienzo

**A fast, native Start menu replacement for Windows 10 and Windows 11.**

Search applications, open Windows settings, and calculate expressions without leaving the
keyboard.

[![Release](https://github.com/victor141516/comienzo/actions/workflows/release.yml/badge.svg)](https://github.com/victor141516/comienzo/actions/workflows/release.yml)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows)](https://github.com/victor141516/comienzo/releases/latest)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)

[Download the latest release](https://github.com/victor141516/comienzo/releases/latest)

</div>

## Why Comienzo?

Comienzo keeps the familiar Windows 11 look while making Start immediate and keyboard-first. Its
window and application catalog are prepared before the first opening, so pressing the Windows key
reveals an already-rendered menu instead of constructing one on demand.

| | Feature |
| --- | --- |
| ⚡ | Pre-rendered interface and preloaded icons for fast opening and smooth scrolling |
| 🔎 | Ranked search across applications and Windows settings |
| 🧮 | Inline calculator with parentheses, precedence, multiplication, division, and powers |
| ⌨️ | Arrow-key navigation, Enter to launch, and Escape to close |
| 🪟 | Bare Windows key and Start-button support without breaking native Windows shortcuts |
| 📦 | Portable, self-contained x64 and ARM64 releases with no separate .NET installation |
| 🔒 | Local-only usage ranking with no telemetry or external service |

## Quick start

1. Download the ZIP for your computer from the [latest GitHub Release](https://github.com/victor141516/comienzo/releases/latest):
   - `win-x64` for most Intel and AMD Windows computers.
   - `win-arm64` for Windows computers with an ARM64 processor.
2. Extract the ZIP and run `Comienzo.exe`.
3. Optionally enable **Start with Windows** from the tray icon.

Comienzo requires Windows 10 or Windows 11. It does not require installation, administrator
permissions, or a separately installed .NET runtime. Windows may display a reputation warning
because release binaries are not currently digitally signed.

## Use Comienzo

- Press and release the Windows key, or click the Start button, to open Comienzo.
- Hold the Windows key and press another key to use native shortcuts such as `Win+R`, `Win+E`, or
  `Win+G`; the shortcut begins as soon as the second key is pressed.
- Hold Shift while invoking Start to open the native Windows Start menu.
- Type an application, setting, or expression such as `(12+3)*2^3`.
- Use Up/Down and Enter—or click a result—to open it.
- Press Escape or click outside the menu to close it.

Comienzo discovers classic Start menu shortcuts, Win32 applications, Microsoft Store/MSIX apps,
App Paths, `.lnk`, `.url`, `.appref-ms`, and registered launcher protocols such as `steam://`.

## Privacy and local data

Comienzo has no telemetry and does not send search or usage history anywhere. The counter behind
the **Most used** section is stored only on the current computer:

```text
%LOCALAPPDATA%\Comienzo\usage.json
```

Deleting that file resets the ranking.

## Build from source

Building Comienzo requires Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build Comienzo.slnx -c Release
dotnet run --project src/Comienzo/Comienzo.csproj -c Release -- --self-test
```

Create the same self-contained packages supported by the release workflow:

```powershell
dotnet publish src/Comienzo/Comienzo.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
dotnet publish src/Comienzo/Comienzo.csproj -c Release -r win-arm64 --self-contained true -o artifacts/win-arm64
```

## Technical overview

- **Interface:** C#, WPF, and .NET 10 with Windows 11-inspired styling.
- **Input:** Low-level Windows hooks distinguish a bare Windows-key press from native shortcuts.
- **Discovery:** Start menu locations, App Paths, uninstall registration, and the AppsFolder shell
  namespace are combined and deduplicated.
- **Launching:** Shell semantics are preserved for shortcuts, URLs, MSIX apps, and URI protocols.
- **Releases:** A pushed version tag builds x64 and ARM64 ZIP files, checksums them, and publishes a
  GitHub Release through [GitHub Actions](.github/workflows/release.yml).

Repository-specific development and validation rules are documented in [AGENTS.md](AGENTS.md).

# Comienzo

[![Release](https://github.com/victor141516/comienzo/actions/workflows/release.yml/badge.svg)](https://github.com/victor141516/comienzo/actions/workflows/release.yml)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows)](https://github.com/victor141516/comienzo/releases)

Comienzo is a lightweight, native alternative Start menu for Windows 10 and Windows 11. It replaces the normal Start experience, preserves Windows shortcuts, and lets you search for applications, system settings, and mathematical expressions from the keyboard.

## Features

- Opens with the Windows key or a click on the Start button.
- Opens the native Start menu when Shift is held.
- Preserves Windows shortcuts such as `Win+R`, `Win+E`, and `Win+L` generically.
- Combines Start menu shortcuts, Win32 apps, MSIX/Store apps, and App Paths in one catalog.
- Supports `.lnk`, `.url`, `.appref-ms`, and launcher protocols such as `steam://`.
- Groups application and Windows Settings search results.
- Supports keyboard navigation with ↑, ↓, and Enter.
- Includes a calculator with parentheses, operator precedence, and powers.
- Ranks frequently used items using local data only.
- Preloads the window and icons for immediate opening and smooth scrolling.
- Stays out of the taskbar and Alt+Tab while remaining pre-rendered in the background.
- Provides self-contained packages for Windows x64 and Windows ARM64.

## Download and run

1. Open the [Releases](https://github.com/victor141516/comienzo/releases) page.
2. Download the appropriate ZIP file:
   - `win-x64` for most Intel and AMD computers.
   - `win-arm64` for Windows computers with an ARM64 processor.
3. Extract the ZIP file and run `Comienzo.exe`.

No installation, administrator permissions, or separately installed .NET runtime are required. Windows may show a reputation warning for new binaries that have not yet been digitally signed.

## Usage

1. Press the Windows key or click the Start button.
2. Type to search for an application, a setting, or an expression such as `(12+3)*2^3`.
3. Use ↑/↓ and Enter, or click a result to open it.
4. Press Escape or click outside the menu to close it.
5. Hold Shift while invoking Start to open the native Windows menu.

The tray icon lets you open or close Comienzo and enable **Start with Windows**. If an instance is already running, launching the program again shows the existing window.

## Privacy and local data

Comienzo does not send telemetry or usage history to external services. The counter used by the **Most used** section is stored locally at:

```text
%LOCALAPPDATA%\Comienzo\usage.json
```

## Build from source

Requirements:

- Windows 10 or 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

Build and run the internal checks:

```powershell
dotnet build Comienzo.slnx -c Release
dotnet run --project src/Comienzo/Comienzo.csproj -c Release -- --self-test
```

Publish self-contained executables:

```powershell
dotnet publish src/Comienzo/Comienzo.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
dotnet publish src/Comienzo/Comienzo.csproj -c Release -r win-arm64 --self-contained true -o artifacts/win-arm64
```

## Project structure

```text
src/Comienzo/
├── Models/       Catalog and result models
├── Services/     Discovery, search, icons, hooks, and persistence
├── App.xaml.cs   Application lifetime, tray, and single-instance behavior
└── MainWindow.*  WPF interface and menu behavior
```

## Automated releases

Every tag pushed to GitHub triggers [`.github/workflows/release.yml`](.github/workflows/release.yml). The workflow builds self-contained `win-x64` and `win-arm64` packages, generates SHA-256 checksums, and creates a GitHub Release with automatically generated notes.

Recommended convention:

```powershell
git tag v0.2.5
git push origin v0.2.5
```

Before creating the tag, update `<Version>` in [`src/Comienzo/Comienzo.csproj`](src/Comienzo/Comienzo.csproj) to match it.

## Development

See [`AGENTS.md`](AGENTS.md) for validation commands, keyboard-hook invariants, and repository-specific rules.

Bugs and feature requests can be filed in [GitHub Issues](https://github.com/victor141516/comienzo/issues).

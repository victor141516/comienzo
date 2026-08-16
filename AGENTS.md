# AGENTS.md

## Scope

These instructions apply to the entire repository.

## Project overview

Comienzo is a lightweight Windows 10/11 Start menu replacement written in C# and WPF on .NET 10. It discovers installed applications and Windows settings, provides grouped search and calculator results, and intercepts only the input needed to replace a bare Windows-key press or a normal Start-button click.

## Repository layout

- `src/Comienzo/App.xaml.cs`: process lifetime, single-instance behavior, tray integration, and global hooks.
- `src/Comienzo/MainWindow.xaml`: WPF styles and visual tree.
- `src/Comienzo/MainWindow.xaml.cs`: menu visibility, focus, navigation, launch behavior, and pre-rendering.
- `src/Comienzo/Models/`: catalog models.
- `src/Comienzo/Services/`: app discovery, search, usage ranking, icon extraction, hooks, startup, and tests.
- `.github/workflows/release.yml`: tag-driven x64 and ARM64 release pipeline.
- `artifacts/`, `bin/`, and `obj/`: generated output; never commit these directories.

## Required validation

Run these commands from the repository root after code changes:

```powershell
dotnet build Comienzo.slnx -c Release
dotnet run --project src/Comienzo/Comienzo.csproj -c Release -- --self-test
```

When changing publishing or runtime-specific code, also cross-publish both supported RIDs:

```powershell
dotnet publish src/Comienzo/Comienzo.csproj -c Release -r win-x64 --self-contained true -o artifacts/qa-win-x64
dotnet publish src/Comienzo/Comienzo.csproj -c Release -r win-arm64 --self-contained true -o artifacts/qa-win-arm64
```

Do not run UI automation or input-injection integration tests on the user's active desktop unless the user explicitly authorizes it in the current task. Pure self-tests and builds are safe to run by default.

## Architecture invariants

- A bare Windows-key press opens or toggles Comienzo without allowing the native Start menu to flash.
- Windows shortcuts must be handled generically. Do not add per-shortcut branches for `Win+R`, `Win+E`, or similar combinations.
- Every replayed shortcut must preserve balanced key-down and key-up events so the Windows key can never remain logically pressed.
- Holding Shift while invoking Start must keep the native Windows behavior.
- A click outside an open menu must close it, including the first click immediately after opening.
- The WPF window is pre-rendered and kept alive off-screen while logically closed. Do not replace this with repeated `Show`/`Hide` cycles that can expose an unpainted DWM frame.
- Catalog and icon work must finish before the first interactive opening. WPF image objects created off the UI thread must be frozen before binding.
- App discovery must preserve shell launch semantics for `.lnk`, `.url`, `.appref-ms`, MSIX apps, and registered URI protocols.
- Usage ranking is local-only and must never prevent an application from launching if persistence fails.

## Coding conventions

- Keep nullable reference types enabled and resolve new warnings instead of suppressing them broadly.
- Prefer small services with explicit responsibilities over adding platform logic directly to the window.
- Use asynchronous I/O and background work for discovery or icon extraction; marshal only UI changes to the WPF dispatcher.
- Release native handles, icons, COM objects, hooks, timers, and tray resources deterministically.
- Preserve Windows 10 compatibility when using Windows 11 DWM attributes: optional APIs must fail safely.
- Do not add production dependencies unless they materially reduce complexity or risk.
- Keep user-facing strings in Spanish unless a task explicitly changes the product language.

## UI changes

- Match the Windows 11 visual language while keeping the app lightweight.
- Verify focus, keyboard navigation, high-DPI positioning, multi-monitor bounds, scrolling, and first-frame rendering.
- Avoid layout changes that reintroduce the Enter glyph, remove the screen-edge gap, or make the window wider without a demonstrated need.
- For visual changes, inspect an actual rendered window or snapshot in addition to checking XAML.

## Release process

- Update `<Version>` in `src/Comienzo/Comienzo.csproj` before tagging.
- Use tags such as `v0.2.4`.
- Never commit generated release binaries. GitHub Actions produces and attaches x64 and ARM64 ZIP files.
- Do not move or force-update a published release tag.

## Code review rules

- Flag any path where a suppressed key-down can lack its matching key-up.
- Flag any special-cased Windows shortcut when the behavior can be expressed by the generic replay state machine.
- Flag synchronous catalog/icon work on the UI thread.
- Flag window lifecycle changes that destroy or hide the pre-rendered surface between openings.
- Flag unbounded native resources, undisposed hooks, or unfrozen cross-thread WPF objects.

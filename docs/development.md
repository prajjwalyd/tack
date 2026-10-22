# Working on Tack

How to build, run and test Tack, and the conventions the code follows.
Tack is small on purpose: a board, prints, pins.

Tack is being ported from Rust and Tauri to C#, WinUI 3 and Native AOT; the
plan, and what is done so far, is in [csharp-port.md](csharp-port.md). The
Rust app is kept on `main` until the C# app replaces it.

## Prerequisites

- Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`global.json` pins
  the version; `winget install Microsoft.DotNet.SDK.10`)
- For Native AOT publishing: Visual Studio or its Build Tools with the
  "Desktop development with C++" workload, plus "C++ ARM64 build tools" to
  publish for ARM64
- Optional: Visual Studio 2026, for XAML Hot Reload and the WinUI debugger.
  Everything builds from the command line without it.

## The dev loop

```sh
dotnet build Tack.slnx                     # everything, Debug
dotnet test --solution Tack.slnx           # the tests
dotnet run --project src/Tack.App          # run the app (a debug build)
./scripts/check-file-length.ps1            # no file over 300 lines
dotnet publish src/Tack.App -c Release -r win-x64 -o artifacts/win-x64   # Native AOT
```

**A debug build never touches your real Tack.** It keeps its data in
`%LOCALAPPDATA%\Tack.Dev` (a `Roaming` folder for board.json and a `Local`
folder for Captures and Notes), and runs under names of its own (mutex,
autostart value, tray tooltip "Tack (development)"), so it runs beside the
installed Tack. Set `TACK_HOME` to a folder to use that instead, for any
build. To try the app on a copy of your board, copy `%APPDATA%\Tack\*` into
`<TACK_HOME>\Roaming` and `%LOCALAPPDATA%\Tack\*` into `<TACK_HOME>\Local`.
The copied board.json still names your real files; the app reads them but
only ever recycles files inside its own folders.

A release build uses the real folders and names: run one only with
`TACK_HOME` set, or with the installed Tack quit.

Debug builds trace to the debugger's output and stderr with a `tack` prefix
(`DevTrace`). Release builds trace nothing.

## Code style

- **One type per file, and short files.** A file over 300 lines fails CI
  (`scripts/check-file-length.ps1`); split it into smaller types instead. A
  folder is a namespace: `src/Tack.Windows/Shell/TrayIcon.cs` is
  `Tack.Windows.Shell.TrayIcon`.
- **Three layers, dependencies pointing down:** `Tack.Core` (logic, no
  Windows APIs, unit tested), `Tack.Windows` (Win32 and WinRT, through
  CsWin32), `Tack.App` (WinUI windows and wiring). No IPC, no MVVM framework,
  no dependency injection container.
- **Use what Windows and .NET provide** before writing Win32 by hand, and
  never poll where Windows can notify.
- Every type starts with a `<summary>` saying what it is for. Comments
  explain why, not what.
- Warnings are errors, `.editorconfig` holds the style, and everything must
  stay Native AOT compatible: no reflection-based JSON or binding.
- Package versions live in `Directory.Packages.props` only.
- Use the domain words: the **board**, a **print** (one pinned screenshot),
  its **pin**, a **capture** (from the clipboard) versus a **screenshot
  file** (from the folder), **reveal** and **tuck**.
- The app's only outgoing connection is the update check and download (one
  HTTPS endpoint, signature-verified). There are no analytics and no
  fetching of anything else. The only listening is the phone board, on the
  NetBird address alone, when the user switches it on. Keep it that way, and
  keep `docs/privacy.md` true when it changes.

CI builds, tests, checks file lengths and publishes with Native AOT for x64
and ARM64 on every push to `main` or a `port/` branch and on every pull
request. The AOT builds are kept as artifacts for a week.

## The social preview

`docs/assets/social-preview.png` (1280 × 640) is what GitHub shows when the
repository is linked, but a file in the repository is not enough: a
maintainer has to upload it under the repository's **Settings › General ›
Social preview** each time it changes.

## Releases and the updater key

The C# app's installer and updater come in phase 6 of the port. Until then,
releases are made from `main` as before.

Installed copies of Tack 0.2 only accept updates signed with the updater
key, a minisign key pair whose public key is built into them. The first C#
release reaches them through that updater, so it has to be signed with the
same key. The repository secrets:

- `TAURI_SIGNING_PRIVATE_KEY`: the private key's contents.
- `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`: its password.

**Keep a backup of the private key and its password** outside GitHub (a
password manager); the secrets cannot be read back. Never commit the key,
paste it in an issue or a log, or put it on a shared drive. If it is lost
before the first C# release, installed copies can only move to the C# app by
downloading its installer by hand.

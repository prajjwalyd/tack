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
- Optional, for the installer: [Inno Setup 6.7](https://jrsoftware.org/isdl.php)
  (CI pins 6.7.3), and Node.js 20 or later to sign one with a throwaway key

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

CI builds, tests, checks file lengths, publishes with Native AOT for x64
and ARM64 and compiles an unsigned installer from each, on every push to
`main` or a `port/` branch and on every pull request. The AOT builds and
installers are kept as artifacts for a week.

## The social preview

`docs/assets/social-preview.png` (1280 × 640) is what GitHub shows when the
repository is linked, but a file in the repository is not enough: a
maintainer has to upload it under the repository's **Settings › General ›
Social preview** each time it changes.

## The installer

`installer/Tack.iss` is an [Inno Setup](https://jrsoftware.org/isinfo.php)
script (its Pascal code is in `installer/code/`). It builds one installer per
architecture from a Native AOT publish folder:

```powershell
dotnet publish src/Tack.App -c Release -r win-x64 -o artifacts/win-x64
./scripts/release/Build-Installer.ps1 -PublishDir artifacts/win-x64 -Arch x64
# artifacts/installer/Tack_0.3.0_x64-setup.exe
```

The version comes from `Directory.Build.props`, and a compiler warning fails
the build. CI compiles both installers, unsigned, on every push.

**Never run the installer on a PC whose installed Tack you want to keep.**
It installs into `%LOCALAPPDATA%\Tack` and replaces whatever Tack is there,
0.2 included, with its Apps entry. Try it in Windows Sandbox or a virtual
machine.

What it does (the design is [distribution-design.md](distribution-design.md),
section 4):

- Installs per user, without elevation, into `%LOCALAPPDATA%\Tack`, with a
  Start menu shortcut "Tack" and an Apps entry "Tack". The `AppId` GUID in
  `Tack.iss` is how every later installer finds this installation: it never
  changes.
- Needs Windows 11. The x64 installer also runs on ARM64 Windows 11.
- Ends a running Tack before it replaces files: an update first waits up to
  10 s for the updater's Tack to quit, then ends it; run by hand, it asks.
  It finds Tack by the path of its exe, so a development build keeps running.
- Takes 0.2's updater arguments as well as Inno's own:

  | Argument | Meaning |
  |---|---|
  | `/P` | passive: a progress window and no questions (runs again as `/SILENT /SUPPRESSMSGBOXES`) |
  | `/S` | silent (runs again as `/VERYSILENT /SUPPRESSMSGBOXES`) |
  | `/UPDATE` | an update: keeps everything of the person's, and does not bring back a deleted Start menu shortcut |
  | `/R` | starts Tack when done |
  | `/ARGS …` | ignored, with everything after it |

  `/P` and `/S` start the installer again because Inno decides whether it is
  silent before any script runs. The first copy waits and exits with code 1,
  so a script that needs the real exit code passes `/SILENT` or
  `/VERYSILENT` itself.
- Uninstalling ends Tack, removes its files, the Start menu shortcut and the
  Run value `Tack`, and asks about "Delete the application data", unticked.
  Only when it is ticked does it delete `%APPDATA%\Tack` and
  `%LOCALAPPDATA%\Tack` (the board, captures and notes), and never when the
  uninstaller is run with `/UPDATE` or silently. An update never runs the
  uninstaller.
- Setup and the uninstaller write a log to `%TEMP%` every run ("Setup Log
  …"), the first thing to ask for when an update goes wrong.

### The bridge from Tack 0.2

Installed copies of 0.2 update through the Tauri updater, and the first C#
release reaches them as an ordinary update: the same feed, the same key, and
an installer that takes 0.2's arguments.

1. 0.2 reads `latest.json`, checks the installer's signature with the key
   built into it, and that the signed version is the announced one.
2. It saves the installer in `%TEMP%`, starts it with `/P /UPDATE /R /ARGS`
   and quits.
3. The installer runs again silently, waits for 0.2 to be gone (ending it if
   it is not), and installs `Tack.exe` over `tack.exe` (names compare without
   case, so it is the same file).
4. Then it removes what only 0.2 used: its `uninstall.exe`, its Apps entry
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Tack`, the key
   `HKCU\Software\tack\Tack` (and `HKCU\Software\tack` if that leaves it
   empty), and the WebView2 folders `%LOCALAPPDATA%\Tack\WebView2` and
   `%LOCALAPPDATA%\app.tack.board`. 0.2's Start menu shortcut has the same
   name as the new one and is replaced. If 0.2 was installed somewhere else
   by hand, its `tack.exe` there goes too.
5. It keeps `%APPDATA%\Tack` (board.json), the Captures and Notes in
   `%LOCALAPPDATA%\Tack`, and the Run value `Tack`, which the new Tack
   rewrites at start. Then it starts Tack.

From then on the C# updater installs the next versions, with the same
arguments. On Windows 10 the installer changes nothing: with `/UPDATE` it
starts 0.2 again (if `/R`) and exits with an error code; run by hand it says
that Tack needs Windows 11. 0.2 there keeps offering the update.

The bridge is checked by hand on a clean virtual machine with 0.2 installed
and in use ([csharp-port.md](csharp-port.md), phase 6).

## Releases

Push a tag that matches `<Version>` in `Directory.Build.props` (`v0.3.0` for
`0.3.0`). The release workflow (`.github/workflows/release.yml`):

1. checks the tag against the version and creates a **draft** release;
2. per architecture, publishes with Native AOT, compiles the installer,
   signs it for the updaters and attaches it and its `.sig` to the draft;
3. writes `latest.json` from the two signatures, in 0.2's shape, and
   attaches it;
4. checks `latest.json` (`scripts/release/Test-LatestJson.ps1`): the
   version, both builds from this release with their `.sig` files, and
   signatures made for this version, for that file, with the key the app
   trusts. A wrong file fails the run.

Nothing reaches anyone until a maintainer publishes the draft: every
installed Tack, 0.2 and the C# app alike, reads
`releases/latest/download/latest.json`, which GitHub points at the newest
published (non-draft, non-prerelease) release.

The workflow's steps are scripts in `scripts/release/`, so each can be run
on its own PC: `Build-Installer.ps1`, `Sign-Installer.ps1`,
`New-LatestJson.ps1`, `Test-LatestJson.ps1`, and `Install-InnoSetup.ps1`
(for CI; it checks the download against a pinned SHA-256).

### The updater key

The installers are signed with the updater key, a minisign key pair made by
the Tauri CLI, the same one 0.2's releases were signed with. Its public key
is in `src/Tack.Core/Updates/UpdateTrust.cs` (and is built into every
installed 0.2); every installed Tack accepts updates signed by the matching
private key and nothing else. The signature also carries the version
(`tauri signer sign --app-version`), and the updaters refuse an update whose
`latest.json` announces another. The repository secrets:

- `TAURI_SIGNING_PRIVATE_KEY`: the private key's contents.
- `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`: its password.

They reach the signing step only, not the whole job; the Tauri CLI
(`@tauri-apps/cli` 2.12.1, pinned in `Sign-Installer.ps1`) is downloaded in
an earlier step and runs offline while the key is in the environment. The
tag's code is what is built. **Keep a backup of the private key and its
password** outside GitHub (a password manager); the secrets cannot be read
back. Never commit the key, paste it in an issue or a log, or put it on a
shared drive.

If the private key is lost or leaked, installed copies cannot be updated by
the usual route, because they only accept the old key and the key is built
into them:

1. Generate a new pair, put the new public key in `UpdateTrust.cs`, replace
   the two secrets, and release a version signed with it.
2. Copies that already have the old public key will not install it: tell
   people (the README, the release notes) to download and run the new
   installer once by hand. After that, updates work again. A copy of 0.2
   can only move this way.
3. If the key leaked rather than was lost, treat any release signed after
   the leak as suspect, publish the new one at once, and say so in the
   notes; whoever has the old key can sign an update that old copies accept
   until they have moved to the new one.

### Rehearsing a release

Everything but the real key can be tried on your own PC, in a scratch
folder outside the repository, with a throwaway key that no installed Tack
trusts. Never use the real key for this.

```powershell
$scratch = "$env:TEMP\tack-rehearsal"
npx @tauri-apps/cli@2.12.1 signer generate --ci -p throwaway -w "$scratch\test.key"

dotnet publish src/Tack.App -c Release -r win-x64 -o "$scratch\win-x64"
$exe = ./scripts/release/Build-Installer.ps1 -PublishDir "$scratch\win-x64" -Arch x64 -OutputDir $scratch
# The arm64 installer, as a script check only (these are x64 binaries):
./scripts/release/Build-Installer.ps1 -PublishDir "$scratch\win-x64" -Arch arm64 -OutputDir $scratch

./scripts/release/Sign-Installer.ps1 -Fetch
$env:TAURI_SIGNING_PRIVATE_KEY_PATH = "$scratch\test.key"
$env:TAURI_SIGNING_PRIVATE_KEY_PASSWORD = 'throwaway'
./scripts/release/Sign-Installer.ps1 -Installer $exe -Version 0.3.0
./scripts/release/Sign-Installer.ps1 -Installer "$scratch\Tack_0.3.0_arm64-setup.exe" -Version 0.3.0

./scripts/release/New-LatestJson.ps1 -Version 0.3.0 -Tag v0.3.0 -Repository prajjwalyd/tack `
    -X64Signature "$exe.sig" -Arm64Signature "$scratch\Tack_0.3.0_arm64-setup.exe.sig" -OutFile "$scratch\latest.json"
./scripts/release/Test-LatestJson.ps1 -Path "$scratch\latest.json" -Version 0.3.0 -Tag v0.3.0 `
    -Repository prajjwalyd/tack -PublicKey (Get-Content -Raw "$scratch\test.key.pub")
```

Without `-PublicKey`, `Test-LatestJson.ps1` checks against the real key and
says the throwaway one is wrong, as the release workflow would. To try the
installer itself, and the bridge, use a virtual machine with 0.2 installed.

### Authenticode

The installers are not code-signed yet, so SmartScreen warns on a first run.
The release workflow has a skipped step marking where it goes: once there is
a certificate (Azure Artifact Signing, or an OV certificate), sign through
Inno Setup's SignTool so the uninstaller inside Setup is signed too, and
before the updater signature, which covers the file's final bytes.

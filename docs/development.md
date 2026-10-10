# Working on Tack

How to build, run and preview Tack, and the conventions the code follows.
Tack is small on purpose: a board, prints, pins.

## Prerequisites

- Windows 10 or 11 (Tack is Windows only; `tack-core` also builds elsewhere)
- [Rust](https://rustup.rs), stable, MSVC toolchain, with the `rustfmt` and
  `clippy` components (`rustup component add rustfmt clippy`)
- Visual Studio C++ Build Tools
- Node.js 20 or later (only for the Tauri CLI)
- Python 3, optional, for the UI preview server

## The dev loop

```sh
npm install
npm run dev                  # debug build of crates/tack-app, then runs it
cargo test --workspace       # unit tests (the board model lives in tack-core)
npm run test:ui              # the page code's tests (Node, no dependencies): tests/ui
```

The installer: `npm run build` writes it to `target/release/bundle/nsis/`.
It is built with updater signatures (`bundle.createUpdaterArtifacts`), so
without the signing key (see Releases) it stops at the signing step. For a
local installer, set `TAURI_SIGNING_PRIVATE_KEY` to a throwaway key from
`npx tauri signer generate` (never the release key): no installed Tack
accepts an update signed with it. CI's plain build (`tauri build
--no-bundle`) makes no installer and needs no key.

Only one Tack runs at a time: quit the tray icon (or
`taskkill /IM tack.exe /F`) before starting another build.

The debug build keeps a console. Tack logs to stderr with a `tack:` prefix,
including one line per clipboard change, which is how the Snipping Tool
capture flow is debugged.

The UI and the backend talk only through Tauri commands and events: if
you change one, change `crates/tack-app/src/ipc/`, `ui/scripts/ipc.js` and
`ui/dev/fake-ipc.js` together.

## The UI preview

The board UI is plain HTML, CSS and ES modules with no build step, so it can
be developed in any browser without Rust:

```sh
npm run preview              # python -m http.server 5178 --directory ui
```

Open <http://localhost:5178/dev/preview.html>. The page loads the real
`ui/styles` and `ui/scripts`, with `ui/dev/fake-ipc.js` standing in for the
backend: it installs a fake `window.__TAURI__`, draws placeholder
screenshots, and the buttons fire the events the backend would (reveal, add,
keep, remove, copied, breeze, drag, update, pointer left...). "Fill to 20"
(or `?n=20` in the URL) gives a row long enough to scroll, "Shuffle" sends
an `order-changed`, and "Theme" forces the board light or dark instead of
following the system; "Felt", "Frame" and "Paper" step through the board
colours, frame finishes and note papers (`?felt=`, `?finish=` and `?paper=`
start on one). The preview runs under the same
content security policy as the app, so anything the app would block fails
there too.

`ui/dev/` is embedded in the app with the rest of `ui/` (Tauri embeds the
whole `frontendDist` folder) but nothing in the app links to it.

## The social preview

`docs/assets/social-preview.png` (1280 × 640) is what GitHub shows when the
repository is linked, but a file in the repository is not enough: a
maintainer has to upload it under the repository's **Settings › General ›
Social preview** each time it changes.

## Code style

- Rust: `cargo fmt --all` (see `rustfmt.toml`) and
  `cargo clippy --workspace --all-targets -- -D warnings` must pass. Fix
  lints rather than allowing them.
- Every Rust module starts with a `//!` paragraph saying what it does and
  why; every JS module starts with a short header comment.
- Comments explain why, not what. The code already says what.
- Keep platform-independent logic in `tack-core` with unit tests, Win32 in
  `tack-windows` behind plain functions and callbacks, and Tauri glue in
  `tack-app`.
- Use the domain words: the **board**, a **print** (one pinned screenshot),
  its **pin**, a **capture** (from the clipboard) versus a **screenshot
  file** (from the folder), **reveal** and **tuck**.
- The app's only outgoing connection is the update check and download
  (`crates/tack-app/src/updates/`, one HTTPS endpoint, signature-verified);
  there are no analytics and no fetching of anything else, and the web views
  stay off the network. The only listening is the phone board
  (`crates/tack-app/src/phone/`), on the NetBird address alone, when the user
  switches it on. Keep it that way, and keep `docs/privacy.md` true when it
  changes (`updates/config_guard.rs` fails the build if an update safeguard
  or the web views' limits are loosened).

CI runs formatting, clippy, the tests and a release build on Windows for
every push.

## Releases

Push a tag that matches the version in `Cargo.toml` and
`crates/tack-app/tauri.conf.json` (`v0.2.0` for `0.2.0`). The release workflow
builds the x64 and arm64 installers, signs them for the updater, and
attaches them, their `.sig` files and `latest.json` to a **draft** release.
Its last job checks `latest.json` (version, both builds, signatures made for
that version) and fails if it is wrong. Nothing reaches users until a
maintainer publishes the draft: the in-app updater reads
`releases/latest/download/latest.json`, which GitHub points at the newest
published (non-draft, non-prerelease) release.

The installers are signed with the updater key, a minisign key pair made by
`npx tauri signer generate`. The public key is in
`crates/tack-app/tauri.conf.json` (`plugins.updater.pubkey`); every installed
Tack trusts updates signed by the matching private key and nothing else. The
repository secrets:

- `TAURI_SIGNING_PRIVATE_KEY`: the private key's contents.
- `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`: its password.

They are passed to the build step only (not the whole job), and the tag's
code is what is built. **Keep a backup of the private key and its password**
outside GitHub (a password manager); the secrets cannot be read back. Never
commit the key, paste it in an issue or a log, or put it on a shared drive.

If the private key is lost or leaked, installed copies cannot be updated by
the usual route, because they only accept the old key and the key is built
into them:

1. Generate a new pair, put the new public key in `tauri.conf.json`, replace
   the two secrets, and release a version signed with it.
2. Copies that already have the old public key will not install it: tell
   people (the README, the release notes) to download and run the new
   installer once by hand. After that, updates work again.
3. If the key leaked rather than was lost, treat any release signed after
   the leak as suspect, publish the new one at once, and say so in the
   notes; whoever has the old key can sign an update that old copies accept
   until they have moved to the new one.

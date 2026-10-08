# Contributing to Tack

Thanks for helping. Tack is small on purpose: a board, prints, pins. Before
building something big, open an issue to talk it through.

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
```

Only one Tack runs at a time: quit the tray icon (or
`taskkill /IM tack.exe /F`) before starting another build.

The debug build keeps a console. Tack logs to stderr with a `tack:` prefix,
including one line per clipboard change, which is how the Snipping Tool
capture flow is debugged.

Where things live, and how a screenshot or a click travels through the app,
is in [docs/architecture.md](docs/architecture.md). The UI and the backend
talk only through the commands and events in [docs/ipc.md](docs/ipc.md): if
you change one, change `crates/tack-app/src/ipc/`, `ui/scripts/ipc.js`,
`ui/dev/fake-ipc.js` and `docs/ipc.md` together.

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
following the system. The preview runs under the same
content security policy as the app, so anything the app would block fails
there too.

`ui/dev/` is embedded in the app with the rest of `ui/` (Tauri embeds the
whole `frontendDist` folder) but nothing in the app links to it.

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
- The app never touches the network. Do not add dependencies that do.

CI runs formatting, clippy, the tests and a release build on Windows for
every push and pull request.

## Commits

- One logical change per commit, with the code, tests and docs it needs.
- Subject in the imperative, under about 70 characters ("Add a Save to
  Pictures item"), a blank line, then the why if it is not obvious.
- Pull requests: say what changed and how you tested it, including a screen
  recording for anything you can see.

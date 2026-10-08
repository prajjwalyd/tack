# Tack

Tack keeps your most recent screenshots on a small corkboard tucked just above
the top edge of your Windows desktop. Every screenshot you take, whether from
Snipping Tool (Win + Shift + S), the Print Screen key or another tool that
saves to the Screenshots folder, gets pinned to the board as a photo print,
and the board slides into view for a few seconds to show it. Nudge the pointer
against the top of the screen when you need one again: the board comes down,
and you can copy a print, open it, edit it or drag it straight into another
app. The board is a single row: newest first, with older prints a scroll
away. Keep the ones you still need; the rest quietly age out after a week.

## Working with prints

- **Click** a print to put the full image on the clipboard.
- **Double-click** to open it in your default image viewer.
- **Press and hold** to open it in Paint (or whatever handles "Edit").
- **Drag** it out as a real file. Dropping into an app (chat, email, a
  document) sends a copy and the print stays pinned. Dropping into an
  Explorer folder, the Desktop or the Recycle Bin moves the file, and the
  print is unpinned.
- **Right-click** for Copy, Open, Edit, Show in Explorer, Keep, Unpin and
  Move to Recycle Bin, plus Save to Pictures for a capture that was never saved.
- **Keep** a print (hover, then the pin button) to give it a brass pin: kept
  prints stay at the front of the row and never age out.
- **Hover** and press the small **×** to unpin a print. The file itself is
  left alone.

The board itself:

- Hold the pointer against the very top edge of a monitor for under half a
  second and the board drops down on that monitor. It goes back up once the
  pointer has left it, or when you click elsewhere.
- **Ctrl+Alt+T** shows or hides it from anywhere.
- The tray icon toggles it on a left click; its menu can clear the board,
  open the Screenshots folder, and switch edge reveal, sounds and starting
  with Windows on or off.

If Snipping Tool is set not to save screenshots automatically, Tack keeps its
own copy of each capture in `%LOCALAPPDATA%\Tack\Captures` while the print is
pinned. When the print is unpinned, cleared or ages out, that copy goes to the
Recycle Bin, never deleted outright, so you can still restore it.
**Save to Pictures** moves it into your Screenshots folder for good.

## Getting Tack

Tack runs on Windows 10 and 11 with the WebView2 runtime (built into Windows
11). There is no published installer yet, so build it as described below;
`npm run build` produces an NSIS installer under
`target/release/bundle/nsis/`.

## Building it yourself

You need [Rust](https://rustup.rs) (stable, MSVC toolchain), the Visual
Studio C++ Build Tools and Node.js 20 or newer.

```sh
npm install          # installs the Tauri CLI
npm run dev          # debug build, then runs it
npm run build        # optimised build plus installer
cargo test --workspace
```

You can also work on the board's look and animations in an ordinary browser,
without Rust: run `npm run preview` and open
<http://localhost:5178/dev/preview.html>. [CONTRIBUTING.md](CONTRIBUTING.md)
has the details.

## Repository map

```
crates/
  tack-core/      board model, history and Keep, duplicate detection, board.json, thumbnails (no Tauri, no Win32)
  tack-windows/   Win32 pieces: Screenshots folder, Snipping Tool clipboard, file drag, overlay window, hotkey, shell
  tack-app/       the Tauri app: wiring, IPC commands and events, tray, context menu
ui/
  index.html      the board page
  styles/         tokens, board, print, pin, note, motion
  scripts/        ES modules: main, ipc, layout, print, gestures, motion/, sound, state
  dev/            browser preview harness with a fake backend
docs/
  architecture.md how the parts connect, with the path of a screenshot and of a click
  ipc.md          every command and event between the UI and the backend
```

## Your data stays on your PC

Tack never sends your screenshots or anything else anywhere. Its own code has
no network access, and the web view's content security policy only permits the
app's own files and its local IPC channel. Tack also switches off the network
services of Microsoft's WebView2 runtime, which draws the board (account
sign-in, secure DNS probes, proxy auto-detection, SmartScreen): in our
measurements it opens no connections at all. See [docs/privacy.md](docs/privacy.md)
for what was measured and the little that sits outside an app's control. What it
stores is small and local: the paths of pinned files plus your settings in
`%APPDATA%\Tack\board.json`, and unsaved Snipping Tool captures in
`%LOCALAPPDATA%\Tack\Captures`.

Tack watches the clipboard only to notice images placed there by Snipping
Tool. Anything copied by other programs is ignored, as is content an app has
flagged as excluded from clipboard monitoring.

## Acknowledgements

Inspired by [Tendedero](https://github.com/alejandrobujan/tendedero) by
Alejandro Buján. See [NOTICE.md](NOTICE.md).

Built on [Tauri](https://tauri.app), [windows-rs](https://github.com/microsoft/windows-rs),
[image](https://github.com/image-rs/image), [notify](https://github.com/notify-rs/notify),
[arboard](https://github.com/1Password/arboard) and
[trash](https://github.com/Byron/trash-rs).

## License

MIT. See [LICENSE](LICENSE).

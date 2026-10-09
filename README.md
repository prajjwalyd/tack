<p align="center">
  <img src="docs/assets/logo.png" width="112" height="112" alt="Tack app icon">
</p>

<h1 align="center">Snip it. It’s tacked.</h1>

<p align="center"><b>The top of your screen, finally useful.</b><br>
Tack is a small pinboard for Windows that hangs just above the top edge of your screen.</p>

<!-- demo video: replace this block with the uploaded video link -->
<p align="center">
  <img src="docs/assets/poster.png" width="880" alt="A snip of a chart flies up onto the Tack board at the top of a Windows desktop">
</p>

- **Snip the way you already do.** Win + Shift + S, Print Screen, or any tool
  that saves to your Screenshots folder. The board drops down, pins it, and
  tucks away again.
- **Get it back in a flick.** Push the pointer against the top edge, or press
  **Win + Alt + S**.
- **Use it anywhere.** Click to copy, drag it straight into a chat or email,
  double-click to open, press and hold to edit.
- **Pin text on purpose.** Select something and press **Win + Alt + C**.
- **No clutter.** Old snips fade out after a week. **Keep** the ones you need.

<p align="center">
  <img src="docs/assets/hero.png" width="880" alt="The Tack board at the top of a Windows desktop with code, a chart, a note, a video frame and a chat pinned on it">
</p>

## Your board, on your phone

Snip on your PC and it's in your pocket. Snap a photo on your phone and it
lands on your PC's board a second later. It works on Wi-Fi, on mobile data,
anywhere, with no cloud in between: your phone talks straight to your PC
through [NetBird](https://netbird.io), a free, open-source private network.

1. Install [NetBird](https://docs.netbird.io/get-started/install) on your PC
   and your phone, and sign in to the same account on both.
2. In Tack's tray menu, choose **Use on your phone…** and switch it on.
3. Scan the QR code with your phone and click **Allow** on your PC.

<p align="center">
  <img src="docs/assets/phone.png" width="880" alt="The Tack on your phone window with its QR code beside a phone showing the same board in Chrome over 5G">
</p>

Add it to your home screen and it's one tap from then on. Only devices you
allow get in, and your board never leaves your PC.

## Shortcuts

| | |
|---|---|
| **Win + Alt + S** | Show or hide the board. Arrow keys move, **Enter** copies, **Esc** closes. |
| **Win + Alt + C** | Pin what's selected: text, a link or a picture. |
| **Click** | Copy |
| **Double-click** | Open |
| **Press and hold** | Edit |
| **Drag** | Drop it into any app as a file |
| **Right-click** | Keep, Unpin, Show in Explorer and more |

Both shortcuts can be changed from the tray menu's **Shortcuts…**.

## Private by design

- Your board stays on your PC. Tack never talks to the internet.
- Text is never read from your clipboard on its own, only when you press
  Win + Alt + C, and your clipboard is put back as it was.
- Nothing is deleted for good: Tack's own copies go to the Recycle Bin.

The details are in [docs/privacy.md](docs/privacy.md).

## Install

Tack runs on Windows 10 and 11. There's no published installer yet, so build
it with [Rust](https://rustup.rs), the Visual Studio C++ Build Tools and
Node.js 20 or newer:

```sh
npm install
npm run build        # installer in target/release/bundle/nsis/
```

[docs/development.md](docs/development.md) covers development and the
browser preview.

## Credits

Inspired by [Tendedero](https://github.com/alejandrobujan/tendedero) by Alejandro Buján.
Built with [Tauri](https://tauri.app).

## License

MIT. See [LICENSE](LICENSE).

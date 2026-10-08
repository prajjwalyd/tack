# Icon sources

Everything Tack ships as an icon is generated from this folder. The design and
its proportions are in [docs/design.md](../../docs/design.md), "Icon".

| File | What it is |
|---|---|
| `master.py` | The vector sizes: the 256 master (`icon.svg`, `docs/assets/logo.svg`), the pixel-hinted 48 px and the 64 px tray icons. The design's numbers (board 70% of the square, pin 53% of the board's diagonal, cap 31% of its width, needle at (0.61, 0.39) of the felt) are at the top. |
| `grids/16.txt`, `20.txt`, `24.txt`, `32.txt` | The small sizes, drawn by hand: one letter per pixel, colours in `pixels.py`. Edit these, not the SVGs. |
| `pixels.py` | Writes a grid out as a crisp-edged SVG or a PNG, in the app colours or a tray tuning (`light`: darker frame and brass rim; `dark`: dimmer frame, lighter felt, no shadow). |
| `render.mjs` | Rasterises SVGs at exact sizes in headless Chrome or Edge (`$CHROME` to pick one). |
| `build.py` | Writes every file into `crates/tack-app/icons/`, `ui/dev/desktop/` and `docs/assets/`. |

## Regenerate

Needs Python 3 with Pillow, Node 22+, and Chrome or Edge. From the repository root:

```sh
python scripts/icon/build.py svg
npx tauri icon crates/tack-app/icons/icon.svg -o crates/tack-app/icons
rm -rf crates/tack-app/icons/android crates/tack-app/icons/ios
python scripts/icon/build.py png
```

`svg` writes the SVG sources: `icon.svg`, `icon-16/20/24/32/48.svg`, `tray-{light,dark}-{16,20,24,32,64}.svg`,
`ui/dev/desktop/tack-tray-{light,dark}.svg` (the README scenes' tray icon) and `docs/assets/logo.svg`.
`tauri icon` makes the bundle's PNGs and `icon.icns` from the master. `png` then replaces
`icon.ico` (16, 20, 24, 32 and 48 hand-tuned, plus 256) and `32x32.png` with the hand-tuned
sizes, and writes the tray PNGs that `crates/tack-app/src/tray.rs` loads and `docs/assets/logo.png`.

To check the result, serve the repository root (`python -m http.server 5191`) and open
`/ui/dev/icon-sheet.html`: every size at 1x and zoomed, on light and dark taskbars, in a
Start-like grid and in the tray at each scale (`docs/screenshots/icon-sizes.png` is that page).

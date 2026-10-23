# Icon sources

Everything Tack ships as an icon is generated from this folder.

| File | What it is |
|---|---|
| `master.py` | The vector sizes: the 256 master (`icon.svg`, `docs/assets/logo.svg`), the pixel-hinted 48 px and the 64 px tray icons. The design's numbers (board 95% of the square, pin 41% of the board's diagonal, cap 29% of its width, needle at (0.55, 0.46) of the felt) are at the top. |
| `grids/16.txt`, `20.txt`, `24.txt`, `32.txt` | The small sizes, drawn by hand: one letter per pixel, colours in `pixels.py`. Edit these, not the SVGs. |
| `pixels.py` | Writes a grid out as a crisp-edged SVG or a PNG, in the app colours or a tray tuning (`light`: darker frame and brass rim; `dark`: dimmer frame, lighter felt, no shadow). |
| `render.mjs` | Rasterises SVGs at exact sizes in headless Chrome or Edge (`$CHROME` to pick one). |
| `build.py` | Writes every file into `assets/icons/` and `docs/assets/`. |

## Regenerate

Needs Python 3 with Pillow, Node 22+, and Chrome or Edge. From the repository root:

```sh
python scripts/icon/build.py svg
python scripts/icon/build.py png
```

`svg` writes the SVG sources: `icon.svg`, `icon-16/20/24/32/48.svg`, `tray-{light,dark}-{16,20,24,32,64}.svg` and `docs/assets/logo.svg`.
`png` writes `icon.ico` (16, 20, 24, 32 and 48 hand-tuned, plus 256), the tray PNGs the app loads, and `docs/assets/logo.png`.

# Icon sources

Tack has one icon, drawn in `master.py`. Every icon file it ships is that
picture scaled down: `Tack.ico` in `src/Tack.App/Assets` (the exe, the
taskbar, the tray and Settings) and `docs/assets/logo.png` for the README.

| File | What it is |
|---|---|
| `master.py` | The icon as an SVG. The design's numbers (board 95% of the square, pin 41% of the board's diagonal, cap 29% of its width, needle at (0.55, 0.46) of the felt) are at the top. |
| `render.mjs` | Rasterises an SVG at an exact size in headless Chrome or Edge (`$CHROME` to pick one). |
| `build.py` | Draws the master at 1024 px and writes every size from it. |

## Regenerate

Needs Python 3 with Pillow, Node 22+, and Chrome or Edge. From the repository root:

```sh
python scripts/icon/build.py
```

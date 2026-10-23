# Icon sources

Every icon Tack ships is generated from this folder: `Tack.ico` and the tray
PNGs in `src/Tack.App/Assets`, and `docs/assets/logo.png` for the README.

| File | What it is |
|---|---|
| `master.py` | The vector sizes: the 256 master, the pixel-hinted 48 px and the 64 px tray icons. The design's numbers (board 95% of the square, pin 41% of the board's diagonal, cap 29% of its width, needle at (0.55, 0.46) of the felt) are at the top. |
| `grids/16.txt`, `20.txt`, `24.txt`, `32.txt` | The small sizes, drawn by hand: one letter per pixel, colours in `pixels.py`. |
| `pixels.py` | Writes a grid out as a PNG, in the app colours or a tray tuning (`light`: darker frame and brass rim; `dark`: dimmer frame, lighter felt, no shadow). |
| `render.mjs` | Rasterises SVGs at exact sizes in headless Chrome or Edge (`$CHROME` to pick one). |
| `build.py` | Writes every file. |

## Regenerate

Needs Python 3 with Pillow, Node 22+, and Chrome or Edge. From the repository root:

```sh
python scripts/icon/build.py
```

`Tack.ico` holds the hand-drawn 16, 20, 24 and 32 px sizes, plus 48 and 256
from the master. The tray PNGs are the grids in the light and dark tunings,
and the 64 px master for scales above 200%.

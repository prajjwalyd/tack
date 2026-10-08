"""Tack's icon, the pixel sizes: grids/16, 20, 24 and 32.txt are drawn by hand,
one letter per pixel, and written out here as crisp-edged SVGs (one rect per
horizontal run) or PNGs.

Grid: one line per row, all the same length; '.' is transparent, every other
letter a colour from PAL (8 hex digits = with alpha). Edit the grids, not the
SVGs. The tray tunings recolour a few letters:
  light  a darker frame and brass rim, so the tile keeps its edge on a pale taskbar
  dark   a dimmer frame (no glare), a lighter felt (never a dark hole), no shadow

    python pixels.py <grid.txt> <out.svg|out.png> [tune=app|light|dark]
"""
import sys

PAL = {
    # brass, light to dark; lower case = a soft (half-covered) edge pixel
    "W": "#fffbea", "H": "#fff0c2", "L": "#f3d27e", "M": "#dcad48", "D": "#a87923", "E": "#7a5410", "K": "#5c3d08",
    "l": "#f3d27e8c", "m": "#dcad4880", "n": "#a8792380", "k": "#7a541080",
    # steel needle
    "s": "#eef1f4", "t": "#a9b2bc", "u": "#6f7983",
    # felt (ui/styles/tokens.css), lit to shaded
    "j": "#3d7a60", "f": "#2f6a52", "g": "#24503f", "h": "#183a2d", "i": "#0f281f", "q": "#0a1d16",
    # aluminium frame, light to dark; o / v / w = soft corners
    "a": "#eef0f2", "b": "#c9ced3", "c": "#a3a9b0", "d": "#7b828a", "e": "#5d646c",
    "o": "#a3a9b080", "v": "#c9ced380", "w": "#7b828a80",
    # a faint shadow under the tile
    "z": "#00000047", "y": "#00000024", "x": "#00000012",
}
TUNE = {
    "app": {},
    "light": {"a": "#dde1e5", "b": "#aab1b8", "c": "#878e96", "d": "#5f666e", "o": "#878e96a0", "v": "#aab1b8a0", "w": "#5f666ea0",
              "x": "#0000001c", "l": "#e3bd5fa6", "m": "#c99a3599", "n": "#9a6c1a99", "k": "#6b470b99", "E": "#6b470b", "K": "#4f3406"},
    "dark": {"a": "#c9ced3", "b": "#aeb4ba", "c": "#959ca4", "o": "#959ca480", "v": "#aeb4ba80",
             "f": "#3a7a62", "g": "#2d5f4b", "h": "#214a3a", "i": "#183a2d", "q": "#14332a",
             "x": "#00000000", "y": "#00000000", "k": "#a8792399", "K": "#7a5410", "u": "#8a949e"},
}

def load(path):
    rows = [l.rstrip("\n") for l in open(path, encoding="utf-8") if l.strip() and not l.startswith("#")]
    n = len(rows)
    assert all(len(r) == n for r in rows), [(i, len(r)) for i, r in enumerate(rows) if len(r) != n]
    return rows

def colour(ch, tune="app"):
    return TUNE[tune].get(ch, PAL[ch])

def to_svg(rows, tune="app", comment=""):
    n = len(rows)
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {n} {n}" width="{n}" height="{n}" shape-rendering="crispEdges">']
    if comment:
        out.append(f"  <!-- {comment} -->")
    for y, r in enumerate(rows):
        x = 0
        while x < n:
            ch = r[x]
            if ch == ".":
                x += 1; continue
            c = colour(ch, tune); x2 = x
            while x2 + 1 < n and r[x2 + 1] != "." and colour(r[x2 + 1], tune) == c:
                x2 += 1
            if c[7:9] != "00":
                op = f' fill-opacity="{int(c[7:9], 16) / 255:.2f}"' if len(c) == 9 else ""
                out.append(f'  <rect x="{x}" y="{y}" width="{x2 - x + 1}" height="1" fill="{c[:7]}"{op}/>')
            x = x2 + 1
    out.append("</svg>")
    return "\n".join(out) + "\n"

def to_png(rows, tune="app"):
    from PIL import Image
    n = len(rows); im = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch == ".": continue
            c = colour(ch, tune); a = int(c[7:9], 16) if len(c) == 9 else 255
            im.putpixel((x, y), (int(c[1:3], 16), int(c[3:5], 16), int(c[5:7], 16), a))
    return im

if __name__ == "__main__":
    rows = load(sys.argv[1]); out = sys.argv[2]
    tune = dict(a.split("=", 1) for a in sys.argv[3:]).get("tune", "app")
    if out.endswith(".png"):
        to_png(rows, tune).save(out)
    else:
        open(out, "w", encoding="utf-8", newline="\n").write(to_svg(rows, tune))

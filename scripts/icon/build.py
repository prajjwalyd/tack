"""Writes every icon file Tack ships from the one master in master.py:
Tack.ico in src/Tack.App/Assets, and docs/assets/logo.png for the README.

    python scripts/icon/build.py

The master is drawn once, large, and every size is that picture scaled down,
so the taskbar, the tray, Settings and the README all show the same icon.
Needs Python 3 with Pillow, Node 22+ and Chrome or Edge (render.mjs).
"""
import io, os, struct, subprocess, sys, tempfile
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import master

APP = os.path.join(REPO, "src", "Tack.App", "Assets")
DOCS = os.path.join(REPO, "docs", "assets")
DRAWN = 1024
# The sizes Windows asks for: the tray and small icons at 100-200 % (16-32), the
# taskbar at 100-300 % (24-72), Start and Explorer (48-256).
SIZES = (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256)

def build():
    tmp = tempfile.mkdtemp(prefix="tack-icon-")
    svg, drawn = os.path.join(tmp, "master.svg"), os.path.join(tmp, "master.png")
    with open(svg, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(master.build(DRAWN))
    subprocess.run(["node", os.path.join(HERE, "render.mjs"), svg, str(DRAWN), drawn], check=True)
    big = Image.open(drawn).convert("RGBA")
    def scaled(n):
        out = io.BytesIO(); big.resize((n, n), Image.LANCZOS).save(out, "PNG", optimize=True)
        return out.getvalue()
    blobs = [scaled(n) for n in SIZES]
    with open(os.path.join(DOCS, "logo.png"), "wb") as fh:
        fh.write(blobs[SIZES.index(256)])
    head = struct.pack("<HHH", 0, 1, len(SIZES)); off = 6 + 16 * len(SIZES); ents = b""
    for n, d in zip(SIZES, blobs):
        ents += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(d), off); off += len(d)
    with open(os.path.join(APP, "Tack.ico"), "wb") as fh:
        fh.write(head + ents + b"".join(blobs))
    print(f"Tack.ico ({', '.join(map(str, SIZES))}) and logo.png written")

if __name__ == "__main__":
    build()

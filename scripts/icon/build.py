"""Writes every icon file Tack ships from the sources in this folder:
Tack.ico and the tray PNGs in src/Tack.App/Assets, and docs/assets/logo.png.

    python scripts/icon/build.py

Tack.ico holds the hand-tuned 16-32 px sizes plus 48 and 256 from the master.
Needs Python 3 with Pillow, Node 22+ and Chrome or Edge (render.mjs).
"""
import os, struct, subprocess, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import master, pixels

APP = os.path.join(REPO, "src", "Tack.App", "Assets")
TRAY = os.path.join(APP, "Tray")
DOCS = os.path.join(REPO, "docs", "assets")
PIXEL = (16, 20, 24, 32)

def grid(n):
    return pixels.load(os.path.join(HERE, "grids", f"{n}.txt"))

def render(jobs):
    """jobs: [(svg path, size, png path)] through headless Chrome."""
    args = [str(v) for j in jobs for v in j]
    subprocess.run(["node", os.path.join(HERE, "render.mjs"), *args], check=True)

def build():
    tmp = tempfile.mkdtemp(prefix="tack-icon-")
    def svg(name, text):
        path = os.path.join(tmp, name)
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
        return path
    master_svg = svg("256.svg", master.build())
    big = {48: os.path.join(tmp, "48.png"), 256: os.path.join(tmp, "256.png")}
    render([(svg("48.svg", master.build(48, 48)), 48, big[48]), (master_svg, 256, big[256]),
            (master_svg, 256, os.path.join(DOCS, "logo.png")),
            (svg("light-64.svg", master.build(64, 64, "light")), 64, os.path.join(TRAY, "tray-light@2x.png")),
            (svg("dark-64.svg", master.build(64, 64, "dark")), 64, os.path.join(TRAY, "tray-dark@2x.png"))])
    datas = []
    for n in PIXEL:
        rows = grid(n)
        p = os.path.join(tmp, f"{n}.png"); pixels.to_png(rows).save(p); datas.append((n, p))
        for t in ("light", "dark"):
            name = f"tray-{t}.png" if n == 32 else f"tray-{t}-{n}.png"
            pixels.to_png(rows, t).save(os.path.join(TRAY, name))
    datas += [(48, big[48]), (256, big[256])]
    blobs = [open(p, "rb").read() for _, p in datas]
    head = struct.pack("<HHH", 0, 1, len(datas)); off = 6 + 16 * len(datas); ents = b""
    for (n, _), d in zip(datas, blobs):
        ents += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(d), off); off += len(d)
    with open(os.path.join(APP, "Tack.ico"), "wb") as fh:
        fh.write(head + ents + b"".join(blobs))
    print("Tack.ico (16, 20, 24, 32, 48, 256), tray PNGs, logo.png written")

if __name__ == "__main__":
    build()

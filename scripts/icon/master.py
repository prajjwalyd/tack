"""Tack's icon, the vector sizes: the 256 master and the pixel-hinted 48 / 64.

A framed felt tile (brushed-aluminium frame, the board's felt from
ui/styles/tokens.css) seen a little from above, and a brass push-pin driven in
diagonally (needle into the felt, cap up and to the right). One key light from
the top left: the frame's highlight and the pin's shadow agree.

Proportions: the board is 70% of the square; the pin
is 53% of the board's diagonal and its cap 31% of the board's width; the needle
enters at (0.61, 0.39) of the felt, so the cap overhangs the tile's top-right
corner. The pin has the shipped character: a flat-topped cap with a bevelled
rim and one specular point, a waisted grip, a neat flange, a short steel needle.

    python master.py <out.svg> [size=256] [hint=0] [tune=app|light|dark]

`hint=N` snaps the tile and its frame to whole pixels of an N px icon (48: the
tile's lower edge is dropped, so the frame is even all round). `tune` recolours
for the tray (light: a darker frame and brass rim; dark: no glare).
"""
import math, sys

def f(v):
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s

PAL = {
    "brass": dict(hi="#fff3cc", lt="#f2cc6c", md="#daa53f", dk="#a5731d", dd="#71490b", ink="#553806"),
    "steel": dict(hi="#ffffff", lt="#dfe4ea", md="#a9b2bc", dk="#6f7983", dd="#4d555e"),
    "felt": dict(lt="#2f6a52", md="#24503f", dk="#183a2d", dd="#0f281f"),
    "alu": dict(hi="#f7f8fa", lt="#dde1e5", md="#b4bac1", dk="#8a9199", dd="#656c74", ee="#4b525a"),
}
# Tray tunings: colour swaps on the finished SVG.
TUNES = {
    "app": {},
    # light taskbar: a darker frame and brass rim, so the edges hold on a pale bar
    "light": {"#f7f8fa": "#e9ecef", "#dde1e5": "#cdd2d7", "#b4bac1": "#9ea5ad", "#8a9199": "#788089",
              "#656c74": "#565d65", "#a5731d": "#956615", "#71490b": "#5f3d08"},
    # dark taskbar: a dimmer frame (no glare) and a lighter felt (never a dark hole)
    "dark": {"#f7f8fa": "#d4d8dc", "#dde1e5": "#c3c8ce", "#2f6a52": "#3a7a62", "#24503f": "#2d5f4b",
             "#183a2d": "#214a3a", "#0f281f": "#183a2d"},
}

# ---------------------------------------------------------------- geometry
def norm(v):
    l = math.hypot(*v); return (v[0] / l, v[1] / l)

def rpoly(pts, r):
    """Closed path through a convex polygon with rounded corners (radius ~r)."""
    n = len(pts); segs = []
    for i in range(n):
        p0, p1, p2 = pts[i - 1], pts[i], pts[(i + 1) % n]
        a = norm((p0[0] - p1[0], p0[1] - p1[1])); b = norm((p2[0] - p1[0], p2[1] - p1[1]))
        A = (p1[0] + a[0] * r, p1[1] + a[1] * r); B = (p1[0] + b[0] * r, p1[1] + b[1] * r)
        c1 = (A[0] + (p1[0] - A[0]) * .55, A[1] + (p1[1] - A[1]) * .55)
        c2 = (B[0] + (p1[0] - B[0]) * .55, B[1] + (p1[1] - B[1]) * .55)
        segs.append((A, c1, c2, B))
    d = f"M{f(segs[0][3][0])} {f(segs[0][3][1])}"
    for A, c1, c2, B in segs[1:] + segs[:1]:
        d += f" L{f(A[0])} {f(A[1])} C{f(c1[0])} {f(c1[1])} {f(c2[0])} {f(c2[1])} {f(B[0])} {f(B[1])}"
    return d + " Z"

def inset(pts, d):
    """Offset a convex polygon inward by d."""
    cx = sum(p[0] for p in pts) / len(pts); cy = sum(p[1] for p in pts) / len(pts)
    lines = []; n = len(pts)
    for i in range(n):
        p, q = pts[i], pts[(i + 1) % n]
        t = norm((q[0] - p[0], q[1] - p[1])); nrm = (-t[1], t[0])
        if (cx - p[0]) * nrm[0] + (cy - p[1]) * nrm[1] < 0: nrm = (-nrm[0], -nrm[1])
        lines.append(((p[0] + nrm[0] * d, p[1] + nrm[1] * d), t))
    out = []
    for i in range(n):
        (p1, t1), (p2, t2) = lines[i - 1], lines[i]
        den = t1[0] * t2[1] - t1[1] * t2[0]
        s = ((p2[0] - p1[0]) * t2[1] - (p2[1] - p1[1]) * t2[0]) / den
        out.append((p1[0] + t1[0] * s, p1[1] + t1[1] * s))
    return out

def ell(cx, cy, rx, ry):
    return f"M{f(cx - rx)} {f(cy)} A{f(rx)} {f(ry)} 0 1 0 {f(cx + rx)} {f(cy)} A{f(rx)} {f(ry)} 0 1 0 {f(cx - rx)} {f(cy)} Z"

def band(rx, ry, y0, y1):
    """Side of a short cylinder (axis +y): far half of the top ellipse to the near half of the bottom one."""
    return (f"M{f(-rx)} {f(y0)} L{f(-rx)} {f(y1)} A{f(rx)} {f(ry)} 0 0 0 {f(rx)} {f(y1)} "
            f"L{f(rx)} {f(y0)} A{f(rx)} {f(ry)} 0 0 0 {f(-rx)} {f(y0)} Z")

def smooth(t):
    return t * t * (3 - 2 * t)

# ---------------------------------------------------------------- the design
# Board, in its own units: w x h face, t lower edge, r corner radius, fw frame.
BOARD = dict(w=200, h=190, t=5, r=25, fw=11)
# Pin: where the needle enters the felt (fractions of the felt box), its angle from
# vertical, L its projected length (cap top to the hole), D the cap's diameter,
# k how much of the cap's top face we see.
PIN = dict(entry=(.61, .39), phi=45, L=146, D=62, k=.42)
BOARD_PX = 180    # the board's width in the 256 master: 70 %
SHADOW = .30      # how far the pin's shadow leans away from the light, per unit of height

def pin_geom(L, D, k):
    """The pin's anatomy along its own axis (y from the cap's top to the hole)."""
    rc = D / 2; ryc = rc * k
    g = dict(rc=rc, ryc=ryc, a=ryc)
    g["Tc"] = .15 * D;            g["b"] = g["a"] + g["Tc"]      # the cap's rim band
    g["Ln"] = .30 * L                                            # steel needle, visible
    g["Tf"] = .075 * D                                           # flange
    g["rf"] = .31 * D;            g["ryf"] = g["rf"] * k
    g["y_flb"] = L - g["Ln"];     g["y_fl"] = g["y_flb"] - g["Tf"]
    g["Wt"] = .37 * D; g["Ww"] = .16 * D; g["Wb"] = .30 * D; g["tw"] = .48   # waisted grip
    g["Wn"] = max(.07 * D, 2.2)
    return g

def grip_path(g, y0, y1):
    n = 24
    def w(t):
        if t < g["tw"]:
            return g["Wt"] + (g["Ww"] - g["Wt"]) * smooth(t / g["tw"])
        return g["Ww"] + (g["Wb"] - g["Ww"]) * smooth((t - g["tw"]) / (1 - g["tw"]))
    L = [(-w(i / n) / 2, y0 + (y1 - y0) * i / n) for i in range(n + 1)]
    R = [(w(i / n) / 2, y0 + (y1 - y0) * i / n) for i in range(n + 1)][::-1]
    return "M" + " L".join(f"{f(x)} {f(y)}" for x, y in L + R) + " Z"

def cap_sil(g):
    rc, ryc, a, b = g["rc"], g["ryc"], g["a"], g["b"]
    return (f"M{f(-rc)} {f(a)} A{f(rc)} {f(ryc)} 0 0 1 {f(rc)} {f(a)} "
            f"L{f(rc)} {f(b)} A{f(rc)} {f(ryc)} 0 0 1 {f(-rc)} {f(b)} Z")

def pin_silhouette(g, L):
    wn = g["Wn"]
    return (f'<path d="{cap_sil(g)}"/><path d="{grip_path(g, g["b"], g["y_fl"])}"/>'
            f'<path d="{band(g["rf"], g["ryf"], g["y_fl"], g["y_flb"])}"/>'
            f'<path d="M{f(-wn / 2)} {f(g["y_flb"] - 2)} L{f(wn / 2)} {f(g["y_flb"] - 2)} L{f(wn * .35)} {f(L)} L{f(-wn * .35)} {f(L)} Z"/>')

def layout(B):
    face = [(0, 0), (B["w"], 0), (B["w"], B["h"]), (0, B["h"])]
    felt = inset(face, B["fw"])
    fx0, fy0 = felt[0]; fx1, fy1 = felt[2]
    E = (fx0 + (fx1 - fx0) * PIN["entry"][0], fy0 + (fy1 - fy0) * PIN["entry"][1])   # the hole
    phi = math.radians(PIN["phi"]); u = (math.sin(phi), -math.cos(phi))              # toward the cap
    O = (E[0] + u[0] * PIN["L"], E[1] + u[1] * PIN["L"])                              # the cap's top
    return face, felt, E, O, u

def bbox(B, g):
    face, felt, E, O, u = layout(B)
    xs = [0, B["w"]]; ys = [0, B["h"] + B["t"]]
    c, s = math.cos(math.radians(PIN["phi"])), math.sin(math.radians(PIN["phi"]))
    for i in range(72):
        a = 2 * math.pi * i / 72
        for cy in (g["a"], g["b"]):
            lx, ly = g["rc"] * math.cos(a), cy + g["ryc"] * math.sin(a)
            xs.append(O[0] + lx * c - ly * s); ys.append(O[1] + lx * s + ly * c)
    return min(xs), min(ys), max(xs), max(ys)

def build(size=256, hint=0, tune="app"):
    B = dict(BOARD)
    g = pin_geom(PIN["L"], PIN["D"], PIN["k"])
    x0, y0, x1, y1 = bbox(B, g)
    sc = BOARD_PX / B["w"]
    tx = 128 - sc * (x0 + x1) / 2
    ty = 128 - sc * (y0 + y1) / 2
    if hint:   # the tile's face and frame on whole output pixels
        gpx = 256 / hint
        snap = lambda v: round(v / gpx) * gpx
        X = snap(tx); Y = snap(ty)
        W = snap(sc * B["w"]); H = snap(sc * B["h"])
        tx, ty = X, Y
        # the pin keeps its place on the felt: scale the board, not the pin
        B.update(w=W / sc, h=H / sc, fw=max(gpx, snap(sc * B["fw"])) / sc,
                 t=(0 if hint <= 48 else max(gpx, snap(sc * B["t"]))) / sc)
    face, felt, E, O, u = layout(B)
    Br, S, F, A = PAL["brass"], PAL["steel"], PAL["felt"], PAL["alu"]
    fx0, fy0 = face[0]; fx1, fy1 = face[2]
    full = True
    o = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="{size}" height="{size}">', "<defs>"]

    def lin(id_, stops, ax, ay, bx, by):
        st = "".join(f'<stop offset="{of}" stop-color="{c}"' + (f' stop-opacity="{al}"' if al is not None else "") + "/>" for of, c, al in stops)
        o.append(f'<linearGradient id="{id_}" gradientUnits="userSpaceOnUse" x1="{f(ax)}" y1="{f(ay)}" x2="{f(bx)}" y2="{f(by)}">{st}</linearGradient>')

    # the board, lit from the top left
    lin("alu", [(0, A["hi"], None), (.45, A["lt"], None), (1, A["md"], None)], fx0, fy0, fx1, fy1)
    lin("aluRim", [(0, A["lt"], None), (1, A["dd"], None)], fx0, fy0, fx1, fy1 + B["t"])
    lin("edge", [(0, A["dk"], None), (.5, A["md"], None), (1, A["dd"], None)], fx0, 0, fx1, 0)
    lin("felt", [(0, F["lt"], None), (.5, F["md"], None), (1, F["dk"], None)], fx0, fy0, fx1, fy1)
    o.append(f'<radialGradient id="feltKey" gradientUnits="userSpaceOnUse" cx="{f(fx0 + (fx1 - fx0) * .22)}" cy="{f(fy0 + (fy1 - fy0) * .18)}" r="{f((fx1 - fx0) * .9)}"><stop offset="0" stop-color="#d6ffe8" stop-opacity=".16"/><stop offset=".6" stop-color="#d6ffe8" stop-opacity="0"/><stop offset="1" stop-color="#000e06" stop-opacity=".22"/></radialGradient>')
    o.append('<radialGradient id="hole" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#000" stop-opacity=".75"/><stop offset="1" stop-color="#000" stop-opacity="0"/></radialGradient>')
    # the pin, in its own frame (local -x is the lit side)
    rc, ryc, a, b = g["rc"], g["ryc"], g["a"], g["b"]
    rf, ryf, wn, L, D = g["rf"], g["ryf"], g["Wn"], PIN["L"], PIN["D"]
    sw = D / 98   # stroke widths as drawn for the first master's 98-unit cap
    lin("capTop", [(0, Br["hi"], None), (.42, Br["lt"], None), (1, Br["md"], None)], -rc, 0, rc * .9, ryc * 2)
    lin("capRim", [(0, Br["lt"], None), (.16, Br["hi"], None), (.34, Br["md"], None), (.78, Br["dk"], None), (1, Br["dd"], None)], -rc, 0, rc, 0)
    lin("bevel", [(0, "#ffffff", .95), (.45, "#fff6dc", .25), (.6, Br["dk"], 0), (1, Br["dd"], .55)], -rc, 0, rc, 0)
    lin("flBevel", [(0, "#ffffff", .9), (.45, "#fff6dc", .2), (.6, Br["dk"], 0), (1, Br["dd"], .5)], -rf, 0, rf, 0)
    lin("grip", [(0, Br["dk"], None), (.12, Br["lt"], None), (.3, Br["hi"], None), (.48, Br["md"], None), (.82, Br["dk"], None), (1, Br["dd"], None)], -g["Wt"] / 2, 0, g["Wt"] / 2, 0)
    lin("gripShade", [(0, "#000", .45), (1, "#000", 0)], 0, b + ryc * .2, 0, b + ryc + .25 * D)
    lin("flTop", [(0, Br["hi"], None), (.5, Br["lt"], None), (1, Br["md"], None)], -rf, 0, rf, 0)
    lin("flRim", [(0, Br["lt"], None), (.2, Br["hi"], None), (.45, Br["md"], None), (1, Br["dd"], None)], -rf, 0, rf, 0)
    lin("needle", [(0, S["dk"], None), (.3, S["hi"], None), (.6, S["md"], None), (1, S["dd"], None)], -wn / 2, 0, wn / 2, 0)
    o.append('<radialGradient id="spec" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#fff"/><stop offset=".45" stop-color="#fff" stop-opacity=".85"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient>')
    if full:
        o.append('<filter id="weave" x="0" y="0" width="1" height="1"><feTurbulence type="fractalNoise" baseFrequency=".85" numOctaves="2" seed="7"/><feColorMatrix values="0 0 0 0 .6  0 0 0 0 1  0 0 0 0 .8  0 0 0 .6 -.22"/><feComposite in2="SourceGraphic" operator="in"/></filter>')
        o.append('<filter id="brush" x="0" y="0" width="1" height="1"><feTurbulence type="fractalNoise" baseFrequency=".012 .9" numOctaves="2" seed="3"/><feColorMatrix values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0 0 0 .9 -.35"/><feComposite in2="SourceGraphic" operator="in"/></filter>')
    o.append('<filter id="blurS" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="2"/></filter>')
    o.append('<filter id="blurM" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="4.5"/></filter>')
    o.append('<filter id="blurL" x="-30%" y="-30%" width="160%" height="170%"><feGaussianBlur stdDeviation="7"/></filter>')
    fr = max(2, B["r"] - B["fw"] * .8)
    o.append(f'<clipPath id="feltClip"><path d="{rpoly(felt, fr)}"/></clipPath>')
    o.append(f'<clipPath id="faceClip"><path d="{rpoly(face, B["r"])}"/></clipPath>')
    o.append("</defs>")
    o.append(f'<g transform="translate({f(tx)} {f(ty)}) scale({f(sc)})">')

    # ---- board: soft shadow, lower edge, frame (brushed), felt (woven), inner shade
    t = B["t"]
    lower = [(q[0], q[1] + t) for q in face]
    if full:
        o.append(f'<path d="{rpoly([(q[0] + 3, q[1] + 8) for q in lower], B["r"])}" fill="#000" opacity=".26" filter="url(#blurL)"/>')
        o.append(f'<path d="{rpoly([(q[0] + 1, q[1] + 2) for q in lower], B["r"])}" fill="#000" opacity=".18" filter="url(#blurS)"/>')
    if t > 0:
        o.append(f'<path d="{rpoly(lower, B["r"])}" fill="url(#edge)"/>')
        o.append(f'<path d="{rpoly(lower, B["r"])}" fill="none" stroke="{A["ee"]}" stroke-opacity=".55" stroke-width="{f(1.2 / sc * 256 / max(size, 64))}"/>')
    o.append(f'<path d="{rpoly(face, B["r"])}" fill="url(#aluRim)"/>')
    hw = 1.0 / sc * 256 / max(size, 48)
    o.append(f'<path d="{rpoly(face[:2] + lower[2:], B["r"])}" fill="none" stroke="#1d2a24" stroke-opacity=".22" stroke-width="{f(hw)}"/>')
    o.append(f'<path d="{rpoly(inset(face, .9), B["r"] - .9)}" fill="url(#alu)"/>')
    o.append(f'<path d="{rpoly(inset(face, .7), B["r"] - .7)}" fill="none" stroke="#fff" stroke-opacity=".8" stroke-width="1.2"/>')
    if full:
        o.append(f'<path d="{rpoly(face, B["r"])} {rpoly(felt, fr)}" fill-rule="evenodd" fill="#fff" filter="url(#brush)" opacity=".5"/>')
    o.append(f'<path d="{rpoly(inset(felt, -1.1), fr + 1.1)}" fill="{A["dk"]}"/>')   # the felt sits a hair below the frame
    o.append(f'<path d="{rpoly(felt, fr)}" fill="url(#felt)"/>')
    o.append(f'<path d="{rpoly(felt, fr)}" fill="url(#feltKey)"/>')
    o.append('<g clip-path="url(#feltClip)">')
    if full:
        o.append(f'<rect x="{f(fx0)}" y="{f(fy0)}" width="{f(fx1 - fx0)}" height="{f(fy1 - fy0)}" fill="#fff" filter="url(#weave)" opacity=".32"/>')
    o.append(f'<path d="{rpoly(felt, fr)}" fill="none" stroke="#000" stroke-opacity=".45" stroke-width="{f(B["fw"] * .55)}" filter="url(#blurS)" transform="translate(2 2.4)"/>')

    # ---- the pin's shadow: its silhouette sheared away from the light about the hole
    # (the higher a point stands above the felt, the farther its shadow falls)
    d = norm((1, 1.15))
    m11, m12 = 1 + SHADOW * d[0] * u[0], SHADOW * d[0] * u[1]
    m21, m22 = SHADOW * d[1] * u[0], 1 + SHADOW * d[1] * u[1]
    ex, ey = E[0] - (m11 * E[0] + m12 * E[1]), E[1] - (m21 * E[0] + m22 * E[1])
    shear = f'matrix({f(m11)} {f(m21)} {f(m12)} {f(m22)} {f(ex)} {f(ey)})'
    pin_tf = f'translate({f(O[0])} {f(O[1])}) rotate({f(PIN["phi"])})'
    sil = pin_silhouette(g, L)
    o.append(f'<g opacity=".42" filter="url(#blurM)"><g transform="{shear}"><g transform="{pin_tf}">{sil}</g></g></g>')
    o.append(f'<g opacity=".3" filter="url(#blurS)"><g transform="{shear}"><g transform="{pin_tf}">{sil}</g></g></g>')
    o.append("</g>")
    o.append(f'<ellipse cx="{f(E[0] + 1.2)}" cy="{f(E[1] + 1.4)}" rx="{f(wn * 2.2)}" ry="{f(wn * 2.0)}" fill="url(#hole)"/>')   # contact shade
    o.append(f'<g clip-path="url(#faceClip)"><g opacity=".22" filter="url(#blurM)"><g transform="{shear}"><g transform="{pin_tf}"><path d="{cap_sil(g)}"/></g></g></g></g>')   # the cap's shadow on the frame

    # ---- the pin: needle, flange, waisted grip, cap (rim band, flat top, bevel, specular)
    o.append(f'<g transform="{pin_tf}">')
    o.append(f'<path d="M{f(-wn / 2)} {f(g["y_flb"] - 2)} L{f(wn / 2)} {f(g["y_flb"] - 2)} L{f(wn * .3)} {f(L)} L{f(-wn * .3)} {f(L)} Z" fill="url(#needle)"/>')
    o.append(f'<path d="{band(rf, ryf, g["y_fl"], g["y_flb"])}" fill="url(#flRim)"/>')
    o.append(f'<path d="M{f(-rf)} {f(g["y_flb"])} A{f(rf)} {f(ryf)} 0 0 0 {f(rf)} {f(g["y_flb"])}" fill="none" stroke="{Br["ink"]}" stroke-opacity=".45" stroke-width="{f(1.2 * sw)}"/>')
    o.append(f'<path d="{ell(0, g["y_fl"], rf, ryf)}" fill="url(#flTop)"/>')
    o.append(f'<path d="{ell(0, g["y_fl"], rf, ryf)}" fill="none" stroke="url(#flBevel)" stroke-width="{f(1.6 * sw)}"/>')
    o.append(f'<path d="{grip_path(g, b - 1, g["y_fl"] + .5)}" fill="url(#grip)"/>')
    o.append(f'<path d="{grip_path(g, b - 1, g["y_fl"] + .5)}" fill="url(#gripShade)"/>')
    o.append(f'<path d="{band(rc, ryc, a, b)}" fill="url(#capRim)"/>')
    o.append(f'<path d="M{f(-rc)} {f(b)} A{f(rc)} {f(ryc)} 0 0 0 {f(rc)} {f(b)}" fill="none" stroke="{Br["ink"]}" stroke-opacity=".55" stroke-width="{f(2 * sw)}"/>')
    o.append(f'<path d="{ell(0, a, rc, ryc)}" fill="url(#capTop)"/>')
    o.append(f'<path d="{ell(0, a, rc - 1.2 * sw, ryc - 1 * sw)}" fill="none" stroke="url(#bevel)" stroke-width="{f(2.4 * sw)}"/>')
    o.append(f'<path d="{ell(0, a, rc * .78, ryc * .74)}" fill="none" stroke="{Br["md"]}" stroke-opacity=".35" stroke-width="{f(1.2 * sw)}"/>')
    o.append(f'<ellipse cx="{f(-rc * .42)}" cy="{f(a - ryc * .1)}" rx="{f(rc * .2)}" ry="{f(ryc * .42)}" fill="url(#spec)"/>')
    o.append(f'<ellipse cx="{f(-rc * .62)}" cy="{f(a + g["Tc"] * .55)}" rx="{f(2.4 * sw)}" ry="{f(g["Tc"] * .3)}" fill="#fff" opacity=".7"/>')
    o.append("</g></g></svg>")
    svg = "\n".join(o)
    for a_, b_ in TUNES[tune].items():
        svg = svg.replace(a_, b_)
    return svg

def note(svg, text):
    """Put a comment after the opening <svg> tag."""
    i = svg.index(">") + 1
    return svg[:i] + "\n  <!-- " + text + " -->" + svg[i:]

if __name__ == "__main__":
    kw = dict(a.split("=", 1) for a in sys.argv[2:])
    svg = build(int(kw.get("size", 256)), int(kw.get("hint", 0)), kw.get("tune", "app"))
    open(sys.argv[1], "w", encoding="utf-8", newline="\n").write(svg + "\n")

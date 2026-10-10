// Generates the board's felt textures: ui/assets/felt-*.webp.
//
//   node scripts/felt/generate.mjs            write the three tiles (WebP, via headless Chrome)
//   node scripts/felt/generate.mjs --png      write PNGs instead (no Chrome needed; larger)
//   node scripts/felt/generate.mjs --out DIR  somewhere else (a preview, say)
//   env: CHROME (path to chrome.exe), SIZES to experiment
//
// What it makes: seamless, NEUTRAL (mid-grey, mean 128) pictures of wool felt
// that ui/styles/felt.css lays over the felt colour with soft-light, so one
// set works on every colour. All three are drawn from scratch by simulating
// the material (no photograph, nothing copied from anywhere):
//
//   felt-fibre   a dense mat of short, randomly turned, slightly curved,
//                soft-ended fibres, each a little lighter at its tip and
//                darker at its root (a faint nap), plus lint and specks
//   felt-cloud   the fabric's uneven density: soft clouds, and a few long tufts
//   felt-pile    larger, blurrier clumps of fibres that break the first up
//
// Tiles wrap at their edges (every stroke is drawn modulo the tile), and are
// drawn at 2x (the cloud, being soft, at 1x) so they stay crisp at 100-200 %
// Windows scaling: the CSS shows each at half its pixel size. Their sizes in CSS pixels (256, 357, 193) share
// no factor, so the three never line up and no repeat shows. Everything is
// seeded: the same script always writes the same files.
//
// Dependency-free: Node 22 only. WebP comes from Chrome's canvas encoder
// (CDP over the global WebSocket, as the demo renderer does).

import { spawn } from "node:child_process";
import { mkdirSync, writeFileSync, rmSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { tmpdir } from "node:os";
import { deflateSync } from "node:zlib";

const HERE = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const OUT = args.includes("--out") ? args[args.indexOf("--out") + 1] : join(HERE, "../../ui/assets");
const AS_PNG = args.includes("--png");
const CHROME = process.env.CHROME || "C:/Program Files/Google/Chrome/Application/chrome.exe";

// ---- tools ----
function rng(seed) {   // mulberry32
  let a = seed >>> 0;
  return () => { a = (a + 0x6d2b79f5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
const gauss = (r) => { let s = 0; for (let i = 0; i < 4; i++) s += r(); return (s - 2) * 1.7320508; };   // ~N(0,1)
const wrap = (v, n) => ((v % n) + n) % n;

/** Adds a soft round dot (gaussian, sigma `s`) at a fractional position, wrapped round the tile. */
function dot(buf, S, x, y, s, amp) {
  const R = Math.ceil(s * 2.2) + 1, x0 = Math.floor(x), y0 = Math.floor(y), k = -1 / (2 * s * s);
  for (let j = -R; j <= R; j++) {
    const yy = y0 + j, dy = yy - y, row = wrap(yy, S) * S;
    for (let i = -R; i <= R; i++) {
      const xx = x0 + i, dx = xx - x, d2 = dx * dx + dy * dy;
      if (d2 > (R + 0.5) * (R + 0.5)) continue;
      buf[row + wrap(xx, S)] += amp * Math.exp(d2 * k);
    }
  }
}

/** A curved strand: starts at (x, y) heading `ang`, bending by `curl` radians over its `len`, soft at both ends; `nap` makes the tip lighter than the root. */
function strand(buf, S, x, y, ang, len, curl, s, amp, nap) {
  const step = Math.max(0.45, s * 0.5), n = Math.max(2, Math.ceil(len / step));
  let a = ang - curl / 2;
  for (let i = 0; i <= n; i++) {
    const t = i / n;
    const ends = Math.pow(Math.sin(Math.PI * t), 0.7);
    dot(buf, S, x, y, s, amp * ends * (1 + nap * (2 * t - 1)) * step);
    a += curl / n;
    x += Math.cos(a) * step; y += Math.sin(a) * step;
  }
}

/** Tileable value noise: smooth random clouds, `cell` px apart, several octaves. */
function clouds(S, cell, octaves, r) {
  const out = new Float32Array(S * S);
  let amp = 1, total = 0;
  for (let o = 0; o < octaves; o++) {
    const n = Math.max(2, Math.round(S / (cell / 2 ** o))), lat = Float32Array.from({ length: n * n }, () => r() * 2 - 1);
    const sm = (t) => t * t * t * (t * (t * 6 - 15) + 10);
    for (let y = 0; y < S; y++) {
      const fy = (y / S) * n, y0 = Math.floor(fy), ty = sm(fy - y0);
      for (let x = 0; x < S; x++) {
        const fx = (x / S) * n, x0 = Math.floor(fx), tx = sm(fx - x0);
        const a = lat[(y0 % n) * n + (x0 % n)], b = lat[(y0 % n) * n + ((x0 + 1) % n)];
        const c = lat[((y0 + 1) % n) * n + (x0 % n)], d = lat[((y0 + 1) % n) * n + ((x0 + 1) % n)];
        out[y * S + x] += amp * ((a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty);
      }
    }
    total += amp; amp *= 0.55;
  }
  for (let i = 0; i < out.length; i++) out[i] /= total;
  return out;
}

/** Separable gaussian blur, wrapped. */
function blur(buf, S, sigma) {
  const R = Math.ceil(sigma * 3), k = Array.from({ length: 2 * R + 1 }, (_, i) => Math.exp(-((i - R) ** 2) / (2 * sigma * sigma)));
  const sum = k.reduce((a, b) => a + b); k.forEach((_, i) => (k[i] /= sum));
  const tmp = new Float32Array(buf.length);
  for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) { let v = 0; for (let i = -R; i <= R; i++) v += k[i + R] * buf[y * S + wrap(x + i, S)]; tmp[y * S + x] = v; }
  for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) { let v = 0; for (let i = -R; i <= R; i++) v += k[i + R] * tmp[wrap(y + i, S) * S + x]; buf[y * S + x] = v; }
}

// Contrast, the grey that means "no change", and how much the light side is held back: lighter flecks hurt
// the caption ink's contrast, dark pits between fibres do not, and soft-light lifts the average a little.
const STRENGTH = +(process.env.STRENGTH || 0.66), MID = +(process.env.MID || 127), HIGHLIGHTS = +(process.env.HIGHLIGHTS || 0.6);

/** Centres the picture on grey `MID` and scales it to the standard deviation `sd` (grey levels), with a soft shoulder so no pixel clips hard. */
function toGrey(buf, sd) {
  let m = 0; for (const v of buf) m += v; m /= buf.length;
  let q = 0; for (const v of buf) q += (v - m) ** 2; q = Math.sqrt(q / buf.length) || 1;
  const out = new Uint8Array(buf.length);
  for (let i = 0; i < buf.length; i++) {
    const z = ((buf[i] - m) / q) * sd / 110;           // -1..1 is the full range
    out[i] = Math.round(MID + 127 * Math.tanh(z * STRENGTH * (z > 0 ? HIGHLIGHTS : 1)));
  }
  return out;
}

// ---- the three tiles (S in pixels: 2x their CSS size) ----

/** Dense short fibres: the surface. */
function fibre(S) {
  const r = rng(7301), buf = new Float32Array(S * S);
  const per = S * S / 1000;                                    // fibres per 1000 px^2
  const nap = 0.55, napDir = 1.15;                              // the pile leans a little, down and to the right
  for (let i = 0; i < Math.round(per * 150); i++) {              // the mat: many fine, faint strands, any direction
    const ang = r() < 0.2 ? napDir + gauss(r) * 0.5 : r() * Math.PI * 2;
    strand(buf, S, r() * S, r() * S, ang, 4 + r() * r() * 12, gauss(r) * 0.55, 0.6 + r() * 0.3, (r() < 0.5 ? -1 : 1) * (0.35 + r() * 0.65), nap);
  }
  for (let i = 0; i < Math.round(per * 5); i++) {              // lint: a few longer, brighter strands, and rarer dark ones
    const light = r() < 0.7;
    strand(buf, S, r() * S, r() * S, r() * Math.PI * 2, 10 + r() * 14, gauss(r) * 0.9, 0.55 + r() * 0.25, (light ? 1 : -1) * (0.9 + r() * 0.8), nap);
  }
  for (let i = 0; i < Math.round(per * 22); i++) {             // specks
    dot(buf, S, r() * S, r() * S, 0.55 + r() * 0.5, (r() < 0.5 ? -1 : 1) * (1.5 + r() * 2.5));
  }
  return toGrey(buf, 22);
}

/** Cloudy density, and a few long soft tufts. */
function cloud(S) {
  const r = rng(1913), buf = new Float32Array(S * S);
  const c = clouds(S, S / 2.2, 4, r), c2 = clouds(S, S / 6, 3, r);
  for (let i = 0; i < buf.length; i++) buf[i] = c[i] * 1.0 + c2[i] * 0.45;
  const tufts = new Float32Array(S * S);
  for (let i = 0; i < Math.round(S * S / 1600); i++) strand(tufts, S, r() * S, r() * S, r() * Math.PI * 2, 18 + r() * 26, gauss(r) * 1.8, 1.2 + r() * 0.8, (r() < 0.5 ? -1 : 1) * (0.6 + r() * 0.6), 0.3);
  let tm = 0, tq = 0; for (const v of tufts) tm += v; tm /= tufts.length; for (const v of tufts) tq += (v - tm) ** 2; tq = Math.sqrt(tq / tufts.length) || 1;
  let bm = 0, bq = 0; for (const v of buf) bm += v; bm /= buf.length; for (const v of buf) bq += (v - bm) ** 2; bq = Math.sqrt(bq / buf.length) || 1;
  for (let i = 0; i < buf.length; i++) buf[i] = (buf[i] - bm) / bq + 0.55 * (tufts[i] - tm) / tq;
  return toGrey(buf, 12);
}

/** Larger clumps of fibres, soft: breaks the regularity of the first. */
function pile(S) {
  const r = rng(4409), buf = new Float32Array(S * S);
  for (let i = 0; i < Math.round(S * S / 420); i++) {
    strand(buf, S, r() * S, r() * S, r() * Math.PI * 2, 14 + r() * 22, gauss(r) * 1.4, 1.6 + r() * 1.0, (r() < 0.5 ? -1 : 1) * (0.4 + r() * 0.6), 0.4);
  }
  blur(buf, S, 0.8);
  return toGrey(buf, 10);
}

const TILES = [
  { name: "felt-fibre", css: 256, draw: fibre },
  { name: "felt-cloud", css: 357, draw: cloud, scale: 1 },   // soft clouds need no more than 1x: it saves 1.5 MB decoded
  { name: "felt-pile", css: 193, draw: pile },
];

/** A greyscale picture at half its size (2 x 2 averages). */
function halve(grey, S) {
  const n = S / 2, out = new Uint8Array(n * n);
  for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) out[y * n + x] = Math.round((grey[2 * y * S + 2 * x] + grey[2 * y * S + 2 * x + 1] + grey[(2 * y + 1) * S + 2 * x] + grey[(2 * y + 1) * S + 2 * x + 1]) / 4);
  return out;
}

// ---- output ----
function png(grey, S) {   // 8-bit greyscale PNG
  const crcT = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (b) => { let c = 0xffffffff; for (const x of b) c = crcT[(c ^ x) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const b = Buffer.alloc(12 + data.length); b.writeUInt32BE(data.length, 0); b.write(type, 4, "ascii"); data.copy(b, 8); b.writeUInt32BE(crc(b.subarray(4, 8 + data.length)), 8 + data.length); return b; };
  const head = Buffer.alloc(13); head.writeUInt32BE(S, 0); head.writeUInt32BE(S, 4); head[8] = 8; head[9] = 0;
  const raw = Buffer.alloc((S + 1) * S);
  for (let y = 0; y < S; y++) { raw[y * (S + 1)] = 0; Buffer.from(grey.buffer, y * S, S).copy(raw, y * (S + 1) + 1); }
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", head), chunk("IDAT", deflateSync(raw, { level: 9 })), chunk("IEND", Buffer.alloc(0))]);
}

async function chromeWebp(pngs) {   // PNG buffers -> WebP buffers, by Chrome's canvas encoder
  const PORT = 9300 + Math.floor(Math.random() * 400), profile = join(tmpdir(), `tack-felt-chrome-${PORT}`);
  rmSync(profile, { recursive: true, force: true }); mkdirSync(profile, { recursive: true });
  const proc = spawn(CHROME, ["--headless=new", `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`, "--no-first-run", "--force-color-profile=srgb", "about:blank"], { stdio: "ignore" });
  try {
    let tabs;
    for (let i = 0; i < 150; i++) { try { tabs = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json(); if (tabs.some((t) => t.type === "page")) break; } catch {} await new Promise((r) => setTimeout(r, 100)); }
    const ws = new WebSocket(tabs.find((t) => t.type === "page").webSocketDebuggerUrl);
    await new Promise((r) => ws.addEventListener("open", r, { once: true }));
    let id = 0; const pending = new Map();
    ws.addEventListener("message", (ev) => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } });
    const evaluate = (expression) => new Promise((res, rej) => { const i = ++id; pending.set(i, (m) => (m.error || m.result.exceptionDetails ? rej(new Error(JSON.stringify(m).slice(0, 500))) : res(m.result.result.value))); ws.send(JSON.stringify({ id: i, method: "Runtime.evaluate", params: { expression, awaitPromise: true, returnByValue: true } })); });
    const out = [];
    for (const p of pngs) {
      const b64 = await evaluate(`(async () => {
        const img = new Image(); img.src = "data:image/png;base64,${p.toString("base64")}"; await img.decode();
        const c = document.createElement("canvas"); c.width = img.width; c.height = img.height;
        c.getContext("2d").drawImage(img, 0, 0);
        const blob = await new Promise((r) => c.toBlob(r, "image/webp", ${WEBP_QUALITY}));
        const buf = new Uint8Array(await blob.arrayBuffer()); let s = ""; for (let i = 0; i < buf.length; i += 8192) s += String.fromCharCode(...buf.subarray(i, i + 8192));
        return btoa(s); })()`);
      out.push(Buffer.from(b64, "base64"));
    }
    ws.close();
    return out;
  } finally { proc.kill(); }
}
const WEBP_QUALITY = +(process.env.WEBP_QUALITY || 0.82);

mkdirSync(OUT, { recursive: true });
const greys = TILES.map((t) => ({ ...t, S: t.css * (t.scale || 2) }));
for (const t of greys) t.grey = t.scale === 1 ? halve(t.draw(t.S * 2), t.S * 2) : t.draw(t.S);
const pngs = greys.map((t) => png(t.grey, t.S));
const files = AS_PNG ? pngs : await chromeWebp(pngs);
greys.forEach((t, i) => {
  const path = join(OUT, `${t.name}.${AS_PNG ? "png" : "webp"}`);
  writeFileSync(path, files[i]);
  console.log(`${t.name}: ${t.S}x${t.S} px, shown at ${t.css} CSS px, ${(files[i].length / 1024).toFixed(1)} KB`);
});

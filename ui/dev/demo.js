// The README loop (demo.html): stands in for the backend and plays one
// snip. A dashed rectangle is drawn on a made-up app window, the snip lifts
// off and flies along a soft trail onto the board as it swings down
// (the app's own flight.js and board), and is tacked in with a brass pin
// that glints. Three prints are already on the board.
//
// Opened in a browser it plays, holds, and plays again. With ?capture it
// waits for window.demo.start(): the frame grabber steps the clock (virtual
// time and begin-frame control), so every frame lands exactly on time.
// Loaded before main.js: it installs window.__TAURI__ before the board boots.

import { COMMANDS, EVENTS } from "../scripts/ipc.js";

const params = new URLSearchParams(location.search);
const CAPTURE = params.has("capture");
const DPR = window.devicePixelRatio || 1;

// ---------------------------------------------------------------- the stand-in backend
const handlers = new Map();
const emit = (name, payload) => { for (const fn of handlers.get(name) || []) fn({ event: name, payload }); };
let boardReady;
const booted = new Promise((r) => { boardReady = r; });

window.__TAURI__ = {
  core: {
    invoke: (cmd) => {
      if (cmd === COMMANDS.BOARD_READY) {
        setTimeout(boardReady, 0);
        return Promise.resolve({ prints: existing, sound: false });
      }
      return Promise.resolve(null);
    },
  },
  event: {
    listen: (name, fn) => {
      if (!handlers.has(name)) handlers.set(name, new Set());
      handlers.get(name).add(fn);
      return Promise.resolve(() => handlers.get(name).delete(fn));
    },
  },
};

// ---------------------------------------------------------------- pictures
function canvas(w, h, scale = 1) {
  const c = document.createElement("canvas");
  c.width = Math.round(w * scale);
  c.height = Math.round(h * scale);
  const g = c.getContext("2d");
  g.scale(scale, scale);
  return [c, g];
}
const round = (g, x, y, w, h, r, fill) => { g.fillStyle = fill; g.beginPath(); g.roundRect(x, y, w, h, r); g.fill(); };

/** A sunset over hills: the first print on the board. */
function sunset(w, h) {
  const [c, g] = canvas(w, h);
  const sky = g.createLinearGradient(0, 0, 0, h);
  sky.addColorStop(0, "#ffb36b"); sky.addColorStop(0.55, "#ff7a8a"); sky.addColorStop(1, "#5a3a8a");
  g.fillStyle = sky; g.fillRect(0, 0, w, h);
  g.fillStyle = "rgba(255, 240, 200, .92)"; g.beginPath(); g.arc(w * 0.68, h * 0.46, h * 0.12, 0, 7); g.fill();
  for (const [col, k, f] of [["#3b2a5c", 0.64, 37], ["#26193f", 0.77, 53]]) {
    g.fillStyle = col; g.beginPath(); g.moveTo(0, h);
    for (let x = 0; x <= w; x += 8) g.lineTo(x, h * k - Math.sin(x / f + k * 9) * 12 - Math.sin(x / 13) * 3);
    g.lineTo(w, h); g.fill();
  }
  return c;
}

/** A code editor. */
function editor(w, h) {
  const [c, g] = canvas(w, h);
  g.fillStyle = "#1e1f26"; g.fillRect(0, 0, w, h);
  g.fillStyle = "#2a2c36"; g.fillRect(0, 0, w, 16);
  g.fillStyle = "#24252e"; g.fillRect(0, 16, 58, h);
  ["#ff5f57", "#febc2e", "#28c840"].forEach((col, i) => { g.fillStyle = col; g.beginPath(); g.arc(9 + i * 10, 8, 3, 0, 7); g.fill(); });
  for (let y = 26; y < h - 8; y += 10) round(g, 9, y, 18 + ((y * 7) % 27), 3.5, 2, "rgba(255,255,255,.17)");
  const cols = ["#c678dd", "#61afef", "#98c379", "#e5c07b", "#e06c75", "#56b6c2", "#abb2bf"];
  let ind = 0;
  for (let y = 26, i = 0; y < h - 8; y += 9, i++) {
    ind = Math.max(0, Math.min(4, ind + [0, 1, 0, -1, 1, 0, -1, -1, 0][i % 9]));
    let x = 70 + ind * 11;
    for (let j = 0; j < 1 + ((i * 5) % 4) && x < w - 12; j++) {
      const bw = 12 + ((i * 13 + j * 29) % 40);
      round(g, x, y, bw, 3.6, 2, cols[(i + j * 3) % cols.length]);
      x += bw + 6;
    }
  }
  return c;
}

/** A web page: a header band and cards. */
function webpage(w, h) {
  const [c, g] = canvas(w, h);
  g.fillStyle = "#f4f5f8"; g.fillRect(0, 0, w, h);
  g.fillStyle = "#dfe3ea"; g.fillRect(0, 0, w, 24);
  round(g, 9, 6, 76, 13, 5, "#fff"); round(g, 92, 8, 52, 9, 4, "#cfd4dd");
  round(g, 32, 31, w - 64, 11, 5, "#fff");
  g.fillStyle = "#3b6ef5"; g.fillRect(0, 50, w, 50);
  round(g, 22, 63, 130, 10, 3, "rgba(255,255,255,.95)"); round(g, 22, 80, 86, 7, 3, "rgba(255,255,255,.6)");
  const cw = (w - 56) / 3;
  const tints = ["#ffd4c2", "#c9f0e3", "#d8defd", "#fbe7a8", "#c9f0e3", "#ffd4c2"];
  for (let i = 0; i < 3; i++) for (let j = 0; j < 2; j++) {
    const x = 16 + i * (cw + 12), y = 114 + j * 58;
    if (y + 48 > h) continue;
    round(g, x, y, cw, 48, 6, "#fff");
    round(g, x + 7, y + 7, cw - 14, 20, 4, tints[i * 2 + j]);
    round(g, x + 7, y + 32, cw * 0.6, 4, 2, "#c3c8d2"); round(g, x + 7, y + 39, cw * 0.4, 4, 2, "#dde1e8");
  }
  return c;
}

/** The app window's content, 470 x 260: a dashboard. Returns the canvas at screen density. */
function dashboard() {
  const W = 470, H = 260;
  const [c, g] = canvas(W, H, DPR);
  g.fillStyle = "#1f2128"; g.fillRect(0, 0, W, H);
  // sidebar
  g.fillStyle = "#1a1c22"; g.fillRect(0, 0, 56, H);
  for (let i = 0; i < 5; i++) round(g, 18, 16 + i * 34, 20, 20, 6, i === 1 ? "#5b3fe0" : "#2c2f39");
  // header
  g.fillStyle = "#f2f3f7"; g.font = "600 15px 'Segoe UI Variable Display', 'Segoe UI', system-ui";
  g.fillText("Q3 review", 72, 28);
  g.fillStyle = "rgba(242,243,247,.5)"; g.font = "11px 'Segoe UI Variable Text', 'Segoe UI', system-ui";
  g.fillText("Updated 10:40 · All regions", 150, 28);
  // KPI cards
  const kpis = [["Revenue", "$4.82M", "#7c6cff"], ["Active users", "128k", "#2fd1a0"], ["Churn", "1.9%", "#ff8a65"]];
  kpis.forEach(([label, value, col], i) => {
    const x = 72 + i * 132;
    round(g, x, 42, 122, 48, 8, "#272a33");
    g.fillStyle = "rgba(242,243,247,.55)"; g.font = "10px 'Segoe UI Variable Text', 'Segoe UI', system-ui"; g.fillText(label, x + 10, 58);
    g.fillStyle = "#f2f3f7"; g.font = "600 16px 'Segoe UI Variable Display', 'Segoe UI', system-ui"; g.fillText(value, x + 10, 80);
    g.strokeStyle = col; g.lineWidth = 1.6; g.beginPath();
    for (let k = 0; k <= 8; k++) { const px = x + 70 + k * 5.5, py = 74 - Math.sin(k * 0.9 + i) * 5 - k * 0.8; k ? g.lineTo(px, py) : g.moveTo(px, py); }
    g.stroke();
  });
  // the chart card (the snip)
  chartCard(g, 72, 102, 228, 144);
  // a photo card
  round(g, 310, 102, 144, 144, 8, "#272a33");
  g.save(); g.beginPath(); g.roundRect(318, 110, 128, 96, 5); g.clip();
  g.drawImage(sunsetLake(128, 96), 318, 110); g.restore();
  round(g, 318, 214, 84, 6, 3, "rgba(242,243,247,.7)"); round(g, 318, 226, 56, 5, 3, "rgba(242,243,247,.35)");
  return c;
}

function chartCard(g, x, y, w, h) {
  round(g, x, y, w, h, 8, "#272a33");
  g.fillStyle = "#f2f3f7"; g.font = "600 11px 'Segoe UI Variable Text', 'Segoe UI', system-ui"; g.fillText("Weekly sign-ups", x + 12, y + 20);
  round(g, x + w - 52, y + 10, 40, 14, 7, "rgba(124,108,255,.25)");
  g.fillStyle = "#b9b0ff"; g.font = "600 9px 'Segoe UI Variable Text', 'Segoe UI', system-ui"; g.fillText("+18%", x + w - 44, y + 20.5);
  const base = y + h - 16, left = x + 12, right = x + w - 12;
  g.strokeStyle = "rgba(255,255,255,.07)"; g.lineWidth = 1;
  for (let k = 0; k < 4; k++) { g.beginPath(); g.moveTo(left, base - k * 26); g.lineTo(right, base - k * 26); g.stroke(); }
  const n = 12, bw = (right - left) / n;
  const vals = [0.32, 0.4, 0.36, 0.5, 0.46, 0.58, 0.54, 0.66, 0.62, 0.74, 0.8, 0.92];
  vals.forEach((v, i) => {
    const bh = v * (h - 46);
    const grad = g.createLinearGradient(0, base - bh, 0, base);
    grad.addColorStop(0, i === n - 1 ? "#ff7eb6" : "#8f7dff"); grad.addColorStop(1, i === n - 1 ? "#c4508a" : "#4b3fb0");
    round(g, left + i * bw + 3, base - bh, bw - 6, bh, 3, grad);
  });
  // trend line
  g.strokeStyle = "#5fe0c0"; g.lineWidth = 2; g.lineJoin = "round"; g.beginPath();
  vals.forEach((v, i) => { const px = left + i * bw + bw / 2, py = base - v * (h - 46) - 10; i ? g.lineTo(px, py) : g.moveTo(px, py); });
  g.stroke();
}

function sunsetLake(w, h) {
  const [c, g] = canvas(w, h, DPR);
  const sky = g.createLinearGradient(0, 0, 0, h);
  sky.addColorStop(0, "#2b3f8f"); sky.addColorStop(0.5, "#e2709a"); sky.addColorStop(1, "#ffb36b");
  g.fillStyle = sky; g.fillRect(0, 0, w, h);
  g.fillStyle = "#ffe2b8"; g.beginPath(); g.arc(w * 0.3, h * 0.55, 9, 0, 7); g.fill();
  g.fillStyle = "#2a2350"; g.beginPath(); g.moveTo(0, h * 0.68);
  for (let x = 0; x <= w; x += 6) g.lineTo(x, h * 0.62 - Math.abs(Math.sin(x / 19)) * 14);
  g.lineTo(w, h); g.lineTo(0, h); g.fill();
  return c;
}

const jpeg = (c) => c.toDataURL("image/jpeg", 0.9);
const MIN = 60_000;
const t0 = Date.now();
const print = (id, c, ago, extra = {}) => ({
  id, name: `Screenshot ${id}.png`, thumb: jpeg(c), width: c.width * 4, height: c.height * 4,
  pinnedAt: t0 - ago, kept: false, keptAt: null, kind: "image", note: null, ...extra,
});
const existing = [
  print("p-sunset", sunset(360, 240), 6 * MIN),
  print("p-editor", editor(360, 203), 50 * MIN),
  print("p-web", webpage(360, 225), 3 * 60 * MIN),
];

// ---------------------------------------------------------------- the window and the snip
const app = document.getElementById("app-canvas");
const painted = dashboard();
app.width = painted.width; app.height = painted.height;
app.getContext("2d").drawImage(painted, 0, 0);

const APP = { x: 452, y: 186 + 30 };                    // the canvas's place on the screen
const SNIP = { x: APP.x + 66, y: APP.y + 96, w: 240, h: 154 };   // the chart card, with a little margin
const snipCanvas = (() => {
  const [c, g] = canvas(SNIP.w, SNIP.h, DPR);
  g.setTransform(1, 0, 0, 1, 0, 0);
  g.drawImage(painted, (SNIP.x - APP.x) * DPR, (SNIP.y - APP.y) * DPR, SNIP.w * DPR, SNIP.h * DPR, 0, 0, c.width, c.height);
  return c;
})();
const snipImage = snipCanvas.toDataURL("image/png");
const snipDecoded = (() => { const i = new Image(); i.src = snipImage; return i.decode().catch(() => {}); })();

const sel = document.querySelector("#snip .sel");
const flash = document.querySelector("#snip .flash");
const cross = document.querySelector("#snip .cross");

// ---------------------------------------------------------------- the story
const ease = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
const clamp01 = (x) => Math.min(1, Math.max(0, x));
const span = (t, a, b) => clamp01((t - a) / (b - a));

const T = {
  dimIn: [40, 200], draw: [160, 720], release: 760, dimOut: [760, 920],
  flash: [760, 980], edgeOut: [800, 960], crossOut: [760, 860], launch: 800,
};

/** The snip overlay at time t (ms since the start). */
function snipAt(t) {
  const k = ease(span(t, ...T.draw));
  const w = SNIP.w * k, h = SNIP.h * k;
  Object.assign(sel.style, { left: `${SNIP.x}px`, top: `${SNIP.y}px`, width: `${w}px`, height: `${h}px` });
  const dim = 0.46 * span(t, ...T.dimIn) * (1 - span(t, ...T.dimOut));
  sel.style.setProperty("--dim", dim.toFixed(3));
  sel.style.setProperty("--edge", (span(t, T.draw[0], T.draw[0] + 60) * (1 - span(t, ...T.edgeOut))).toFixed(3));
  const f = t < T.flash[0] ? 0 : 0.5 * (1 - ease(span(t, ...T.flash)));
  flash.style.opacity = f.toFixed(3);
  cross.style.left = `${SNIP.x + w}px`;
  cross.style.top = `${SNIP.y + h}px`;
  cross.style.opacity = (span(t, 0, 120) * (1 - span(t, ...T.crossOut))).toFixed(3);
}

// The trail: the flight's path behind it, a soft ribbon of light that
// narrows and fades behind the snip. Points are taken while it travels
// (lift and press stay clean), and the ribbon is drawn as one strip of
// quads, so nothing overlaps into beads.
const trail = document.getElementById("trail");
trail.width = 960 * DPR; trail.height = 540 * DPR;
const tg = trail.getContext("2d");
const points = [];
const TRAVEL = [90, 640];          // ms into the flight: flight.js lifts for 110, travels for 560
const LIFE = 300;                  // ms a point of the trail lasts
let flownAt = null;
function trailAt(t) {
  const f = document.querySelector(".flight");
  if (f && flownAt === null) flownAt = t;
  if (f && t - flownAt >= TRAVEL[0] && t - flownAt <= TRAVEL[1]) {
    const r = f.getBoundingClientRect();
    const p = { x: r.left + r.width / 2, y: r.top + r.height / 2, t };
    const q = points[points.length - 1];
    if (!q || q.t !== t) points.push(p);
  }
  tg.setTransform(DPR, 0, 0, DPR, 0, 0);
  tg.clearRect(0, 0, 960, 540);
  const live = points.filter((p) => t - p.t < LIFE);
  if (live.length < 2) return;
  const fresh = (p) => 1 - (t - p.t) / LIFE;            // 1 new .. 0 gone
  const edge = (i, side, scale) => {
    const a = live[Math.max(0, i - 1)], b = live[Math.min(live.length - 1, i + 1)];
    const dx = b.x - a.x, dy = b.y - a.y, len = Math.hypot(dx, dy) || 1;
    const w = (1.5 + 13 * fresh(live[i])) * scale;
    return { x: live[i].x - (dy / len) * w * side, y: live[i].y + (dx / len) * w * side };
  };
  for (const [blur, scale, alpha] of [[12, 2.2, 0.16], [3, 1, 0.34]]) {
    tg.filter = `blur(${blur}px)`;
    for (let i = 1; i < live.length; i++) {
      const k = fresh(live[i]);
      const a0 = edge(i - 1, 1, scale), a1 = edge(i, 1, scale), b1 = edge(i, -1, scale), b0 = edge(i - 1, -1, scale);
      tg.fillStyle = `rgba(255, 243, 222, ${(alpha * k * k).toFixed(3)})`;
      tg.beginPath(); tg.moveTo(a0.x, a0.y); tg.lineTo(a1.x, a1.y); tg.lineTo(b1.x, b1.y); tg.lineTo(b0.x, b0.y); tg.closePath(); tg.fill();
    }
  }
  tg.filter = "none";
}

// The brass glint as the new print is tacked in.
new MutationObserver((list) => {
  for (const m of list) {
    const el = m.target;
    if (el.classList?.contains("slot") && el.dataset.id === "p-snip" && el.classList.contains("landing") && !el.classList.contains("demo-glint")) {
      el.classList.add("demo-glint");
    }
  }
}).observe(document.getElementById("prints"), { attributes: true, attributeFilter: ["class"], subtree: true });

let start = null;
function tick() {
  // performance.now(), not the frame time: under the frame grabber the
  // clock that timers follow is the virtual one.
  const t = performance.now() - start;
  snipAt(t);
  trailAt(t);
  if (t < 4200) requestAnimationFrame(tick);
}

function launch() {
  const at = Date.now();
  const snip = {
    id: "p-snip", name: "Screenshot snip.png", thumb: snipImage,
    width: SNIP.w * 4, height: SNIP.h * 4, pinnedAt: at, kept: true, keptAt: at, kind: "image", note: null,
  };
  emit(EVENTS.PRINT_ADDED, { print: snip, animate: true, flight: { from: { ...SNIP }, image: snipImage, found: true, tip: false } });
  emit(EVENTS.ORDER_CHANGED, { ids: ["p-snip", ...existing.map((p) => p.id)] });
  emit(EVENTS.REVEAL, { reason: "new" });
}

/** Plays the story from its first frame. */
function play() {
  start = performance.now();
  snipAt(0);
  requestAnimationFrame(tick);
  setTimeout(launch, T.launch);
}

const ready = Promise.all([booted, snipDecoded, document.fonts.ready]).then(() => new Promise((r) => setTimeout(r, 300)));
window.demo = { ready, start: play };

if (!CAPTURE) {
  // In a browser: play, hold, and start again (a fresh page is the clean first frame).
  ready.then(() => {
    play();
    setTimeout(() => location.reload(), 4600);
  });
}

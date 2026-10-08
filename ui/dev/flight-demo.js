// Preview only: fakes a Snipping Tool capture so the capture flight can be
// watched in a browser. "Fly" puts a made-up app window somewhere on the
// "screen" (a random rectangle), snips it, and sends board:print-added with
// a flight from exactly there, the way the backend would; "Fly (no match)"
// is the fallback, flying from a small rectangle at a made-up pointer.
// Loaded after fake-ipc.js (it needs window.preview) and main.js.

import { EVENTS } from "../scripts/ipc.js";

const preview = window.preview;
const board = document.getElementById("board");

// The flight's styles, which the preview page does not link itself.
const css = document.createElement("link");
css.rel = "stylesheet";
css.href = "../styles/flight.css";
document.head.appendChild(css);

// The fake backend does not know set_tip: answer it here.
const invoke = window.__TAURI__.core.invoke;
window.__TAURI__.core.invoke = (cmd, args = {}) =>
  cmd === "set_tip" ? Promise.resolve(console.info("[flight-demo] set_tip", JSON.stringify(args))) : invoke(cmd, args);

const rnd = (a, b) => a + Math.random() * (b - a);
let n = 0;
let tipShown = false;

/** A made-up app window, w x h CSS px, drawn at the screen's pixel density. */
function appWindow(w, h) {
  const dpr = window.devicePixelRatio || 1;
  const c = document.createElement("canvas");
  c.width = Math.round(w * dpr);
  c.height = Math.round(h * dpr);
  const g = c.getContext("2d");
  g.scale(dpr, dpr);
  const hue = Math.floor(rnd(0, 360));
  g.fillStyle = "#fbfbfd"; g.fillRect(0, 0, w, h);
  g.fillStyle = "#eceef3"; g.fillRect(0, 0, w, 30);
  g.fillStyle = `hsl(${hue} 70% 52%)`; g.fillRect(0, 30, w, 54);
  g.fillStyle = "rgba(255,255,255,.95)"; g.font = "600 15px Segoe UI, system-ui, sans-serif";
  g.fillText(`Quarterly report ${n}`, 16, 62);
  g.fillStyle = "#2b2f3a"; g.font = "12px Segoe UI, system-ui, sans-serif";
  g.fillText("app.example  ·  Dashboard  ·  Settings", 14, 20);
  const bars = Math.max(4, Math.floor((w - 40) / 26));
  for (let i = 0; i < bars; i++) {
    const bh = rnd(16, Math.max(20, h - 140));
    g.fillStyle = i % 3 ? `hsl(${hue} 60% 78%)` : `hsl(${hue} 70% 52%)`;
    g.beginPath(); g.roundRect(20 + i * 26, h - 20 - bh, 18, bh, 3); g.fill();
  }
  for (let y = 98; y < h - 30 && y < 150; y += 14) {
    g.fillStyle = "#c9ced8"; g.beginPath(); g.roundRect(16, y, rnd(80, w * 0.6), 6, 3); g.fill();
  }
  g.strokeStyle = "rgba(0,0,0,.12)"; g.strokeRect(0.5, 0.5, w - 1, h - 1);
  return c;
}

/** The canvas as a JPEG data URL, its long side at most `side` device px. */
function jpeg(canvas, side) {
  const s = Math.min(1, side / Math.max(canvas.width, canvas.height));
  if (s === 1) return canvas.toDataURL("image/jpeg", 0.85);
  const c = document.createElement("canvas");
  c.width = Math.round(canvas.width * s);
  c.height = Math.round(canvas.height * s);
  c.getContext("2d").drawImage(canvas, 0, 0, c.width, c.height);
  return c.toDataURL("image/jpeg", 0.85);
}

function fly(found) {
  n++;
  const vw = window.innerWidth, vh = window.innerHeight;
  const w = Math.round(rnd(260, Math.min(680, vw * 0.55)));
  const h = Math.round(rnd(170, Math.min(440, vh * 0.5)));
  const x = Math.round(rnd(20, vw - w - 20));
  const y = Math.round(rnd(Math.min(200, vh - h - 20), vh - h - 20));
  const canvas = appWindow(w, h);

  // On screen first: the window being snipped.
  const shot = document.createElement("canvas");
  shot.width = canvas.width; shot.height = canvas.height;
  shot.getContext("2d").drawImage(canvas, 0, 0);
  Object.assign(shot.style, {
    position: "fixed", left: `${x}px`, top: `${y}px`, width: `${w}px`, height: `${h}px`,
    zIndex: 0, boxShadow: "0 12px 40px rgba(0,0,0,.35)", borderRadius: "0",
  });
  document.body.appendChild(shot);
  shot.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 160 });

  setTimeout(() => {
    const dpr = window.devicePixelRatio || 1;
    const pointer = { x: x + w, y: y + h };
    const print = {
      id: `snip-${n}`,
      name: `Screenshot ${new Date().toISOString().slice(0, 19).replace("T", " ").replace(/:/g, "")}.png`,
      thumb: jpeg(canvas, 360),
      width: canvas.width, height: canvas.height,
      pinnedAt: Date.now(), kept: false, keptAt: null,
    };
    const from = found
      ? { x, y, w, h }
      : { x: pointer.x - 48, y: pointer.y - Math.round(48 * h / w), w: 96, h: Math.round(96 * h / w) };
    const tip = !tipShown;
    tipShown = true;
    preview.prints.unshift(print);
    const kept = preview.prints.filter((p) => p.kept).sort((a, b) => a.keptAt - b.keptAt);
    const rest = preview.prints.filter((p) => !p.kept).sort((a, b) => b.pinnedAt - a.pinnedAt);
    const tucked = board.classList.contains("tucked");
    preview.emit(EVENTS.PRINT_ADDED, { print, animate: true, flight: { from, image: jpeg(canvas, 1600), found, tip } });
    preview.emit(EVENTS.ORDER_CHANGED, { ids: [...kept, ...rest].map((p) => p.id) });
    if (tucked) {
      preview.reveal();
      // Back up about 1.2 s after the landing, like the backend, unless the
      // pointer is on the board or the tip is up.
      setTimeout(() => {
        if (!board.matches(":hover") && !document.querySelector(".tip")) preview.tuck();
      }, 2150);
    }
    setTimeout(() => {
      shot.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 300, fill: "forwards" }).onfinish = () => shot.remove();
    }, 1400);
  }, 420);
}

const controls = document.getElementById("controls");
for (const [label, found] of [["Fly (no match)", false], ["Fly", true]]) {
  const b = document.createElement("button");
  b.textContent = label;
  b.addEventListener("click", () => fly(found));
  controls?.prepend(b);
}
preview.fly = fly;

// The preview harness's stand-in for the Tauri backend. Installs a fake
// window.__TAURI__ that answers commands locally and fires events the way
// the real backend would, draws placeholder screenshots on a canvas, makes
// paper notes (preview.html starts with a few; ?notes=0 for none), pins
// what is dropped on the board (pin_text, pin_image), and wires the buttons
// in preview.html (also reachable as window.preview).
// Uses the same command and event names as the app (../scripts/ipc.js).

import { COMMANDS, EVENTS } from "../scripts/ipc.js";

const handlers = new Map();
const log = (msg) => {
  const box = document.getElementById("log");
  if (!box) return;
  const d = document.createElement("div");
  d.textContent = msg;
  box.prepend(d);
  while (box.children.length > 14) box.lastChild.remove();
};
const emit = (name, payload) => {
  log(`event ${name} ${payload ? JSON.stringify(payload).slice(0, 80) : ""}`);
  for (const fn of handlers.get(name) || []) fn({ event: name, payload });
};

// ---------------------------------------------------------------- placeholder screenshots
let seed = 7;
const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
const pick = (a) => a[Math.floor(rnd() * a.length)];
function shot(kind, W, H) {
  const long = 360, s = long / Math.max(W, H);
  const c = document.createElement("canvas");
  c.width = Math.round(W * s); c.height = Math.round(H * s);
  const g = c.getContext("2d");
  const w = c.width, h = c.height;
  const bar = (x, y, bw, bh, col, r = 2) => { g.fillStyle = col; g.beginPath(); g.roundRect(x, y, bw, bh, r); g.fill(); };
  if (kind === "editor") {
    g.fillStyle = "#1e1f26"; g.fillRect(0, 0, w, h);
    g.fillStyle = "#2a2c36"; g.fillRect(0, 0, w, 14);
    g.fillStyle = "#24252e"; g.fillRect(0, 14, 56, h);
    ["#ff5f57", "#febc2e", "#28c840"].forEach((col, i) => { g.fillStyle = col; g.beginPath(); g.arc(8 + i * 9, 7, 2.8, 0, 7); g.fill(); });
    for (let y = 22; y < h - 6; y += 9) bar(8, y, 18 + rnd() * 26, 3, "rgba(255,255,255,.18)");
    const cols = ["#c678dd", "#61afef", "#98c379", "#e5c07b", "#e06c75", "#56b6c2", "#abb2bf"];
    for (let y = 22, ind = 0; y < h - 6; y += 8) {
      ind = Math.max(0, Math.min(4, ind + pick([-1, 0, 0, 1])));
      let x = 66 + ind * 10;
      const n = 1 + Math.floor(rnd() * 4);
      for (let i = 0; i < n && x < w - 10; i++) { const bw = 10 + rnd() * 40; bar(x, y, bw, 3.2, pick(cols)); x += bw + 5; }
    }
  } else if (kind === "browser") {
    g.fillStyle = "#f4f5f8"; g.fillRect(0, 0, w, h);
    g.fillStyle = "#dfe3ea"; g.fillRect(0, 0, w, 22);
    bar(8, 5, 70, 14, "#ffffff", 5); bar(84, 7, 50, 10, "#cfd4dd", 4);
    bar(30, 28, w - 60, 10, "#ffffff", 5);
    g.fillStyle = "#3b6ef5"; g.fillRect(0, 44, w, 46);
    bar(20, 56, 120, 9, "rgba(255,255,255,.95)"); bar(20, 71, 80, 6, "rgba(255,255,255,.6)");
    const cw = (w - 50) / 3;
    for (let i = 0; i < 3; i++) for (let j = 0; j < 2; j++) {
      const x = 15 + i * (cw + 10), y = 102 + j * 52;
      if (y + 44 > h) continue;
      bar(x, y, cw, 44, "#ffffff", 5);
      bar(x + 6, y + 6, cw - 12, 18, pick(["#ffd4c2", "#c9f0e3", "#d8defd", "#fbe7a8"]), 3);
      bar(x + 6, y + 29, cw * .6, 4, "#c3c8d2"); bar(x + 6, y + 36, cw * .4, 4, "#dde1e8");
    }
  } else if (kind === "chart") {
    g.fillStyle = "#ffffff"; g.fillRect(0, 0, w, h);
    g.fillStyle = "#f1f3f7"; g.fillRect(0, 0, 60, h);
    for (let y = 14; y < h; y += 16) bar(10, y, 38, 5, y === 46 ? "#7b5cff" : "#d5d9e2");
    bar(72, 12, 110, 9, "#2b2f3a"); bar(72, 26, 70, 5, "#a8aebb");
    for (let i = 0; i < 3; i++) { bar(72 + i * ((w - 84) / 3), 40, (w - 84) / 3 - 8, 34, "#f5f6fa", 5); bar(78 + i * ((w - 84) / 3), 48, 30, 10, ["#7b5cff", "#18b47b", "#ff8a3d"][i]); }
    const base = h - 14, n = 14, bw = (w - 90) / n;
    for (let i = 0; i < n; i++) { const bh = 20 + rnd() * (base - 100); bar(76 + i * bw, base - bh, bw - 4, bh, i % 3 === 0 ? "#7b5cff" : "#c7bcff", 2); }
  } else if (kind === "chat") {
    g.fillStyle = "#ece5dd"; g.fillRect(0, 0, w, h);
    g.fillStyle = "#128c7e"; g.fillRect(0, 0, w, 30);
    g.fillStyle = "#d7f0ec"; g.beginPath(); g.arc(18, 15, 8, 0, 7); g.fill();
    bar(32, 10, 60, 6, "rgba(255,255,255,.9)"); bar(32, 19, 36, 4, "rgba(255,255,255,.6)");
    for (let y = 40; y < h - 40;) {
      const me = rnd() < .45, bw = 50 + rnd() * (w * .55), bh = 14 + Math.floor(rnd() * 3) * 9;
      bar(me ? w - bw - 10 : 10, y, bw, bh, me ? "#dcf8c6" : "#ffffff", 7);
      y += bh + 8;
    }
    bar(8, h - 28, w - 46, 20, "#ffffff", 10); g.fillStyle = "#128c7e"; g.beginPath(); g.arc(w - 18, h - 18, 10, 0, 7); g.fill();
  } else if (kind === "photo") {
    const sky = g.createLinearGradient(0, 0, 0, h);
    sky.addColorStop(0, "#ffb36b"); sky.addColorStop(.55, "#ff7a8a"); sky.addColorStop(1, "#5a3a8a");
    g.fillStyle = sky; g.fillRect(0, 0, w, h);
    g.fillStyle = "rgba(255,240,200,.9)"; g.beginPath(); g.arc(w * .68, h * .45, h * .12, 0, 7); g.fill();
    [["#3b2a5c", .62], ["#26193f", .75]].forEach(([col, k]) => {
      g.fillStyle = col; g.beginPath(); g.moveTo(0, h);
      for (let x = 0; x <= w; x += 12) g.lineTo(x, h * k - Math.sin(x / 37 + k * 9) * 14 - rnd() * 8);
      g.lineTo(w, h); g.fill();
    });
  } else {
    g.fillStyle = "#fbfbfd"; g.fillRect(0, 0, w, h);
    bar(10, 8, 90, 8, "#2b2f3a");
    const rows = Math.floor((h - 26) / 12);
    for (let r = 0; r < rows; r++) {
      const y = 24 + r * 12;
      bar(10, y, 60, 6, "#c9ced8");
      const x0 = 80 + rnd() * (w - 200), len = 40 + rnd() * 140;
      bar(x0, y - 1, Math.min(len, w - x0 - 10), 8, pick(["#ff8a65", "#4fc3f7", "#81c784", "#ba68c8", "#ffd54f"]), 4);
    }
  }
  return { thumb: c.toDataURL("image/jpeg", 0.86), width: W, height: H };
}

const kinds = [
  ["editor", 1920, 1080], ["browser", 1440, 900], ["chat", 750, 1334], ["chart", 1600, 1000],
  ["photo", 1200, 800], ["timeline", 2400, 700], ["browser", 1280, 1024], ["editor", 1100, 1100],
  ["chart", 1920, 1200], ["chat", 900, 1600], ["photo", 1000, 1300], ["timeline", 1800, 900],
];
let counter = 0;
const make = (ago = 0) => {
  const [kind, W, H] = kinds[counter % kinds.length];
  counter++;
  const d = new Date(2026, 9, 7, 23, 10 + counter, counter * 7 % 60);
  const stamp = d.toISOString().slice(0, 19).replace("T", " ").replace(/:/g, "");
  return {
    id: `print-${counter}`, name: `Screenshot ${stamp}.png`, ...shot(kind, W, H),
    pinnedAt: Date.now() - ago, kept: false, keptAt: null,
    kind: "image", note: null,
  };
};

// ---------------------------------------------------------------- placeholder notes
const NOTE_MAX = 20 * 1024;     // the backend keeps at most 20 KB of a note's text
const stampOf = (d) => d.toISOString().slice(0, 19).replace("T", " ").replace(/:/g, "");
let noteCounter = 0;
/** A note print, as the backend would send it: a lone http(s) URL is a link note. */
function makeNote(text, ago = 0) {
  noteCounter++;
  let body = String(text);
  let truncated = false;
  if (new TextEncoder().encode(body).length > NOTE_MAX) {
    while (new TextEncoder().encode(body).length > NOTE_MAX) body = body.slice(0, Math.floor(body.length * 0.98));
    truncated = true;
  }
  const t = body.trim();
  let link = null, domain = null;
  if (/^https?:\/\/\S+$/i.test(t)) {
    try { const u = new URL(t); link = t; domain = u.hostname.replace(/^www\./i, ""); } catch {}
  }
  return {
    id: `note-${noteCounter}`, name: `Note ${stampOf(new Date(Date.now() - ago))}.txt`,
    thumb: "", width: 0, height: 0,
    pinnedAt: Date.now() - ago, kept: false, keptAt: null,
    kind: "note", note: { text: body, link, domain, truncated },
  };
}

const LONG_NOTE = Array.from({ length: 160 }, (_, i) =>
  `${i + 1}. Meeting notes, part ${i + 1}: the board should stay calm, the row should read left to right, ` +
  "kept things first and the rest newest first. Notes are paper, prints are photos, and both hang from the same pins.").join("\n\n");
const SAMPLE_NOTES = [
  ["Call Sam back about the venue before 4, and ask whether the projector works with USB-C.", 5],
  ["https://github.com/tauri-apps/tauri/releases", 40],
  ["Release checklist\n- bump the version\n- tag v0.2.0\n- update the changelog\n- announce it", 3 * 60],
  [LONG_NOTE, 2 * 24 * 60],
];
const SAMPLE_ADDS = [
  "Remember: the Wi-Fi password is on the fridge.",
  "Pick up the prints from the framer on Thursday\nand drop the keys at reception",
  "Ideas for the talk: one slide per idea, no bullet points, end with a question.",
];
const SAMPLE_LINKS = [
  "https://developer.mozilla.org/en-US/docs/Web/API/HTML_Drag_and_Drop_API",
  "https://www.netbird.io/docs/how-to/getting-started",
  "https://en.wikipedia.org/wiki/Cork_(material)",
];

// ---------------------------------------------------------------- the fake backend
// In row order, like the backend: kept first (by keptAt), then newest first.
const prints = [];
const MIN = 60_000;
for (const ago of [3 * 24 * 60, 26 * 60, 2 * 60, 15, 2]) prints.push(make(ago * MIN));
// The preview starts with a few notes among the prints (?notes=0: none).
// Other pages (hero.html) only with ?notes=1, for the README's notes image.
const params = new URLSearchParams(location.search);
const withNotes = location.pathname.endsWith("/preview.html") ? params.get("notes") !== "0" : params.get("notes") === "1";
if (withNotes) {
  for (const [text, ago] of SAMPLE_NOTES) prints.push(makeNote(text, ago * MIN));
}
// ?n=20 starts with that many prints (ages spread over the last week).
const startN = Math.min(50, +new URLSearchParams(location.search).get("n") || 0);
for (let i = prints.length; i < startN; i++) prints.push(make((i * 7 + 4) * 37 * MIN));
// ?kept=2 keeps the first prints made (from three days ago on): brass pins, at the front.
const keptN = +new URLSearchParams(location.search).get("kept") || 0;
prints.slice(0, keptN).forEach((it, i) => Object.assign(it, { kept: true, keptAt: Date.now() - (keptN - i) * MIN }));
const arrange = () => prints.sort((a, b) =>
  a.kept !== b.kept ? (a.kept ? -1 : 1) : a.kept ? a.keptAt - b.keptAt : b.pinnedAt - a.pinnedAt);
const emitOrder = () => emit(EVENTS.ORDER_CHANGED, { ids: prints.map((x) => x.id) });
arrange();
let sound = true;

const commands = {
  [COMMANDS.BOARD_READY]: () => {
    // Reveal shortly after the page is listening, like the backend would.
    setTimeout(() => preview.reveal(), 350);
    return { prints: prints.slice(), sound };
  },
  [COMMANDS.SET_BOARD_RECT]: ({ x, y, w, h }) => {
    const dpr = window.devicePixelRatio || 1;
    const r = document.getElementById("rect");
    if (r) Object.assign(r.style, { left: x / dpr + "px", top: y / dpr + "px", width: w / dpr + "px", height: h / dpr + "px" });
    if (r) r.querySelector("span").textContent = `board rect ${x},${y} ${w}×${h} phys px`;
  },
  [COMMANDS.COPY_PRINT]: ({ id }) => setTimeout(() => emit(EVENTS.PRINT_COPIED, { id }), 40),
  [COMMANDS.OPEN_PRINT]: ({ id }) => log(`(would open ${prints.find((x) => x.id === id)?.note?.link || id})`),
  [COMMANDS.EDIT_PRINT]: ({ id }) => log(`(would edit ${prints.find((x) => x.id === id)?.name || id})`),
  [COMMANDS.PIN_TEXT]: ({ text }) => { if (String(text || "").trim()) pin(makeNote(text)); },
  [COMMANDS.PIN_IMAGE]: async ({ name, data }) => {
    const type = /\.jpe?g$/i.test(name || "") ? "image/jpeg" : "image/png";
    const bytes = Uint8Array.from(atob(data), (c) => c.charCodeAt(0));
    let bmp;
    try { bmp = await createImageBitmap(new Blob([bytes], { type })); } catch {
      emit(EVENTS.NOTICE, { text: "Can't pin that image" });
      return null;
    }
    const { width, height } = bmp;
    bmp.close();
    counter++;
    pin({
      id: `print-${counter}`, name: name || "Image.png", thumb: `data:${type};base64,${data}`,
      width, height, pinnedAt: Date.now(), kept: false, keptAt: null, kind: "image", note: null,
    });
    return null;
  },
  [COMMANDS.TAKE_FOCUS]: () => {},
  [COMMANDS.RELEASE_FOCUS]: () => {},
  [COMMANDS.HIDE_BOARD]: () => { setTimeout(() => emit(EVENTS.TUCK), 10); },
  [COMMANDS.START_DRAG]: ({ id }) => {
    setTimeout(() => emit(EVENTS.PRINT_DRAGGING, { id }), 30);
    setTimeout(() => emit(EVENTS.PRINT_DRAG_ENDED, { id }), 1600);
  },
  [COMMANDS.DISCARD_PRINT]: ({ id }) => remove(id, "fall"),
  [COMMANDS.CONTEXT_MENU]: ({ id, at }) => {
    if (at) console.info(`[fake-ipc] context_menu ${id} at ${at.x},${at.y} physical px`);
  },
  [COMMANDS.SET_HOVERING]: () => {},
  [COMMANDS.SET_KEPT]: ({ id, kept }) => {
    const it = prints.find((x) => x.id === id);
    if (!it) return;
    const before = prints.map((x) => x.id).join();
    if (it.kept !== kept) Object.assign(it, { kept, keptAt: kept ? Date.now() : null });
    arrange();
    emit(EVENTS.PRINT_UPDATED, { print: it });
    if (prints.map((x) => x.id).join() !== before) emitOrder();
  },
};
function remove(id, how) {
  const i = prints.findIndex((x) => x.id === id);
  if (i < 0) return;
  prints.splice(i, 1);
  setTimeout(() => emit(EVENTS.PRINT_REMOVED, { id, how }), 20);
}
function add(animate) {
  pin(make(), animate);
}
/** Pins a new print or note, live, the way the backend does. */
function pin(print, animate = true) {
  prints.unshift(print);
  arrange();
  // The history holds 50 unkept prints; the oldest ages out. Like the
  // backend, it is gone from the order at once and falls a moment later.
  const unkept = prints.filter((x) => !x.kept);
  const aged = unkept.length > 50 ? unkept[unkept.length - 1] : null;
  if (aged) prints.splice(prints.indexOf(aged), 1);
  emit(EVENTS.PRINT_ADDED, { print, animate });
  emitOrder();
  if (aged) setTimeout(() => emit(EVENTS.PRINT_REMOVED, { id: aged.id, how: "fall" }), 20);
}

window.__TAURI__ = {
  core: {
    invoke: (cmd, args = {}) => {
      if (cmd !== COMMANDS.SET_BOARD_RECT) log(`invoke ${cmd} ${JSON.stringify(args).slice(0, 120)}`);
      const fn = commands[cmd];
      if (!fn) return Promise.reject(new Error(`unknown command ${cmd}`));
      return Promise.resolve(fn(args) ?? null);
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

// ---------------------------------------------------------------- the buttons
const anyPrint = () => prints[Math.floor(Math.random() * prints.length)];
const preview = {
  emit, add, remove, prints, pin, makeNote,
  // A tray reveal: no focus. The hotkey is a keyboard open (keyboard.js).
  reveal: () => emit(EVENTS.REVEAL, { reason: "tray" }),
  revealKeyboard: () => emit(EVENTS.REVEAL, { reason: "hotkey" }),
  addNote: () => pin(makeNote(SAMPLE_ADDS[noteCounter % SAMPLE_ADDS.length])),
  addLink: () => pin(makeNote(SAMPLE_LINKS[noteCounter % SAMPLE_LINKS.length])),
  nothingSelected: () => emit(EVENTS.NOTICE, { text: "Nothing selected" }),
  tuck: () => emit(EVENTS.TUCK),
  addQuiet: () => add(false),
  fall: () => prints.length && commands[COMMANDS.DISCARD_PRINT]({ id: anyPrint().id }),
  keep: () => { if (!prints.length) return; const it = anyPrint(); commands[COMMANDS.SET_KEPT]({ id: it.id, kept: !it.kept }); },
  quiet: () => prints.length && remove(anyPrint().id, "quiet"),
  copied: () => prints.length && emit(EVENTS.PRINT_COPIED, { id: anyPrint().id }),
  gust: () => emit(EVENTS.GUST),
  drag: () => prints.length && commands[COMMANDS.START_DRAG]({ id: prints[prints.length - 1].id }),
  update: () => {
    if (!prints.length) return;
    const it = anyPrint();
    const fresh = shot(["photo", "chart", "editor"][Math.floor(Math.random() * 3)], it.width, it.height);
    Object.assign(it, fresh);
    emit(EVENTS.PRINT_UPDATED, { print: it });
  },
  clear: () => prints.slice().forEach((it, i) => setTimeout(() => remove(it.id, i ? "quiet" : "fall"), i * 45)),
  // Up to 20 prints, restored quietly with ages spread over the last week.
  fill: () => {
    while (prints.length < 20) {
      const it = make((prints.length * 7 + 4) * 37 * MIN);
      prints.push(it);
      arrange();
      emit(EVENTS.PRINT_ADDED, { print: it, animate: false });
    }
    emitOrder();
  },
  // Not something the backend does: a shuffled row, to watch the reorder.
  // The next real order-changed (keep, add) puts it back.
  shuffle: () => {
    const ids = prints.map((x) => x.id);
    for (let i = ids.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [ids[i], ids[j]] = [ids[j], ids[i]]; }
    emit(EVENTS.ORDER_CHANGED, { ids });
  },
  pointerLeft: () => emit(EVENTS.POINTER_LEFT),
  sound: () => { sound = !sound; emit(EVENTS.SETTINGS, { sound }); return sound; },
};
window.preview = preview;

const toggles = {
  sound: () => preview.sound(),
  light: () => document.body.classList.toggle("light"),
  // The board's theme: follow the system, or force light / dark.
  theme: (button) => {
    const next = { "": "light", light: "dark", dark: "" }[document.documentElement.dataset.theme || ""];
    if (next) document.documentElement.dataset.theme = next; else delete document.documentElement.dataset.theme;
    button.textContent = `Theme: ${next || "system"}`;
    return !!next;
  },
  "show-rect": () => document.body.classList.toggle("show-rect"),
};
for (const button of document.querySelectorAll("#controls button")) {
  const { action, toggle } = button.dataset;
  button.addEventListener("click", () => {
    if (action === "add") preview.add(true);
    else if (action) preview[action]();
    else if (toggle) button.classList.toggle("on", toggles[toggle](button));
  });
}

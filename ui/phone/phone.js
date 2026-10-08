// The phone board: shows what is pinned on the PC and pins photos and text
// onto it. Talks only to the Tack that served it (crates/tack-app/src/phone/routes.rs):
//   GET  /api/board       200 the board (kept first, then newest), 304 if unchanged
//                         403 {state: "waiting", code} | {state: "denied" | "busy"}
//                         anything else: the PC can't be reached
//   POST /api/ask         ask the PC again after a denial
//   GET  /api/thumb/<id>, /api/print/<id>  a print's pictures (addressed by content, so cached)
//   POST /api/pin         a JPEG or PNG, or text/plain
// Every call carries X-Tack: 1, which tells the PC it comes from this page.
"use strict";

const $ = (id) => document.getElementById(id);

// The longest side a photo is sent at. Phone cameras take 4000+ px; this
// keeps a send quick on a relayed link and is still sharp on a PC screen.
const MAX_SIDE = 2560;
// How long to wait before the next look at the board, by what the last one found.
const POLL_MS = { loading: 1500, ready: 3000, waiting: 2000, denied: 6000, busy: 5000, down: 4000 };
// A request with no answer by then counts as unreachable (a dead tunnel
// often just hangs); a photo upload gets longer.
const GET_TIMEOUT_MS = 6000;
const SEND_TIMEOUT_MS = 90000;
const PIN_COLOURS = 5; // p0 .. p4 in phone.css: tomato, cobalt, violet, lemon, white
const TIP_KEY = "tack.tip.home-screen";

// loading | ready | waiting | denied | busy | down ("down" is the unreachable card).
let mode = "loading";
let pc = "";
let you = "";
let code = ""; // shown while waiting, to match the one on the PC
let etag = ""; // the board last drawn, so an unchanged one costs nothing
let prints = [];
let turn = 0; // which refresh is the latest; older answers are dropped
let timer = 0;
let view = ""; // which card or grid is on screen
let shown = ""; // what the grid was built from

// localStorage is optional (private windows, blocked storage): never throw.
function keep(key, value) {
  try {
    if (value === undefined) return localStorage.getItem(key);
    localStorage.setItem(key, value);
  } catch {}
  return null;
}

// Builds an element: h("p", {class: "x"}, "text", child...). Text only, never HTML.
function h(tag, props, ...kids) {
  const el = document.createElement(tag);
  for (const [name, value] of Object.entries(props || {})) {
    if (name.startsWith("on")) el.addEventListener(name.slice(2), value);
    else el.setAttribute(name, value);
  }
  el.append(...kids.filter((kid) => kid != null));
  return el;
}

// A request that gives up after `ms`. Resolves to {status, text, etag};
// rejects when there is no answer at all.
async function request(url, init, ms) {
  const abort = new AbortController();
  const stop = setTimeout(() => abort.abort(), ms);
  const headers = { "X-Tack": "1", ...(init.headers || {}) };
  try {
    const response = await fetch(url, { cache: "no-store", ...init, headers, signal: abort.signal });
    return { status: response.status, text: await response.text(), etag: response.headers.get("ETag") || "" };
  } finally {
    clearTimeout(stop);
  }
}

function json(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

// ---- talking to the board ----

async function refresh() {
  clearTimeout(timer);
  const mine = ++turn;
  let next = "down";
  let data = null;
  try {
    const ask = mode === "ready" && etag ? { headers: { "If-None-Match": etag } } : {};
    const reply = await request("/api/board", ask, GET_TIMEOUT_MS);
    data = json(reply.text);
    if (reply.status === 304) next = "same";
    else if (reply.status === 200 && data && Array.isArray(data.prints)) {
      next = "ready";
      etag = reply.etag;
    } else if (reply.status === 403 && data && ["waiting", "denied", "busy"].includes(data.state)) next = data.state;
  } catch {}
  if (mine !== turn) return;

  if (next === "same") {
    next = "ready";
  } else {
    if (next !== "ready") etag = "";
    if (data && next !== "down") {
      pc = data.pc || pc;
      you = data.you || you;
      code = data.code || "";
    }
    prints = next === "ready" ? data.prints : [];
  }
  mode = next;
  render();
  timer = setTimeout(() => document.visibilityState === "visible" && refresh(), POLL_MS[mode]);
}

async function askAgain(button) {
  button.disabled = true;
  try {
    await request("/api/ask", { method: "POST" }, GET_TIMEOUT_MS);
  } catch {}
  refresh();
}

// ---- drawing ----

const on = () => pc || "your PC";

function render() {
  document.body.dataset.link = { ready: "ok", down: "down", denied: "down" }[mode] || "wait";
  document.title = pc ? `Tack · ${pc}` : "Tack";
  $("where").textContent = `on ${on()}`;
  $("status").textContent = {
    loading: "Connecting…",
    ready: `Connected through NetBird as ${you}`,
    waiting: `Waiting for approval as ${you}`,
    denied: `Not approved as ${you}`,
    busy: "Your PC is busy",
    down: "Not connected",
  }[mode];

  const ready = mode === "ready";
  const next = ready ? (prints.length ? "board" : "empty") : mode === "waiting" ? `waiting:${code}` : mode;
  if (next !== view) {
    view = next;
    shown = "";
    $("board").replaceChildren();
    const card = CARDS[next.split(":")[0]];
    $("state").replaceChildren(...(card ? card() : []));
    $("state").hidden = !card;
  }
  $("board").hidden = next !== "board";
  $("bar").hidden = !ready;
  $("tip").hidden = !ready || Boolean(keep(TIP_KEY));
  if (next === "board") drawBoard();
}

// One calm card per state: a title, one line or one action.
const CARDS = {
  loading: () => [pin(), h("h2", {}, "Connecting…"), h("p", {}, "Looking for your PC through NetBird.")],
  waiting: () => [
    pin(),
    h("h2", {}, "Check your PC"),
    h("p", {}, `Click Allow on ${on()} if it shows this code:`),
    h("p", { class: "code", "aria-label": `Code ${code.split("").join(" ")}` }, code),
  ],
  busy: () => [pin(), h("h2", {}, "Your PC is busy"), h("p", {}, "Other devices are asking right now. Try again in a minute.")],
  denied: () => [
    pin(),
    h("h2", {}, `${on()} didn't allow this phone.`),
    h("button", { class: "btn felt", type: "button", onclick: (e) => askAgain(e.currentTarget) }, "Ask again"),
  ],
  down: () => [
    pin(),
    h("h2", {}, `Can't reach ${on()}`),
    h("ul", { class: "check" }, h("li", {}, "NetBird is connected on this phone and the PC."), h("li", {}, "Tack is running with “Use on phone” on.")),
  ],
  empty: () => [pin(), h("h2", {}, "Nothing pinned yet"), h("p", {}, "Snip something on your PC, or pin a photo from here.")],
};

function pin() {
  return h("span", { class: "pin p0", "aria-hidden": "true" });
}

function drawBoard() {
  const key = JSON.stringify(prints.map((p) => [p.id, p.kept, p.text, p.thumb]));
  if (key === shown) return;
  shown = key;
  // Pins count from the oldest print, so a new one doesn't recolour the rest.
  $("board").replaceChildren(...prints.map((print, i) => item(print, prints.length - 1 - i)));
}

function item(print, order) {
  const li = h("li", {});
  const button = h("button", { class: "print", type: "button" });
  if (print.note) {
    const link = Boolean(print.link);
    button.classList.add("note");
    button.append(h("span", { class: "note-text" + (link ? " link" : "") }, print.text || print.link || ""));
    button.setAttribute("aria-label", `${link ? "Link" : "Note"}: ${print.text || print.link}. Tap to copy.`);
    button.addEventListener("click", () => copy(print.link || print.text || "", button));
  } else {
    button.append(h("img", { src: print.thumb, alt: "" }));
    button.setAttribute("aria-label", "Screenshot. Tap to view.");
    button.addEventListener("click", () => openPrint(print));
  }
  const head = h("span", { class: print.kept ? "pin kept" : `pin p${order % PIN_COLOURS}`, "aria-hidden": "true" });
  li.append(button, head);
  return li;
}

// ---- the tip ----

$("tip-close").addEventListener("click", () => {
  keep(TIP_KEY, "1");
  $("tip").hidden = true;
});

// ---- tap a print or a note ----

function openPrint(print) {
  $("full").src = print.full;
  $("viewer").showModal();
}

$("viewer-close").addEventListener("click", () => $("viewer").close());
$("viewer").addEventListener("close", () => $("full").removeAttribute("src"));

// Plain HTTP pages get no async clipboard API; the old copy command still
// works from a tap.
function copy(text, button) {
  const area = h("textarea", { readonly: "" });
  area.value = text;
  area.style.position = "fixed";
  area.style.opacity = "0";
  document.body.append(area);
  area.select();
  area.setSelectionRange(0, text.length);
  let ok = false;
  try {
    ok = document.execCommand("copy");
  } catch {}
  area.remove();
  if (!ok) return toast("Couldn't copy here. Press and hold the note instead.", true);
  const chip = h("span", { class: "chip" }, "Copied");
  button.append(chip);
  setTimeout(() => chip.remove(), 1400);
}

// ---- pinning ----

function toast(text, bad = false) {
  const el = $("toast");
  el.textContent = text;
  el.classList.toggle("bad", bad);
  el.classList.add("show");
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => el.classList.remove("show"), bad ? 5000 : 3000);
}

// Both buttons are off while a pin is on its way.
function setBusy(yes, what) {
  $("photo-btn").disabled = $("write-btn").disabled = yes;
  $("photo-btn").textContent = yes && what === "photo" ? "Pinning…" : "Pin photo";
  $("write-btn").textContent = yes && what === "text" ? "Pinning…" : "Pin text";
}

async function send(body, type, what) {
  setBusy(true, what);
  try {
    const reply = await request("/api/pin", { method: "POST", headers: { "Content-Type": type }, body }, SEND_TIMEOUT_MS);
    if (reply.status === 200) toast(`Pinned on ${on()}`);
    else toast(reply.text.trim().slice(0, 140) || `Couldn't pin that (${reply.status}).`, true);
  } catch {
    toast(`Couldn't reach ${on()}. Is NetBird connected?`, true);
  }
  setBusy(false);
  refresh();
}

// A photo, scaled down to MAX_SIDE and sent as JPEG; small PNGs (screenshots)
// go as they are, sharp.
async function prepare(file) {
  if (file.type === "image/png" && file.size < 8 * 1024 * 1024) return { blob: file, type: "image/png" };
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext("2d").drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.88));
  if (!blob) throw new Error("encode");
  return { blob, type: "image/jpeg" };
}

$("photo-btn").addEventListener("click", () => $("photo").click());
$("photo").addEventListener("change", async (event) => {
  const file = event.target.files[0];
  event.target.value = "";
  if (!file) return;
  setBusy(true, "photo");
  try {
    const { blob, type } = await prepare(file);
    await send(blob, type, "photo");
  } catch {
    setBusy(false);
    toast("That picture couldn't be read. Try a JPEG or PNG.", true);
  }
});

const sheet = $("text-sheet");
$("write-btn").addEventListener("click", () => {
  $("text").value = "";
  $("text-pin").disabled = true;
  sheet.showModal();
  $("text").focus();
});
$("text").addEventListener("input", () => ($("text-pin").disabled = !$("text").value.trim()));
$("text-cancel").addEventListener("click", () => sheet.close());
$("text-pin").addEventListener("click", () => sheet.close("pin"));
sheet.addEventListener("close", () => {
  const pinIt = sheet.returnValue === "pin";
  sheet.returnValue = ""; // Esc would keep the last value otherwise
  if (pinIt) send($("text").value, "text/plain; charset=utf-8", "text");
});

// ---- go ----

render();
refresh();
// Look again at once when the page comes back into view or the phone gets a network.
document.addEventListener("visibilitychange", () => document.visibilityState === "visible" && refresh());
window.addEventListener("online", refresh);

// The "Tack on your phone" window: a small window of its own (phone-link.html)
// that the tray's "Use on your phone…" opens, and that opens by itself, with
// the focus, when a new device asks to use the board.
//
// Tack can serve the board to the user's phone over NetBird (a private
// WireGuard network): the phone, with the NetBird app on the same account,
// opens http://<pc>.netbird.cloud:7717 and sees the board, and can pin photos
// and text onto it. This window is the switch for that, the way in (a QR code
// and the address), the answer to "pixel wants to use your board", and the
// list of devices that were allowed.
//
// Everything on the page comes from one PhoneState (docs/ipc.md): the backend
// sends it whenever anything changes (event phone:state) and every command
// answers with the new one. Each part of the page is rebuilt only when its
// own slice changes, so the keyboard focus is never pulled from under you.
//
// A request to use the board is a security prompt: it sits first, its Allow
// button takes the focus, Enter acts only on the button that has it, and Esc
// closes the window without allowing anything.

import * as ipc from "./ipc.js";

/**
 * @typedef {{ key: string, name: string, approvedAt: number }} Device
 * @typedef {{ key: string, name: string }} Asking
 * @typedef {{ on: boolean, netbird: "missing" | "disconnected" | "connected",
 *             serving: boolean, address: string | null, qr: string | null,
 *             error: string | null, pc: string | null,
 *             devices: Device[], pending: Asking[] }} PhoneState
 * `approvedAt`: ms since the epoch. `qr`: trusted SVG markup from the backend.
 * Device names are NetBird's names for them (set in NetBird, not by the
 * device over this connection); always shown as text.
 */

const { phoneState, setPhone, answerDevice, forgetDevice, openNetbirdDownload, closePhoneLink } = ipc;

const ICONS = {
  phone: `<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="6.75" y="2.75" width="10.5" height="18.5" rx="2.5" fill="none" stroke="currentColor" stroke-width="1.5"/><path d="M10.75 18h2.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>`,
  ask: `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2.8l7 2.6v5.6c0 4.4-2.8 8-7 10-4.2-2-7-5.6-7-10V5.4z" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"/><path d="M9 11.8l2.2 2.2L15.2 10" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>`,
  warn: `<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.8l6.4 11.4H1.6z" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/><path d="M8 6.2v3.4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/><circle cx="8" cy="11.6" r=".8" fill="currentColor"/></svg>`,
};

/** @type {PhoneState} */
let state = { on: false, netbird: "connected", serving: false, address: null, qr: null, error: null, pc: null, devices: [], pending: [] };
let loaded = false;
let switching = false;   // set_phone is on its way
const sigs = {};         // what each part of the page was last built from
let copyTimer = 0;
let announceTimer = 0;

// ---------------------------------------------------------------- the page

document.body.innerHTML = `
  <main class="dialog">
    <section id="asks" aria-label="Requests to use your board"></section>
    <header class="hero">
      <span class="hero-icon">${ICONS.phone}</span>
      <div>
        <h1 id="title" tabindex="-1">Your board, on your phone</h1>
        <p>Snip on your PC, it's on your phone. Pin a photo on your phone, it's on your PC. Any network.</p>
      </div>
    </header>
    <div class="card">
      <button type="button" class="switch" id="switch" role="switch" aria-checked="false" aria-labelledby="switch-label">
        <span id="switch-label">Use on phone</span>
        <span class="state" aria-hidden="true">Off</span>
        <span class="track" aria-hidden="true"></span>
      </button>
    </div>
    <div id="body" class="stack"></div>
    <section id="devices" aria-labelledby="devices-title"></section>
  </main>
  <footer>
    <p class="privacy">Your board stays on this PC.</p>
    <button type="button" class="std" id="close">Close</button>
  </footer>
  <div class="visually-hidden" id="live" role="status" aria-live="polite"></div>`;

const $ = (sel) => document.querySelector(sel);
const el = { asks: $("#asks"), body: $("#body"), devices: $("#devices"), sw: $("#switch"), live: $("#live"), title: $("#title") };

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

/** Says something to a screen reader without moving anything on screen. */
function announce(text) {
  clearTimeout(announceTimer);
  el.live.textContent = "";
  announceTimer = setTimeout(() => { el.live.textContent = text; }, 60);
}

/** Runs `build` (which returns the new HTML) only if `slice` changed since last time. */
function rebuild(part, host, slice, build) {
  const sig = JSON.stringify(slice);
  if (sigs[part] === sig) return false;
  sigs[part] = sig;
  host.innerHTML = build();
  return true;
}

/** "just now", "5 minutes ago", "yesterday", "3 days ago", or a date. */
function when(ms) {
  const mins = Math.floor((Date.now() - ms) / 60000);
  if (!(mins >= 1)) return "just now";
  if (mins < 60) return `${mins} minute${mins === 1 ? "" : "s"} ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? "" : "s"} ago`;
  const midnight = (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((midnight(new Date()) - midnight(new Date(ms))) / 86400000);
  if (days <= 1) return "yesterday";
  if (days < 7) return `${days} days ago`;
  return `on ${new Date(ms).toLocaleDateString(undefined, { day: "numeric", month: "short", year: days > 300 ? "numeric" : undefined })}`;
}

// ---------------------------------------------------------------- the parts

function renderSwitch(s) {
  el.sw.setAttribute("aria-checked", String(s.on));
  el.sw.querySelector(".state").textContent = s.on ? "On" : "Off";
}

/** The requests, loud and first. Returns true if the list changed. */
function renderAsks(s) {
  const hadFocus = el.asks.contains(document.activeElement);
  const before = new Set((sigs.asks ? JSON.parse(sigs.asks) : []).map((p) => p.key));
  const changed = rebuild("asks", el.asks, s.pending, () => s.pending.map((p, i) => `
    <div class="ask" role="group" aria-labelledby="ask-${i}" data-key="${esc(p.key)}">
      <div class="ask-head">
        <span class="ask-icon">${ICONS.ask}</span>
        <div>
          <h2 class="ask-title" id="ask-${i}">${esc(p.name)} wants to use your board</h2>
          <p class="ask-sub">It's a device on your NetBird network.</p>
        </div>
      </div>
      <div class="ask-actions">
        <button type="button" class="std" data-act="deny" aria-label="Don't allow ${esc(p.name)}">Don't allow</button>
        <button type="button" class="std accent" data-act="allow" aria-label="Allow ${esc(p.name)}">Allow</button>
      </div>
    </div>`).join(""));
  if (!changed) return;
  const fresh = s.pending.filter((p) => !before.has(p.key));
  if (!s.pending.length) {
    if (hadFocus) el.title.focus();   // the focused button just went away
    return;
  }
  // A person must see this: scroll to it, and put the focus on Allow when it
  // is new (or the one that had the focus went away).
  el.asks.parentElement.scrollTop = 0;
  if (fresh.length && loaded) announce(`${fresh.map((p) => p.name).join(" and ")} ${fresh.length > 1 ? "are" : "is"} asking to use your board`);
  if (fresh.length || hadFocus) el.asks.querySelector('[data-act="allow"]').focus();
}

/** What the switch leads to. */
function bodyHtml(s) {
  if (s.netbird === "missing") {
    return `
      <div class="card note">
        <p>Tack reaches your phone through NetBird, a free, open-source private network. Install it on this PC and on your phone, and sign in to the same account on both.</p>
        <button type="button" class="std accent" data-act="get-netbird">Get NetBird</button>
        <p class="small">Then turn this on.</p>
      </div>`;
  }
  if (!s.on) {
    return `<p class="quiet">Only devices on your NetBird network can ask, and only ones you allow get in.</p>`;
  }
  if (s.netbird === "disconnected") {
    return `<div class="card note"><p>NetBird isn't connected on this PC. Connect it, and Tack picks it up.</p></div>`;
  }
  if (s.serving && s.address) {
    return `
      <div class="card share">
        ${s.qr ? `<div class="qr" role="img" aria-label="QR code for ${esc(s.address)}">${s.qr}</div>` : ""}
        <p class="scan">${s.qr ? "Scan with your phone's camera" : "Open this on your phone"}</p>
        <div class="addr-row">
          <span class="addr" id="addr">${esc(s.address)}</span>
          <button type="button" class="std" data-act="copy" aria-describedby="addr">Copy</button>
        </div>
        <p class="quiet center needs">Your phone needs the NetBird app, signed in to the same account.</p>
      </div>`;
  }
  if (s.error) {
    return `<div class="card problem" role="alert">${ICONS.warn}<span>${esc(s.error)}</span></div>`;
  }
  return `<p class="quiet">Getting ready…</p>`;
}

function renderBody(s) {
  const slice = { n: s.netbird, on: s.on, serving: s.serving, a: s.address, q: s.qr, e: s.error };
  rebuild("body", el.body, slice, () => bodyHtml(s));
}

function renderDevices(s) {
  const hadFocus = el.devices.contains(document.activeElement);
  const changed = rebuild("devices", el.devices, s.devices, () => !s.devices.length ? "" : `
    <h2 class="sec" id="devices-title">Your devices</h2>
    <ul class="card list">
      ${s.devices.map((d) => `
      <li class="dev">
        <div class="dev-text">
          <span class="dev-name">${esc(d.name)}</span>
          <span class="dev-when">Allowed ${esc(when(d.approvedAt))}</span>
        </div>
        <button type="button" class="text" data-act="forget" data-key="${esc(d.key)}" aria-label="Remove ${esc(d.name)}">Remove</button>
      </li>`).join("")}
    </ul>`);
  // The Remove button that had the focus is gone: keep the keyboard nearby.
  if (changed && hadFocus) (el.devices.querySelector('[data-act="forget"]') || el.title).focus();
}

/** Takes a new PhoneState and brings the page up to date. */
function apply(next) {
  if (!next) return;
  const prev = state;
  state = { ...next, devices: next.devices || [], pending: next.pending || [] };
  renderSwitch(state);
  renderAsks(state);
  renderBody(state);
  renderDevices(state);
  if (loaded && state.serving && !prev.serving) announce("Ready. Scan the code with your phone.");
  loaded = true;
}

// ---------------------------------------------------------------- actions

async function toggle() {
  if (switching) return;
  switching = true;
  const want = !state.on;
  el.sw.setAttribute("aria-busy", "true");
  renderSwitch({ ...state, on: want });   // at once; the answer settles it
  const st = await setPhone(want);
  switching = false;
  el.sw.removeAttribute("aria-busy");
  if (st) apply(st);
  else { renderSwitch(state); announce("Couldn't change that. Try again."); }
}

async function answer(card, allow) {
  const key = card.dataset.key;
  const name = state.pending.find((p) => p.key === key)?.name || "The device";
  const buttons = card.querySelectorAll("button");
  buttons.forEach((b) => { b.disabled = true; });
  const st = await answerDevice(key, allow);
  if (st) {
    announce(allow ? `${name} can use your board` : `${name} was not allowed`);
    apply(st);
  } else {
    buttons.forEach((b) => { b.disabled = false; });
    announce("Couldn't answer. Try again.");
  }
}

async function forget(key) {
  const name = state.devices.find((d) => d.key === key)?.name || "The device";
  const st = await forgetDevice(key);
  if (st) { announce(`${name} removed`); apply(st); }
  else announce("Couldn't remove it. Try again.");
}

async function copy(btn) {
  const addr = state.address;
  if (!addr) return;
  try {
    await navigator.clipboard.writeText(addr);
  } catch (err) {
    console.warn("[tack] copy failed:", err);
    // Select it instead, so Ctrl+C is one key away.
    const sel = getSelection();
    sel.selectAllChildren($("#addr"));
    announce("Couldn't copy. The address is selected: press Ctrl+C.");
    return;
  }
  btn.textContent = "Copied";
  btn.classList.add("done");
  announce("Address copied");
  clearTimeout(copyTimer);
  copyTimer = setTimeout(() => {
    btn.textContent = "Copy";
    btn.classList.remove("done");
  }, 1800);
}

document.addEventListener("click", (e) => {
  const sw = e.target.closest("#switch");
  if (sw) { toggle(); return; }
  const btn = e.target.closest("[data-act]");
  if (!btn) return;
  switch (btn.dataset.act) {
    case "allow": answer(btn.closest(".ask"), true); break;
    case "deny": answer(btn.closest(".ask"), false); break;
    case "forget": forget(btn.dataset.key); break;
    case "copy": copy(btn); break;
    case "get-netbird": openNetbirdDownload(); break;
  }
});
$("#close").addEventListener("click", closePhoneLink);

document.addEventListener("keydown", (e) => {
  // Esc closes the window and nothing else: it never allows a device. Enter
  // and Space act on the button that has the focus, as in any Windows dialog.
  if (e.key === "Escape") { e.preventDefault(); closePhoneLink(); return; }
  // No browser behaviour in a dialog window.
  const k = e.key.toLowerCase();
  if (e.key === "F5" || ((e.ctrlKey || e.metaKey) && ["r", "p", "f", "g", "u", "s", "o", "n", "j", "h", "+", "-", "=", "0"].includes(k))) e.preventDefault();
}, true);
document.addEventListener("contextmenu", (e) => {
  // The address is selectable text: leave the menu to a click on it, nowhere else.
  if (!e.target.closest?.(".addr")) e.preventDefault();
});

// ---------------------------------------------------------------- start

async function load() {
  if (!ipc.available()) { console.error("[tack] window.__TAURI__ is not available"); return; }
  await ipc.on(ipc.EVENTS.PHONE_STATE, apply);
  const st = await phoneState();
  if (st) apply(st);
}

// No control takes the focus on open, unless a device is asking: then Allow
// does (renderAsks). Tab reaches everything; Esc closes from anywhere.
load();

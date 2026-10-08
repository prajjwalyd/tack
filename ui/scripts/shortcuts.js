// The shortcuts dialog: a small window of its own (shortcuts.html) that the
// tray's "Shortcuts…" opens. Two rows, each a global shortcut shown as
// Windows-style keycaps: show or hide the board, and pin the selection.
//
// Selecting a field (click, Enter or Space) captures the next chord typed:
// Tack's own hotkeys are paused meanwhile (pause_shortcuts), or pressing
// the current chord would toggle the board instead of being recorded.
// Esc cancels the capture, Backspace or Delete turns that shortcut off. A
// chord needs Win, Ctrl or Alt, or an F-key. Nothing is saved until Save,
// and only if every chord is free (set_shortcuts); a chord another app
// holds says so under its row and the dialog stays open.
//
// Chords travel as text: modifiers in the order Win, Ctrl, Alt, Shift, then
// the key, joined with "+", e.g. "Win+Alt+S"; "" is off (docs/ipc.md).

import * as ipc from "./ipc.js";

const ROWS = [
  { name: "toggle", label: "Show or hide the board" },
  { name: "pin", label: "Pin the selection" },
];
const HINT = "Use Win, Ctrl or Alt with a letter, number or F-key.";
const MODS = ["Win", "Ctrl", "Alt", "Shift"];
const NAMED = {
  Space: "Space", Insert: "Insert", Delete: "Delete", Home: "Home", End: "End",
  PageUp: "PageUp", PageDown: "PageDown",
  ArrowUp: "Up", ArrowDown: "Down", ArrowLeft: "Left", ArrowRight: "Right",
};
const WARN_SVG = `<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.8l6.4 11.4H1.6z" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/><path d="M8 6.2v3.4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/><circle cx="8" cy="11.6" r=".8" fill="currentColor"/></svg>`;

const chords = { toggle: "", pin: "" };
let defaults = { toggle: "Win+Alt+S", pin: "Win+Alt+C" };
let capturing = null;   // { row, held: string[] } while a field listens
let saving = false;

// ---------------------------------------------------------------- the page

document.body.innerHTML = `
  <main class="dialog">
    <p class="intro" id="intro">Select a shortcut, then press the keys you want.</p>
    <div class="card" role="group" aria-labelledby="intro">
      ${ROWS.map((r) => `
      <div class="row" data-name="${r.name}">
        <span class="label" id="${r.name}-label">${r.label}</span>
        <button type="button" class="field" id="${r.name}-field" aria-describedby="${r.name}-status"></button>
        <p class="status" id="${r.name}-status" role="status"></p>
      </div>`).join("")}
    </div>
  </main>
  <footer>
    <button type="button" class="text" id="reset">Reset to defaults</button>
    <span class="spacer"></span>
    <button type="button" class="std" id="cancel">Cancel</button>
    <button type="button" class="std accent" id="save">Save</button>
  </footer>`;

const $ = (sel) => document.querySelector(sel);
const rows = Object.fromEntries(ROWS.map((r) => [r.name, {
  ...r,
  field: $(`#${r.name}-field`),
  status: $(`#${r.name}-status`),
}]));

/** Keycaps for a chord ("" shows Off). */
function caps(keys) {
  if (!keys.length) return `<span class="off">Off</span>`;
  return keys.map((k) => `<kbd>${k}</kbd>`).join("");
}

function render(row) {
  const r = rows[row];
  const live = capturing?.row === row;
  r.field.classList.toggle("capturing", live);
  if (live) {
    const held = capturing.held;
    r.field.innerHTML = held.length
      ? `${caps(held)}<span class="wait">…</span>`
      : `<span class="wait">Press a shortcut…</span>`;
    r.field.setAttribute("aria-label", `${r.label}: press a shortcut. Escape cancels, Backspace turns it off.`);
    return;
  }
  const chord = chords[row];
  r.field.innerHTML = caps(chord ? chord.split("+") : []);
  r.field.setAttribute("aria-label", `${r.label}: ${chord ? chord.split("+").join(" + ") : "off"}. Press Enter to change.`);
}

function setStatus(row, text, tone = "warn") {
  const s = rows[row].status;
  s.className = `status ${text ? tone : ""}`;
  s.innerHTML = text ? `${WARN_SVG}<span></span>` : "";
  if (text) s.querySelector("span").textContent = text;
}

/** The status line for a Shortcut from the backend, as it stands. */
function describe(sc) {
  if (!sc) return "";
  if (sc.status === "in-use") return sc.chord ? `${sc.chord} is in use by another app` : "In use by another app";
  if (sc.status === "invalid") return HINT;
  return "";
}

// ---------------------------------------------------------------- capture

/** A key's name in a chord, from its physical code; null if it cannot be one. */
function keyName(code) {
  let m = /^Key([A-Z])$/.exec(code);
  if (m) return m[1];
  m = /^Digit([0-9])$/.exec(code);
  if (m) return m[1];
  m = /^F([1-9]|1[0-9]|2[0-4])$/.exec(code);
  if (m) return code;
  return NAMED[code] || null;
}

function heldMods(e) {
  return MODS.filter((m) => (m === "Win" ? e.metaKey : m === "Ctrl" ? e.ctrlKey : m === "Alt" ? e.altKey : e.shiftKey));
}

const isModifier = (e) => ["Meta", "OS", "Control", "Alt", "AltGraph", "Shift"].includes(e.key)
  || /^(Meta|OS|Control|Alt|Shift)(Left|Right)$/.test(e.code);

async function startCapture(row) {
  if (capturing?.row === row) return;
  if (capturing) endCapture();
  capturing = { row, held: [] };
  setStatus(row, "");
  render(row);
  await ipc.pauseShortcuts(true);
}

function endCapture() {
  const c = capturing;
  if (!c) return;
  capturing = null;
  render(c.row);
  ipc.pauseShortcuts(false);
}

function onCaptureKey(e) {
  const row = capturing.row;
  if (e.key === "Tab" && !e.ctrlKey && !e.altKey && !e.metaKey) { endCapture(); return; }  // let the focus move on
  e.preventDefault();
  e.stopPropagation();
  const mods = heldMods(e);
  if (isModifier(e)) {
    capturing.held = mods;
    render(row);
    return;
  }
  if (!mods.length && e.key === "Escape") { endCapture(); return; }
  if (!mods.length && (e.key === "Backspace" || e.key === "Delete")) {
    chords[row] = "";
    endCapture();
    return;
  }
  const key = keyName(e.code);
  const fkey = !!key && /^F\d+$/.test(key);
  const valid = !!key && (fkey || mods.some((m) => m !== "Shift"));
  if (!valid) {
    setStatus(row, HINT);
    capturing.held = [];
    render(row);
    return;
  }
  chords[row] = [...mods, key].join("+");
  setStatus(row, "");
  endCapture();
}

function onCaptureKeyUp(e) {
  if (!capturing) return;
  e.preventDefault();
  capturing.held = heldMods(e).filter((m) => !(e.key === "Meta" && m === "Win"));
  render(capturing.row);
}

// ---------------------------------------------------------------- actions

async function save() {
  if (saving) return;
  if (capturing) endCapture();
  for (const r of ROWS) setStatus(r.name, "");
  if (chords.toggle && chords.toggle === chords.pin) {
    setStatus("pin", "Already used to show or hide the board");
    rows.pin.field.focus();
    return;
  }
  saving = true;
  $("#save").disabled = true;
  const result = await ipc.setShortcuts(chords.toggle, chords.pin);
  saving = false;
  $("#save").disabled = false;
  if (!result) { setStatus("toggle", "Couldn't save the shortcuts. Try again."); return; }
  let ok = true;
  for (const r of ROWS) {
    const sc = result[r.name];
    if (!sc || sc.status === "ok" || sc.status === "off") continue;
    ok = false;
    setStatus(r.name, sc.status === "in-use" ? "In use by another app" : HINT);
  }
  if (ok) ipc.closeShortcuts();
  else (ROWS.map((r) => rows[r.name]).find((r) => r.status.textContent)?.field || $("#save")).focus();
}

function reset() {
  if (capturing) endCapture();
  for (const r of ROWS) {
    chords[r.name] = defaults[r.name] || "";
    setStatus(r.name, "");
    render(r.name);
  }
}

for (const r of Object.values(rows)) {
  // Click, Enter or Space (a button's own activation) starts the capture.
  r.field.addEventListener("click", () => startCapture(r.name));
}
$("#reset").addEventListener("click", reset);
$("#cancel").addEventListener("click", () => { if (capturing) endCapture(); ipc.closeShortcuts(); });
$("#save").addEventListener("click", save);

document.addEventListener("keydown", (e) => {
  if (capturing) { onCaptureKey(e); return; }
  if (e.key === "Escape") { e.preventDefault(); ipc.closeShortcuts(); return; }
  // Enter anywhere but on a button saves, as in any Windows dialog.
  if (e.key === "Enter" && !(e.target instanceof HTMLButtonElement)) { e.preventDefault(); save(); }
  // No browser behaviour in a dialog window.
  const k = e.key.toLowerCase();
  if (e.key === "F5" || ((e.ctrlKey || e.metaKey) && ["r", "p", "f", "g", "u", "s", "o", "n", "j", "h", "+", "-", "=", "0"].includes(k))) e.preventDefault();
}, true);
document.addEventListener("keyup", onCaptureKeyUp, true);
// A click anywhere but the listening field ends the capture.
document.addEventListener("pointerdown", (e) => {
  if (capturing && !rows[capturing.row].field.contains(e.target)) endCapture();
}, true);
document.addEventListener("contextmenu", (e) => e.preventDefault());
// Leaving the window mid-capture: stop listening, and never leave Tack's hotkeys paused.
window.addEventListener("blur", () => { if (capturing) endCapture(); });
window.addEventListener("pagehide", () => { if (capturing) endCapture(); });

// ---------------------------------------------------------------- start

async function load() {
  for (const r of ROWS) render(r.name);
  if (!ipc.available()) { console.error("[tack] window.__TAURI__ is not available"); return; }
  const st = await ipc.shortcutsState();
  if (!st) return;
  if (st.defaults) defaults = { ...defaults, ...st.defaults };
  for (const r of ROWS) {
    chords[r.name] = st[r.name]?.chord || "";
    render(r.name);
    setStatus(r.name, describe(st[r.name]));
  }
}

// No field takes the focus on open: the tray opens this with the mouse, and a
// focus ring would sit on the first field for nothing. Tab reaches the fields;
// Enter and Esc work from anywhere.
load();

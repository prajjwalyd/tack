// Shortcuts: the two global shortcuts, show or hide the board and pin the
// selection, each shown as keycaps in a field. Every change applies at once
// (there is no Save), through set_shortcuts with both chords.
//
// Selecting a field (click, Enter or Space) captures the next chord typed.
// Tack's own hotkeys are paused meanwhile (pause_shortcuts), or pressing the
// current chord would toggle the board instead of being recorded. Esc cancels,
// Backspace or Delete turns the shortcut off, Tab moves on. A chord that
// is free is applied and shown; one another app holds, or one that is not
// usable, is reported under its row and the old chord stays. The rules of a
// chord, and the chords' text form, are in ../chord.js.

import * as ipc from "../../ipc.js";
import { WARN_SVG } from "../../dialog.js";
import { HINT, heldAfterRelease, interpret, speak } from "../chord.js";
import { group } from "../markup.js";

const ROWS = [
  { name: "toggle", label: "Show or hide the board", use: "to show or hide the board" },
  { name: "pin", label: "Pin the selection", use: "to pin the selection" },
];
const FALLBACK = { toggle: "Win+Alt+S", pin: "Win+Alt+C" };

/** The status line for a backend Shortcut: what is wrong with it, or "". */
function describe(sc) {
  if (!sc) return "";
  if (sc.status === "in-use") return sc.chord ? `${sc.chord} is in use by another app` : "In use by another app";
  if (sc.status === "invalid") return HINT;
  return "";
}

/** Keycaps for a chord's keys (none: "Off"). The keys are ours (keyName), never user text. */
const caps = (keys) => (keys.length ? keys.map((k) => `<kbd>${k}</kbd>`).join("") : `<span class="off">Off</span>`);

export function mount(host, api) {
  group(host, "shortcuts", "Shortcuts", `
    <p class="group-desc" id="shortcuts-intro">Select a shortcut, then press the keys you want.</p>
    <div class="card" role="group" aria-labelledby="shortcuts-title" aria-describedby="shortcuts-intro">
      ${ROWS.map((r) => `
      <div class="row shortcut">
        <span class="row-title" id="shortcut-${r.name}-label">${r.label}</span>
        <button type="button" class="field" id="shortcut-${r.name}-field" aria-describedby="shortcut-${r.name}-status"></button>
        <p class="status" id="shortcut-${r.name}-status" role="status"></p>
      </div>`).join("")}
    </div>
    <div class="reset">
      <button type="button" class="text" id="shortcuts-reset" aria-disabled="true">Reset to defaults</button>
    </div>`);

  const rows = Object.fromEntries(ROWS.map((r) => [r.name, {
    ...r,
    field: host.querySelector(`#shortcut-${r.name}-field`),
    status: host.querySelector(`#shortcut-${r.name}-status`),
  }]));
  const reset = host.querySelector("#shortcuts-reset");

  const chords = { toggle: "", pin: "" };   // what is applied
  const asked = {};                          // a chord shown while set_shortcuts is on its way
  let defaults = FALLBACK;
  let capturing = null;                      // { row, held: string[] } while a field listens
  let queue = Promise.resolve();             // set_shortcuts calls, one after another

  function render(row) {
    const r = rows[row];
    const live = capturing?.row === row;
    r.field.classList.toggle("capturing", live);
    if (live) {
      const held = capturing.held;
      r.field.innerHTML = held.length ? `${caps(held)}<span class="wait">…</span>` : `<span class="wait">Press a shortcut…</span>`;
      r.field.setAttribute("aria-label", `${r.label}: press a shortcut. Escape cancels, Backspace turns it off.`);
      return;
    }
    const chord = asked[row] ?? chords[row];
    r.field.innerHTML = caps(chord ? chord.split("+") : []);
    r.field.setAttribute("aria-label", `${r.label}: ${speak(chord)}. Press Enter to change.`);
  }

  function renderAll() {
    for (const r of ROWS) render(r.name);
    reset.setAttribute("aria-disabled", String(ROWS.every((r) => chords[r.name] === defaults[r.name])));
  }

  function setStatus(row, text) {
    const s = rows[row].status;
    s.className = text ? "status warn" : "status";
    s.innerHTML = text ? `${WARN_SVG}<span></span>` : "";
    if (text) s.querySelector("span").textContent = text;
  }

  /**
   * Asks for `change` (a part of the chords) to be applied. If every chord is
   * free it is, and shown; otherwise nothing changes and each row that is the
   * trouble says so. `row` is the row being changed (null: both, a reset).
   */
  async function apply(change, row) {
    const next = { ...chords, ...change };
    for (const r of ROWS) setStatus(r.name, "");
    if (row && next.toggle && next.toggle === next.pin) {
      setStatus(row, `Already used ${ROWS.find((r) => r.name !== row).use}`);
      return false;
    }
    const result = await ipc.setShortcuts(next.toggle, next.pin);
    if (!result) {
      setStatus(row ?? "toggle", "Couldn't change the shortcut. Try again.");
      api.announce("Couldn't change the shortcut. Try again.");
      return false;
    }
    let ok = true;
    for (const r of ROWS) {
      const status = result[r.name]?.status;
      if (!status || status === "ok" || status === "off") continue;
      ok = false;
      setStatus(r.name, describe(result[r.name]));
    }
    if (!ok) return false;
    for (const r of ROWS) chords[r.name] = result[r.name]?.chord ?? next[r.name];
    return true;
  }

  /** Applies one row's new chord, after any change still in flight. */
  function changeRow(row, chord) {
    asked[row] = chord;   // at once; the answer settles it
    render(row);
    queue = queue.then(async () => {
      const done = await apply({ [row]: chord }, row);
      delete asked[row];
      if (done) api.announce(chord ? `${rows[row].label}: ${speak(chord)}` : `${rows[row].label}: turned off`);
      renderAll();
    });
    return queue;
  }

  async function startCapture(row) {
    if (capturing?.row === row) return;
    if (capturing) endCapture();
    capturing = { row, held: [] };
    setStatus(row, "");
    render(row);
    await ipc.pauseShortcuts(true);
  }

  /** Stops listening and gives Tack's hotkeys back (resolves once they are back). */
  function endCapture() {
    const c = capturing;
    if (!c) return Promise.resolve();
    capturing = null;
    render(c.row);
    return ipc.pauseShortcuts(false);
  }

  function onKeyDown(e) {
    if (!capturing) return;
    const row = capturing.row;
    const did = interpret(e);
    if (did.act === "leave") { endCapture(); return; }   // the focus moves on
    e.preventDefault();
    e.stopPropagation();   // so the page's own Esc, which closes the window, never sees it
    switch (did.act) {
      case "held":
        capturing.held = did.mods;
        render(row);
        break;
      case "cancel":
        endCapture();
        break;
      case "off":
      case "chord":
        // Applying changes the hotkeys, so they are given back first.
        endCapture().then(() => changeRow(row, did.act === "off" ? "" : did.chord));
        break;
      default:
        setStatus(row, HINT);
        capturing.held = [];
        render(row);
    }
  }

  function onKeyUp(e) {
    if (!capturing) return;
    e.preventDefault();
    capturing.held = heldAfterRelease(e);
    render(capturing.row);
  }

  for (const r of Object.values(rows)) r.field.addEventListener("click", () => startCapture(r.name));
  reset.addEventListener("click", () => {
    if (reset.getAttribute("aria-disabled") === "true") return;
    if (capturing) endCapture();
    queue = queue.then(async () => {
      if (await apply({ toggle: defaults.toggle, pin: defaults.pin }, null)) api.announce("Shortcuts reset");
      renderAll();
    });
  });

  // On the window's capture phase, so a listening field gets every key before
  // anything else (Esc, Backspace and the rest never reach the page's handlers).
  window.addEventListener("keydown", onKeyDown, true);
  window.addEventListener("keyup", onKeyUp, true);
  // A click anywhere but the listening field ends the capture.
  window.addEventListener("pointerdown", (e) => {
    if (capturing && !rows[capturing.row].field.contains(e.target)) endCapture();
  }, true);
  // Leaving mid-capture must never leave Tack's hotkeys paused.
  window.addEventListener("blur", () => { endCapture(); });
  window.addEventListener("pagehide", () => { endCapture(); });

  renderAll();
  ipc.shortcutsState().then((st) => {
    if (!st) return;
    if (st.defaults) defaults = { ...defaults, ...st.defaults };
    for (const r of ROWS) {
      chords[r.name] = st[r.name]?.chord || "";
      setStatus(r.name, describe(st[r.name]));
    }
    renderAll();
  });
  return {};
}

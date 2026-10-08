// Paper notes: text and links pinned in the row like prints. A note is a print
// with `data.kind` "note" (docs/ipc.md): print.js builds it with the same layers
// and calls `fillNote` for its paper. This module also decides what "open"
// means per kind (`openItem`) and unfolds a text note into a bigger sheet
// hanging from its pin below the board.
//
// The sheet is the board's only scrollable thing (the wheel scrolls it, not
// the row). It folds on Esc, double click, a click elsewhere, the row scrolling
// or moving, the note going, and the tuck. While down, the board rect sent to
// the backend includes it (layout.js), so it is not click-through.

import * as ipc from "./ipc.js";
import { clearHover } from "./gestures.js";
import { PIN_Y, PRINT_TOP, TOP_HIDDEN, onLayout, setExtraRect } from "./layout.js";
import { onScrollState, scrollPos } from "./scroll.js";
import { dom, state } from "./state.js";

const SHEET_W = 360;          // widest unfolded sheet, CSS px (max height is in note.css)
const SHEET_MIN_W = 240;
const SHEET_PIN_Y = 9;        // pin centre from the sheet's top (--pin-y in note.css)
const EDGE = 8;               // the sheet keeps this far from the window's sides
const NOTE_CHARS = 360;       // about four lines; the paper clamps the rest

const LINK_SVG = `<svg viewBox="0 0 10 10" aria-hidden="true"><path d="M4.3 5.7l1.4-1.4M3.6 4.2L2.5 5.3a1.6 1.6 0 0 0 2.2 2.2l1.1-1.1M6.4 5.8l1.1-1.1a1.6 1.6 0 0 0-2.2-2.2L4.2 3.6" stroke="currentColor" stroke-width="1.1" stroke-linecap="round" fill="none"/></svg>`;

/** True for a note (text or link), false for a screenshot. */
export function isNote(data) {
  return data?.kind === "note" && !!data.note;
}

/** A note's link, if the whole note is one. */
export function linkOf(data) {
  return isNote(data) ? data.note.link || null : null;
}

/** An address as a note shows it: no scheme, no trailing slash. */
export function bareLink(url) {
  return String(url).replace(/^https?:\/\//i, "").replace(/\/$/, "");
}

/** Writes an address into `el` with break chances after / ? & = # and before a dot, so it wraps between parts. */
function writeAddress(el, url) {
  el.textContent = "";
  for (const part of url.split(/(?<=[/?&=#])|(?=\.)/)) {
    if (el.firstChild) el.appendChild(document.createElement("wbr"));
    el.appendChild(document.createTextNode(part));
  }
}

/** Builds (or rebuilds) a note's paper and caption parts. */
export function fillNote(print) {
  const note = print.data.note || { text: "" };
  const link = note.link || null;
  const paper = print.paper;
  print.slot.classList.add("is-note");
  print.slot.classList.toggle("is-link", !!link);
  paper.textContent = "";
  const p = document.createElement("p");
  p.className = "note-text";
  if (link) writeAddress(p, bareLink(link));
  else p.textContent = (note.text || "").trim().slice(0, NOTE_CHARS);
  paper.appendChild(p);
  if (link) {
    const d = document.createElement("span");
    d.className = "note-domain";
    d.innerHTML = LINK_SVG;
    const name = document.createElement("span");
    name.textContent = note.domain || bareLink(link).split("/")[0];
    d.appendChild(name);
    paper.appendChild(d);
  }
  // A link's caption: full address over the age (print.js fills the age).
  const caption = print.caption;
  caption.classList.toggle("link", !!link);
  caption.textContent = "";
  print.captionAge = null;
  if (link) {
    const url = document.createElement("span");
    url.className = "cap-url";
    writeAddress(url, link);
    const age = document.createElement("span");
    age.className = "cap-age";
    caption.append(url, age);
    print.captionAge = age;
  }
}

/** "Open" for any kind: a screenshot or link goes to the backend; a text note unfolds (or folds). `focus` moves keyboard focus into the sheet. */
export function openItem(print, { focus = false } = {}) {
  if (!print || print.leaving) return;
  if (isNote(print.data) && !linkOf(print.data)) {
    if (sheet?.print === print) fold();
    else unfold(print, focus);
    return;
  }
  ipc.openPrint(print.id);
}

let sheet = null;   // { print, el, scroller, x }

export function isUnfolded(print = null) {
  return !!sheet && (!print || sheet.print === print);
}

/** The unfolded sheet's scrolling element (for keyboard.js), or null. */
export function sheetElement() {
  return sheet?.el || null;
}

function unfold(print, focus) {
  if (!state.revealed) return;
  fold(true);
  if (state.hovered === print) clearHover();

  const note = print.data.note;
  const el = document.createElement("div");
  el.className = "sheet";
  el.setAttribute("role", "document");
  el.setAttribute("aria-label", "Note");
  el.innerHTML = `<div class="paper"></div><div class="sheet-scroll" tabindex="-1"><p class="sheet-text"></p></div>
    <span class="pin" aria-hidden="true"><span class="pin-head"></span><span class="pin-brass"></span></span>`;
  el.querySelector(".sheet-text").textContent = note.text || "";
  if (note.truncated) {
    const foot = document.createElement("p");
    foot.className = "sheet-foot";
    foot.textContent = "Shortened to 20 KB";
    el.querySelector(".sheet-scroll").appendChild(foot);
  }
  const pin = el.querySelector(".pin");
  pin.dataset.c = print.pin.dataset.c || "sage";
  if (print.data.kept) {
    pin.querySelector(".pin-brass").style.opacity = "1";
  }

  // Placed from layout, not measured boxes: the board may still be turning on its hinge.
  const boardLeft = (window.innerWidth - state.boardW) / 2;
  const pinX = state.boardW / 2 + print.x - scrollPos() + print.w / 2;
  const pinY = TOP_HIDDEN + PRINT_TOP + PIN_Y;
  const width = Math.max(SHEET_MIN_W, Math.min(SHEET_W, window.innerWidth - 2 * EDGE));
  const minLeft = EDGE - boardLeft, maxLeft = window.innerWidth - EDGE - boardLeft - width;
  const left = Math.round(Math.max(minLeft, Math.min(maxLeft, pinX - width / 2)));
  Object.assign(el.style, { left: `${left}px`, top: `${Math.round(pinY - SHEET_PIN_Y)}px`, width: `${width}px` });
  el.style.setProperty("--pin-x", `${Math.round(pinX - left)}px`);

  dom.board.appendChild(el);
  const scroller = el.querySelector(".sheet-scroll");
  sheet = { print, el, scroller, x: print.x };
  print.slot.classList.add("unfolded");

  // The wheel scrolls the note, not the row (scroll.js listens on the board).
  el.addEventListener("wheel", (e) => { if (!e.ctrlKey) e.stopPropagation(); }, { passive: true });
  el.addEventListener("dblclick", () => fold());
  el.addEventListener("pointerdown", (e) => e.stopPropagation());
  document.addEventListener("pointerdown", onOutside, true);

  const r = { width, height: el.offsetHeight };
  setExtraRect({ x: boardLeft + left, y: pinY - SHEET_PIN_Y - TOP_HIDDEN, w: width, h: r.height });
  if (focus) scroller.focus({ preventScroll: true });

  if (state.reduced) {
    el.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 140, easing: "ease-out" });
    return;
  }
  // Grows out of the note about its pin.
  const sx = Math.min(1, print.w / r.width), sy = Math.min(1, print.h / r.height);
  el.animate([
    { opacity: 0.4, transform: `scale(${sx.toFixed(3)}, ${sy.toFixed(3)})` },
    { opacity: 1, offset: 0.35 },
    { opacity: 1, transform: "none" },
  ], { duration: 260, easing: "cubic-bezier(.2, .8, .25, 1)" });
}

/** Folds the sheet back; `instant` skips animation (tuck, a new sheet). */
export function fold(instant = false) {
  const s = sheet;
  if (!s) return;
  sheet = null;
  document.removeEventListener("pointerdown", onOutside, true);
  s.print.slot.classList.remove("unfolded");
  setExtraRect(null);
  // Give back the focus the sheet held.
  if (s.el.contains(document.activeElement)) s.print.slot.focus({ preventScroll: true });
  if (instant || !state.revealed || state.reduced || s.print.leaving) {
    if (instant || !state.revealed) { s.el.remove(); return; }
    const a = s.el.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 120, fill: "forwards" });
    a.onfinish = () => s.el.remove();
    return;
  }
  const sx = Math.min(1, s.print.w / s.el.offsetWidth), sy = Math.min(1, s.print.h / s.el.offsetHeight);
  s.el.style.pointerEvents = "none";
  const a = s.el.animate([
    { opacity: 1, transform: "none" },
    { opacity: 1, offset: 0.6 },
    { opacity: 0, transform: `scale(${sx.toFixed(3)}, ${sy.toFixed(3)})` },
  ], { duration: 170, easing: "cubic-bezier(.45, 0, .85, .4)", fill: "forwards" });
  a.onfinish = () => s.el.remove();
}

function onOutside(e) {
  if (sheet && !sheet.el.contains(e.target)) fold();
}

// A scrolling or moving row would leave the sheet hanging from nothing.
onScrollState((moving) => { if (moving && sheet) fold(); });
onLayout(() => {
  if (sheet && (sheet.print.leaving || !state.prints.has(sheet.print.id) || sheet.print.x !== sheet.x)) fold();
});

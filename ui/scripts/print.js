// A print on the board: creating its element, pinning it on, keeping it,
// reordering, updating its picture, the copied check, the age caption and
// taking it down. A paper note (text or link) is a print too, with the same
// layers; only its paper differs (note.js).
//
// Layers, outermost first:
//   .slot   placed in the row by layout.js; holds the pin and caption
//   .drop   arrival, keep press, reorder lift and fall animations
//   .swing  pendulum motion around the pin (motion/swing.js)
//   .card   resting tilt, hover lift and press; holds .shade, .paper
//           (.photo > img, or a note's text), .badge and the Keep and x buttons
// Each .slot is an option of the board's listbox, labelled in words
// (announce.js); its layers are hidden from screen readers.

import * as ipc from "./ipc.js";
import { announce, labelFor } from "./announce.js";
import { abortFlight, launch, prepareFlight, restingBox, showTip } from "./flight.js";
import { clearHover, endPress, forgetPendingClick, rehover, wireGestures } from "./gestures.js";
import { itemRemoved } from "./keyboard.js";
import { colorFor, layout, measure, showPrint } from "./layout.js";
import { LAYOUT_MS } from "./motion/spring.js";
import { swing } from "./motion/swing.js";
import { fillNote, fold, isNote, isUnfolded } from "./note.js";
import { playPop, playTock } from "./sound.js";
import { cancel, dom, later, rand, state } from "./state.js";

const MAX_TILT = 1.5;          // degrees, either way
const COPIED_MS = 1000;        // the check stays this long
const KEEP_PRESS_MS = 360;     // the pin's press when kept; the row waits for it

const X_SVG = `<svg viewBox="0 0 18 18" aria-hidden="true"><path d="M6.4 6.4l5.2 5.2M11.6 6.4l-5.2 5.2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" fill="none"/></svg>`;
const KEEP_SVG = `<svg viewBox="0 0 18 18" aria-hidden="true"><path class="fill" d="M6.6 5.1h4.8a.8.8 0 0 1 .8.8v7.2L9 11l-3.2 2.1V5.9a.8.8 0 0 1 .8-.8z" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/></svg>`;
const CHECK_SVG = `<svg viewBox="0 0 28 28" aria-hidden="true"><path d="M9.6 14.4l3 3 5.9-6.6" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" fill="none"/></svg>`;

function createPrint(data) {
  const note = isNote(data);
  const print = {
    id: data.id,
    data: { kind: "image", note: null, ...data },
    addedAt: Date.now(),
    tilt: (Math.random() < 0.5 ? -1 : 1) * rand(0.3, MAX_TILT),
    color: colorFor(data.id),
    settled: false,
    w: 0, h: 0, x: 0, y: 0, t: "",
    sw: null, swAnim: null,
    leaving: false,
    flight: null,     // see flight.js
    badgeTimer: 0,
    captionTimer: 0,
    captionAge: null, // a link note's age span (note.js)
  };

  const slot = document.createElement("div");
  slot.className = "slot placing";
  slot.dataset.id = data.id;
  slot.tabIndex = -1;                       // roving focus (keyboard.js)
  slot.setAttribute("role", "option");
  slot.setAttribute("aria-selected", "false");
  const paper = note ? "" : `<div class="photo"><img alt="" draggable="false" decoding="async"></div>`;
  slot.innerHTML = `
    <div class="drop" aria-hidden="true"><div class="swing"><div class="card">
      <div class="shade"></div>
      <div class="paper">${paper}</div>
      <div class="badge">${CHECK_SVG}</div>
      <button class="btn keep" tabindex="-1" aria-label="Keep">${KEEP_SVG}</button>
      <button class="btn x" tabindex="-1" aria-label="Unpin">${X_SVG}</button>
    </div></div></div>
    <span class="pin" data-c="${print.color}" aria-hidden="true"><span class="pin-head"></span><span class="pin-brass"></span></span>
    <div class="caption" aria-hidden="true"></div>`;
  print.slot = slot;
  print.drop = slot.querySelector(".drop");
  print.swingLayer = slot.querySelector(".swing");
  print.el = slot.querySelector(".card");
  print.shade = slot.querySelector(".shade");
  print.paper = slot.querySelector(".paper");
  print.img = slot.querySelector("img");    // null for a note
  print.badge = slot.querySelector(".badge");
  print.keepButton = slot.querySelector(".keep");
  print.discardButton = slot.querySelector(".x");
  print.pin = slot.querySelector(".pin");
  print.caption = slot.querySelector(".caption");
  print.el.style.setProperty("--tilt", `${print.tilt.toFixed(2)}deg`);
  setKeptClass(print, !!data.kept);
  if (note) {
    fillNote(print);
  } else {
    print.img.addEventListener("load", () => {
      // The backend sent no dimensions: use the decoded size.
      if (!(print.data.width > 0 && print.data.height > 0)) {
        print.data.width = print.img.naturalWidth;
        print.data.height = print.img.naturalHeight;
        measure(print);
        layout();
      }
    });
    print.img.src = data.thumb;
  }
  refreshLabel(print);
  measure(print);
  wireGestures(print);
  return print;
}

export function printById(id) { return state.prints.get(id); }

/**
 * Puts a print on the board. `animate` pins it on (now, or once revealed);
 * with a `flight` it flies in from where it was taken. Initial prints arrive
 * in display order (`append`); a new one goes at the end of the kept group
 * if kept, else first among the recent prints.
 */
export function addPrint(data, animate, append = false, flight = null) {
  if (!data || !data.id) return;
  if (state.prints.has(data.id)) { updatePrint(data); return; }
  const print = createPrint(data);
  if (animate) print.flight = prepareFlight(flight);
  state.prints.set(data.id, print);
  const keptCount = state.order.filter((id) => state.prints.get(id)?.data.kept).length;
  if (append) state.order.push(data.id);
  else state.order.splice(keptCount, 0, data.id);
  dom.prints.appendChild(print.slot);
  const widthBefore = state.boardW;
  layout();
  // Flush so it is placed without sliding in from the origin.
  void print.slot.offsetWidth;
  print.slot.classList.remove("placing");
  if (!animate) return;
  if (!state.revealed) { print.slot.classList.add("awaiting"); state.awaiting.push(print); return; }
  // If the board grows or the row scrolls to make room, pin on once there is cork under it.
  const moved = showPrint(print);
  if (print.flight) { flyIn(print); return; }
  if ((state.boardW > widthBefore || moved) && !state.reduced) {
    print.slot.classList.add("awaiting");
    later(() => { if (print.leaving) return; print.slot.classList.remove("awaiting"); pinOn(print); }, LAYOUT_MS * 0.4);
  } else {
    pinOn(print);
  }
}

/** Takes a print down; `how` is "fall" or "quiet". */
export function removePrint(id, how) {
  const print = printById(id);
  if (!print) return;
  const index = state.order.indexOf(id);
  state.prints.delete(id);
  state.order = state.order.filter((x) => x !== id);
  state.awaiting = state.awaiting.filter((c) => c !== print);
  if (state.hovered === print) clearHover();
  if (state.press?.print === print) endPress();
  abortFlight(print);
  forgetPendingClick(id);
  if (state.draggingId === id) state.draggingId = null;
  if (isUnfolded(print)) fold(true);
  itemRemoved(print, index);
  detach(print, how);
  // Let the print start to go before the row closes the gap.
  if (state.revealed && !state.reduced && how === "fall") later(layout, 120);
  else layout();
}

/** New picture, size, name or keep state for a print already on the board. */
export function updatePrint(data) {
  const print = printById(data.id);
  if (!print) return;
  const before = print.data;
  print.data = { ...before, ...data };
  if (isNote(print.data)) {
    // Text edited externally (e.g. in Notepad).
    if (data.note && JSON.stringify(data.note) !== JSON.stringify(before.note)) {
      if (isUnfolded(print)) fold(true);
      fillNote(print);
      if (state.revealed && !state.reduced) {
        print.paper.animate([{ opacity: 0.4 }, { opacity: 1 }], { duration: 360, easing: "ease-out" });
      }
    }
  } else if (print.img && data.thumb && data.thumb !== before.thumb) {
    print.img.src = data.thumb;
    if (state.revealed && !state.reduced) {
      print.img.animate([{ opacity: 0.3 }, { opacity: 1 }], { duration: 360, easing: "ease-out" });
    }
  }
  if (typeof data.kept === "boolean" && data.kept !== print.slot.classList.contains("kept")) {
    setKept(print, data.kept, state.revealed);
  }
  if (data.width !== before.width || data.height !== before.height) { measure(print); layout(); }
  if (state.hovered === print) showCaption(print);
  refreshLabel(print);
}

/** Flies a new capture in (flight.js). `rest` is its landing box if already measured. Reduced motion just pins on. */
export function flyIn(print, rest = null) {
  const flight = print.flight;
  print.flight = null;
  if (!flight || state.reduced) {
    print.slot.classList.remove("awaiting");
    pinOn(print);
    if (flight?.tip) later(showTip, 500);
    return;
  }
  print.slot.classList.add("awaiting");
  ipc.debugAck("pin-on", { id: print.id });
  launch(print, flight, rest || restingBox(print), {
    pin: () => { playTock(); ipc.debugAck("tock", { id: print.id }); },
    settled: () => swing(print, -Math.sign(print.tilt || 1) * 0.6),
    // Never took off: pins on now, or at the next reveal if tucked meanwhile.
    skipped: () => { if (state.revealed && !print.leaving) { print.slot.classList.remove("awaiting"); pinOn(print); } },
  });
}

/** The arrival: held just above the cork, pressed down, then the pin goes in. */
export function pinOn(print) {
  const slot = print.slot;
  ipc.debugAck("pin-on", { id: print.id });
  const tock = () => { playTock(); ipc.debugAck("tock", { id: print.id }); };
  if (state.reduced) {
    print.drop.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 180, easing: "ease-out" });
    print.pin.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 160, delay: 120, fill: "backwards" });
    later(tock, 140);
    return;
  }
  slot.classList.add("arriving");
  const lean = Math.sign(print.tilt || 1) * rand(0.8, 1.6);
  // Held just above the cork (scale 1.06, wide soft shadow), then pressed down.
  print.drop.animate([
    { offset: 0, opacity: 0, transform: `translateY(-7px) scale(1.06) rotate(${lean}deg)` },
    { offset: 0.2, opacity: 1, transform: `translateY(-6px) scale(1.06) rotate(${lean}deg)`, easing: "cubic-bezier(.45, 0, .25, 1)" },
    { offset: 1, opacity: 1, transform: "none" },
  ], { duration: 330 });
  print.shade.animate([
    { opacity: 1, transform: "translateY(4px) scale(1.03)" },
    { offset: 0.2, opacity: 1, transform: "translateY(4px) scale(1.03)", easing: "cubic-bezier(.45, 0, .25, 1)" },
    { opacity: 0, transform: "none" },
  ], { duration: 330 });
  const pinAt = 250;
  print.pin.animate([
    { opacity: 0, transform: "translateY(-4px) scale(1.7)" },
    { offset: 0.6, opacity: 1, transform: "scale(.9)", easing: "cubic-bezier(.3, 1.4, .5, 1)" },
    { opacity: 1, transform: "none" },
  ], { duration: 170, delay: pinAt, easing: "cubic-bezier(.5, 0, .9, .4)", fill: "backwards" });
  later(tock, pinAt + 95);
  later(() => { slot.classList.remove("arriving"); swing(print, -Math.sign(print.tilt || 1) * 0.6); }, pinAt + 150);
}

function setKeptClass(print, kept) {
  print.slot.classList.toggle("kept", kept);
  print.keepButton.setAttribute("aria-label", kept ? "Stop keeping" : "Keep");
  refreshLabel(print);
}

/** Shows a print as kept or not; `animate` plays the pin's little press. */
function setKept(print, kept, animate) {
  print.data.kept = kept;
  if (kept && print.data.keptAt == null) print.data.keptAt = Date.now();
  if (!kept) print.data.keptAt = null;
  if (state.revealed) announce(kept ? "Kept" : "No longer kept");
  if (!animate || state.reduced || !state.revealed) { setKeptClass(print, kept); return; }
  // The pin turns brass at the bottom of the press and springs back.
  print.pin.animate([
    { transform: "none" },
    { offset: 0.32, transform: "scale(.7)", easing: "cubic-bezier(.2, .9, .3, 1.5)" },
    { offset: 0.7, transform: "scale(1.1)" },
    { transform: "none" },
  ], { duration: KEEP_PRESS_MS, easing: "cubic-bezier(.4, 0, .6, 1)" });
  print.drop.animate([
    { transform: "none" },
    { offset: 0.32, transform: "scale(.985)" },
    { transform: "none" },
  ], { duration: KEEP_PRESS_MS - 40, easing: "ease-out" });
  later(() => setKeptClass(print, print.data.kept), KEEP_PRESS_MS * 0.3);
  holdOrder(KEEP_PRESS_MS);
}

/** Toggles keeping. */
export function toggleKeep(print) {
  if (print.leaving) return;
  const kept = !print.data.kept;
  setKept(print, kept, true);
  ipc.setKept(print.id, kept);
  applyOrder(localOrder());   // the backend confirms with board:order-changed
}

let orderHoldUntil = 0;
let orderTimer = 0;
let pendingOrder = null;

/** Holds reorders back for `ms`, so a keep press is seen before the print moves. */
function holdOrder(ms) { orderHoldUntil = Math.max(orderHoldUntil, performance.now() + ms); }

/** Applies a reorder waiting on a keep press, so tucking leaves no timers behind. */
export function flushOrder() {
  orderHoldUntil = 0;
  if (orderTimer) { cancel(orderTimer); orderTimer = 0; }
  if (pendingOrder) { const o = pendingOrder; pendingOrder = null; applyOrder(o); }
}

/** The display order the backend would give: kept by keptAt, then newest first. */
function localOrder() {
  const idx = new Map(state.order.map((id, i) => [id, i]));
  return state.order.map((id) => state.prints.get(id)).filter(Boolean).sort((a, b) => {
    const ka = a.data.kept ? 0 : 1, kb = b.data.kept ? 0 : 1;
    if (ka !== kb) return ka - kb;
    if (!ka) {
      const d = (a.data.keptAt ?? 0) - (b.data.keptAt ?? 0);
      if (d) return d;
    } else if (a.data.pinnedAt != null && b.data.pinnedAt != null && a.data.pinnedAt !== b.data.pinnedAt) {
      return b.data.pinnedAt - a.data.pinnedAt;
    }
    return idx.get(a.id) - idx.get(b.id);
  }).map((p) => p.id);
}

/** A new row order (board:order-changed, or a local keep). Prints that travel further than a step lift on the way. */
export function applyOrder(ids) {
  const wait = orderHoldUntil - performance.now();
  if (wait > 0 && state.revealed) {
    pendingOrder = ids;
    if (!orderTimer) orderTimer = later(() => { orderTimer = 0; const o = pendingOrder; pendingOrder = null; applyOrder(o); }, wait);
    return;
  }
  const seen = new Set();
  const next = [];
  for (const id of ids || []) if (state.prints.has(id) && !seen.has(id)) { seen.add(id); next.push(id); }
  // A print missing from `ids` is about to be removed (board:print-removed follows): it keeps its place.
  state.order.forEach((id, i) => {
    if (seen.has(id)) return;
    const prev = state.order[i - 1];
    next.splice(prev === undefined ? 0 : next.indexOf(prev) + 1, 0, id);
    seen.add(id);
  });
  if (next.join("\n") === state.order.join("\n")) return;
  const before = new Map(next.map((id) => [id, state.prints.get(id).x]));
  state.order = next;
  layout();
  if (!state.revealed) return;
  // The hovered print may have moved out from under the pointer.
  if (state.hovered && state.hovered.x !== before.get(state.hovered.id)) { clearHover(); later(rehover, LAYOUT_MS); }
  if (state.reduced) return;
  for (const id of next) {
    const p = state.prints.get(id);
    const dx = Math.abs(p.x - before.get(id));
    if (dx <= p.w + 30) continue;
    p.slot.classList.add("moving");
    p.drop.animate([
      { transform: "none" },
      { offset: 0.45, transform: "translateY(-5px) scale(1.03)" },
      { transform: "none" },
    ], { duration: LAYOUT_MS, easing: "ease-in-out" });
    later(() => p.slot.classList.remove("moving"), LAYOUT_MS);
  }
}

/** "fall": the pin pops out and the print drops away. "quiet": a quick fade. */
function detach(print, how) {
  print.leaving = true;
  const slot = print.slot;
  slot.classList.add("leaving");
  slot.classList.remove("hover", "pressing");
  print.swAnim?.cancel();
  cancel(print.badgeTimer);
  cancel(print.captionTimer);
  const done = () => slot.remove();

  if (!state.revealed) { done(); return; }

  if (how !== "fall" || state.reduced) {
    const a = print.drop.animate(
      [{ opacity: 1, transform: "none" }, { opacity: 0, transform: how === "fall" ? "translateY(10px)" : "scale(.96)" }],
      { duration: how === "fall" ? 220 : 170, easing: "ease-in", fill: "forwards" });
    print.pin.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 140, fill: "forwards" });
    a.onfinish = done;
    if (how === "fall") playPop();
    return;
  }

  const dir = Math.sign(print.tilt) || 1;
  print.pin.animate([
    { opacity: 1, transform: "none" },
    { offset: 0.4, opacity: 1, transform: `translate(${dir * 1}px, -5px) scale(1.12)` },
    { opacity: 0, transform: `translate(${dir * 3}px, -9px) scale(1)` },
  ], { duration: 200, easing: "cubic-bezier(.2, .7, .4, 1)", fill: "forwards" });
  playPop();
  const a = print.drop.animate([
    { offset: 0, transform: "none", opacity: 1 },
    { offset: 0.12, transform: `translateY(-2px) rotate(${dir * 0.6}deg)`, opacity: 1, easing: "cubic-bezier(.5, 0, .9, .55)" },
    { offset: 0.5, opacity: 0.85 },
    { offset: 1, transform: `translate(${dir * 6}px, 64px) rotate(${dir * rand(6, 10)}deg)`, opacity: 0 },
  ], { duration: 450, delay: 50, fill: "forwards" });
  a.onfinish = done;
}

/** A check stamps onto the print for a second, and the print sways on its pin. */
export function showCopied(print) {
  cancel(print.badgeTimer);
  print.badge.getAnimations().forEach((a) => a.cancel());
  print.badge.animate([
    { opacity: 0, transform: "scale(1.4)" },
    { offset: 0.6, opacity: 1, transform: "scale(.95)" },
    { opacity: 1, transform: "none" },
  ], { duration: state.reduced ? 1 : 230, easing: "cubic-bezier(.3, .8, .4, 1)", fill: "forwards" });
  print.badgeTimer = later(() => {
    print.badge.animate([{ opacity: 1, transform: "none" }, { opacity: 0, transform: "scale(.92)" }],
      { duration: 200, easing: "ease-in", fill: "forwards" });
  }, COPIED_MS);
  swing(print, Math.sign(print.tilt || 1) * 1.2);
}

const dayFmt = new Intl.DateTimeFormat(undefined, { weekday: "long" });
const dateFmt = new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short" });
const yearFmt = new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short", year: "numeric" });

/** "Just now", "2 min ago", "3 hours ago", "Yesterday", "Tuesday", "12 Sep". */
export function ageText(t, now = Date.now()) {
  const s = Math.max(0, (now - t) / 1000);
  if (s < 45) return "Just now";
  const min = Math.max(1, Math.round(s / 60));
  if (min < 60) return `${min} min ago`;
  const a = new Date(t), b = new Date(now);
  const days = Math.round((new Date(b.getFullYear(), b.getMonth(), b.getDate()) - new Date(a.getFullYear(), a.getMonth(), a.getDate())) / 86400000);
  if (days <= 0) { const h = Math.floor(min / 60); return h === 1 ? "1 hour ago" : `${h} hours ago`; }
  if (days === 1) return "Yesterday";
  if (days < 7) return dayFmt.format(a);
  return (a.getFullYear() === b.getFullYear() ? dateFmt : yearFmt).format(a);
}

/** Fills the hovered print's caption and keeps it current while it shows. */
export function showCaption(print) {
  cancel(print.captionTimer);
  const t = print.data.pinnedAt ?? print.addedAt;
  const text = ageText(t);
  const target = print.captionAge || print.caption;
  if (target.textContent !== text) target.textContent = text;
  refreshLabel(print);   // spoken age
  const age = Date.now() - t;
  const next = age < 3600e3 ? 60e3 - (age % 60e3) + 50 : 5 * 60e3;
  print.captionTimer = later(() => { if (state.hovered === print) showCaption(print); }, next);
}

/** The print's label for screen readers: what it is, its age, kept. */
export function refreshLabel(print) {
  const label = labelFor(print.data, print.data.pinnedAt ?? print.addedAt);
  if (print.slot.getAttribute("aria-label") !== label) print.slot.setAttribute("aria-label", label);
}

export function hideCaption(print) {
  cancel(print.captionTimer);
  print.captionTimer = 0;
}

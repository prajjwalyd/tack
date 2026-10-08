// Tack's board UI: boot and wiring. Plain ES modules, no dependencies, no
// build step. Listens to the backend's events (see ipc.js and docs/ipc.md),
// asks for the prints already pinned, and keeps the webview from behaving
// like a browser.

import * as ipc from "./ipc.js";
import { EVENTS } from "./ipc.js";
import { onDragEnded, onDragging, onPointerLeft } from "./gestures.js";
import { forgetRect, layout, restScroll } from "./layout.js";
import { gust, scheduleGust, stopGusts } from "./motion/swing.js";
import { addPrint, applyOrder, printById, removePrint, showCopied, updatePrint } from "./print.js";
import { reveal, tuck } from "./reveal.js";
import { dom, reducedMotion, state } from "./state.js";

reducedMotion.addEventListener?.("change", (e) => {
  state.reduced = e.matches;
  if (e.matches) stopGusts(); else if (state.revealed) scheduleGust();
});

// ---------------------------------------------------------------- browser hygiene
document.addEventListener("contextmenu", (e) => e.preventDefault());
document.addEventListener("dragstart", (e) => e.preventDefault());
document.addEventListener("selectstart", (e) => e.preventDefault());
document.addEventListener("auxclick", (e) => e.preventDefault());
document.addEventListener("pointerdown", (e) => { if (e.button === 1) e.preventDefault(); });
window.addEventListener("wheel", (e) => { if (e.ctrlKey) e.preventDefault(); }, { passive: false });
document.addEventListener("keydown", (e) => {
  const k = (e.key || "").toLowerCase();
  const ctrl = e.ctrlKey || e.metaKey;
  if (
    k === "f5" || k === "f3" || k === "f7" || k === "browserback" || k === "browserforward" || k === "browserrefresh" ||
    (ctrl && ["r", "p", "f", "g", "u", "s", "o", "n", "j", "h", "+", "-", "=", "0", "a"].includes(k)) ||
    (e.altKey && (k === "arrowleft" || k === "arrowright" || k === "home"))
  ) e.preventDefault();
}, true);

let resizeTimer = 0;
window.addEventListener("resize", () => {
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => {
    resizeTimer = 0;
    // A new monitor or size: jump to the new layout, don't slide.
    forgetRect();
    layout({ instant: true });
    if (!state.revealed) restScroll();
  }, 60);
});

dom.board.addEventListener("pointerenter", () => ipc.setHovering(true));
dom.board.addEventListener("pointerleave", () => ipc.setHovering(false));

// ---------------------------------------------------------------- wiring
async function init() {
  if (!ipc.available()) {
    console.error("[tack] window.__TAURI__ is not available");
    return;
  }
  await Promise.all([
    ipc.on(EVENTS.REVEAL, () => reveal()),
    ipc.on(EVENTS.TUCK, () => tuck()),
    ipc.on(EVENTS.PRINT_ADDED, (p) => addPrint(p.print, !!p.animate)),
    ipc.on(EVENTS.PRINT_REMOVED, (p) => removePrint(p.id, p.how)),
    ipc.on(EVENTS.PRINT_UPDATED, (p) => p.print && updatePrint(p.print)),
    ipc.on(EVENTS.ORDER_CHANGED, (p) => Array.isArray(p.ids) && applyOrder(p.ids)),
    ipc.on(EVENTS.PRINT_COPIED, (p) => { const c = printById(p.id); if (c && !c.leaving) showCopied(c); }),
    ipc.on(EVENTS.PRINT_DRAGGING, (p) => onDragging(printById(p.id), p.id)),
    ipc.on(EVENTS.PRINT_DRAG_ENDED, (p) => onDragEnded(printById(p.id), p.id)),
    ipc.on(EVENTS.POINTER_LEFT, () => onPointerLeft()),
    ipc.on(EVENTS.GUST, () => gust()),
    ipc.on(EVENTS.SETTINGS, (p) => { if (typeof p.sound === "boolean") state.sound = p.sound; }),
  ]);

  const ready = await ipc.boardReady();
  if (ready) {
    if (typeof ready.sound === "boolean") state.sound = ready.sound;
    // Already in row order: append as they come.
    for (const print of ready.prints || []) addPrint(print, false, true);
  }
  // Initial prints go straight to their places.
  layout({ instant: true });
  restScroll();
}

init();

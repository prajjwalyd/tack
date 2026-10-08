// Board boot and wiring. Plain ES modules, no dependencies, no build step.
// Listens to backend events (ipc.js), asks for the prints already
// pinned, and keeps the webview from behaving like a browser. keyboard.js and
// drop.js wire themselves up when imported.

import * as ipc from "./ipc.js";
import { EVENTS } from "./ipc.js";
import { announce, hush } from "./announce.js";
import { clearDrop } from "./drop.js";
import { onDragEnded, onDragging, onPointerLeft } from "./gestures.js";
import { keyboardOpen, keyboardTuck } from "./keyboard.js";
import { forgetRect, layout, restScroll } from "./layout.js";
import { REVEAL_MS } from "./motion/spring.js";
import { gust, scheduleGust, stopGusts } from "./motion/swing.js";
import { fold } from "./note.js";
import { hideNotice, notice, noticeAfterReveal } from "./notice.js";
import { addPrint, applyOrder, printById, removePrint, showCopied, updatePrint } from "./print.js";
import { reveal, tuck, warmUp } from "./reveal.js";
import { dom, reducedMotion, state } from "./state.js";

reducedMotion.addEventListener?.("change", (e) => {
  state.reduced = e.matches;
  if (e.matches) stopGusts(); else if (state.revealed) scheduleGust();
});

// Keep the webview from behaving like a browser.
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
    // New monitor or size: jump, don't slide.
    forgetRect();
    layout({ instant: true });
    if (!state.revealed) restScroll();
  }, 60);
});

dom.board.addEventListener("pointerenter", () => ipc.setHovering(true));
dom.board.addEventListener("pointerleave", () => ipc.setHovering(false));

async function init() {
  if (!ipc.available()) {
    console.error("[tack] window.__TAURI__ is not available");
    return;
  }
  await Promise.all([
    ipc.on(EVENTS.REVEAL, (p) => onReveal(p.reason)),
    ipc.on(EVENTS.TUCK, () => onTuck()),
    ipc.on(EVENTS.WARM_UP, () => warmUp()),
    ipc.on(EVENTS.PRINT_ADDED, (p) => {
      addPrint(p.print, !!p.animate, false, p.flight);
      if (p.animate) announce("Pinned");
    }),
    ipc.on(EVENTS.PRINT_REMOVED, (p) => {
      if (p.how === "fall" && state.revealed && printById(p.id)) announce("Unpinned");
      removePrint(p.id, p.how);
    }),
    ipc.on(EVENTS.PRINT_UPDATED, (p) => p.print && updatePrint(p.print)),
    ipc.on(EVENTS.ORDER_CHANGED, (p) => Array.isArray(p.ids) && applyOrder(p.ids)),
    ipc.on(EVENTS.PRINT_COPIED, (p) => {
      const c = printById(p.id);
      if (c && !c.leaving) { showCopied(c); announce("Copied"); }
    }),
    ipc.on(EVENTS.NOTICE, (p) => notice(p.text)),
    ipc.on(EVENTS.PRINT_DRAGGING, (p) => onDragging(printById(p.id), p.id)),
    ipc.on(EVENTS.PRINT_DRAG_ENDED, (p) => onDragEnded(printById(p.id), p.id)),
    ipc.on(EVENTS.POINTER_LEFT, () => onPointerLeft()),
    ipc.on(EVENTS.GUST, () => gust()),
    ipc.on(EVENTS.SETTINGS, (p) => { if (typeof p.sound === "boolean") state.sound = p.sound; }),
  ]);

  const ready = await ipc.boardReady();
  if (ready) {
    if (typeof ready.sound === "boolean") state.sound = ready.sound;
    // Already in row order.
    for (const print of ready.prints || []) addPrint(print, false, true);
  }
  layout({ instant: true });
  restScroll();
}

/** board:reveal. The hotkey is a keyboard open: the page takes the focus (keyboard.js). */
function onReveal(reason) {
  const was = state.revealed;
  reveal();
  if (reason === "hotkey") keyboardOpen(was ? 0 : (state.reduced ? 100 : REVEAL_MS));
  noticeAfterReveal(was);
}

/** board:tuck: everything transient goes with the board. */
function onTuck() {
  keyboardTuck();
  fold(true);
  hideNotice();
  clearDrop();
  hush();
  tuck();
}

init();

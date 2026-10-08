// The board sliding down (reveal) and back up (tuck). The backend shows the
// window before a reveal and hides it after the tuck. While tucked nothing
// runs: no animations, no timers, no rAF, the audio context suspended, and
// the board is display:none so its layers are released. Of the prints that
// arrived meanwhile, the newest is pinned on as the board comes down.

import { debugAck } from "./ipc.js";
import { clearHover, endPress } from "./gestures.js";
import { forgetRect, restScroll, sendRect, showPrint, stopRectTimers } from "./layout.js";
import { REVEAL_MS } from "./motion/spring.js";
import { scheduleGust, stopGusts } from "./motion/swing.js";
import { flushOrder, pinOn } from "./print.js";
import { halt } from "./scroll.js";
import { sleepSound, wakeSound } from "./sound.js";
import { dom, later, state } from "./state.js";

const TUCK_MS = 200;        // --dur-tuck

let tuckTimer = 0;

export function reveal() {
  clearTimeout(tuckTimer);
  tuckTimer = 0;
  const wasRevealed = state.revealed;
  state.revealed = true;
  if (!wasRevealed) {
    wakeSound();
    dom.board.classList.remove("gone");
    void dom.board.offsetWidth;   // one style flush, so the slide starts from tucked
    dom.board.classList.remove("tucked");
    scheduleGust();
  }
  forgetRect();
  sendRect();
  debugAck("reveal", { queued: state.awaiting.length });
  if (state.awaiting.length) {
    // Prints can pile up while the board is away: a new screenshot that did
    // not bring it down (something was full screen), or events that waited
    // in a sleeping page. Only the newest is pinned on, with its sound; the
    // rest are simply there when the board comes down, never a salvo.
    const queue = state.awaiting.splice(0).filter((p) => !p.leaving);
    const newest = queue.reduce((a, b) => (arrivedAt(b) > arrivedAt(a) ? b : a), queue[0]);
    for (const print of queue) if (print !== newest) print.slot.classList.remove("awaiting");
    if (newest) {
      showPrint(newest);
      const start = wasRevealed ? 0 : (state.reduced ? 100 : REVEAL_MS * 0.5);
      later(() => {
        if (newest.leaving) return;
        newest.slot.classList.remove("awaiting");
        pinOn(newest);
      }, start);
    }
  }
}

/** When a print was pinned, for picking the newest of a backlog. */
function arrivedAt(print) {
  return print.data.pinnedAt || print.addedAt || 0;
}

export function tuck() {
  if (!state.revealed && dom.board.classList.contains("tucked")) return;
  state.revealed = false;
  stopGusts();
  flushOrder();
  for (const t of state.timers) clearTimeout(t);
  state.timers.clear();
  // Prints whose pin-on was still scheduled pin on at the next reveal instead.
  for (const id of state.order) {
    const p = state.prints.get(id);
    if (p && p.slot.classList.contains("awaiting") && !state.awaiting.includes(p)) state.awaiting.push(p);
  }
  endPress();
  clearHover();
  halt();
  dom.board.classList.add("tucked");
  clearTimeout(tuckTimer);
  tuckTimer = setTimeout(() => {
    tuckTimer = 0;
    if (state.revealed) return;
    // Nothing keeps running while tucked.
    for (const c of state.prints.values()) {
      c.sw = null; c.swAnim = null;
      c.slot.classList.remove("arriving", "moving");
    }
    for (const a of dom.board.getAnimations({ subtree: true })) a.cancel();
    for (const s of dom.prints.querySelectorAll(".slot.leaving")) s.remove();
    // Prints still waiting to pin on stay hidden until the next reveal.
    restScroll();
    stopRectTimers();
    sleepSound();
    dom.board.classList.add("gone");
  }, TUCK_MS + 30);
}

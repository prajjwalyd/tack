// The board sliding down (reveal) and back up (tuck). The backend shows the
// window before a reveal and hides it after the tuck. While tucked nothing
// runs: no animations, no timers, no rAF, the audio context suspended, and
// the board is display:none so its layers are released. Of the prints that
// arrived meanwhile, the newest is pinned on as the board comes down, or
// flies in if it is a new capture (flight.js).

import { debugAck } from "./ipc.js";
import { abortFlights, dropTip, restingBox } from "./flight.js";
import { clearHover, endPress } from "./gestures.js";
import { forgetRect, restScroll, sendRect, showPrint, stopRectTimers } from "./layout.js";
import { REVEAL_MS } from "./motion/spring.js";
import { scheduleGust, stopGusts } from "./motion/swing.js";
import { flushOrder, flyIn, pinOn } from "./print.js";
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
  if (endWarmUp) {
    endWarmUp(true);
    dom.board.classList.add("tucked");
    void dom.board.offsetWidth;
  }
  // Prints can pile up while the board is away: a new screenshot that did
  // not bring it down (something was full screen), or events that waited
  // in a sleeping page. Only the newest is pinned on, with its sound; the
  // rest are simply there when the board comes down, never a salvo.
  const queued = state.awaiting.length;
  const queue = state.awaiting.splice(0).filter((p) => !p.leaving);
  const newest = queue.reduce((a, b) => (arrivedAt(b) > arrivedAt(a) ? b : a), queue[0]);
  for (const print of queue) {
    if (print === newest) continue;
    print.flight = null;
    print.slot.classList.remove("awaiting");
  }
  let rest = null;
  if (!wasRevealed) {
    wakeSound();
    dom.board.classList.remove("gone");
    if (newest) showPrint(newest);
    // A flight aims at where its print will rest once the board is down:
    // measured now, before the swing starts.
    if (newest?.flight && !state.reduced) rest = restingBox(newest);
    void dom.board.offsetWidth;   // one style flush, so the slide starts from tucked
    dom.board.classList.remove("tucked");
    scheduleGust();
  } else if (newest) {
    showPrint(newest);
  }
  forgetRect();
  sendRect();
  debugAck("reveal", { queued });
  if (!newest) return;
  // A new capture flies in at once, alongside the board's swing.
  if (newest.flight) { flyIn(newest, rest); return; }
  const start = wasRevealed ? 0 : (state.reduced ? 100 : REVEAL_MS);
  later(() => {
    if (newest.leaving) return;
    newest.slot.classList.remove("awaiting");
    pinOn(newest);
  }, start);
}

/**
 * Once at startup (board:warm-up), while the backend shows the hidden window
 * for a moment: draws the board once, at 1% opacity so nothing is seen,
 * part-way through its swing and then flat, with a stand-in like a flight's.
 * The first drawing of all that costs the renderer about a second in which
 * no frame reaches the screen; paid now, the first reveal and the first
 * flight are seen from their first frame.
 */
let endWarmUp = null;

export function warmUp() {
  if (state.revealed || endWarmUp) return;
  const b = dom.board;
  const timers = [];
  // Undone at the end, or at once by a reveal that comes meanwhile.
  endWarmUp = (revealing) => {
    endWarmUp = null;
    timers.forEach(clearTimeout);
    stand.remove();
    b.style.transition = b.style.transform = b.style.opacity = "";
    if (!revealing) b.classList.add("tucked", "gone");
  };
  b.style.transition = "none";
  b.style.opacity = "0.01";
  b.style.transform = "perspective(var(--board-perspective)) rotateX(-40deg)";
  b.classList.remove("gone", "tucked");
  const stand = document.createElement("div");
  stand.className = "flight";
  Object.assign(stand.style, { left: "40px", top: "200px", width: "168px", height: "100px", opacity: "0.01" });
  stand.innerHTML = `<div class="flight-shade" style="opacity:1"></div><div class="flight-paper" style="opacity:1"></div><div class="flight-photo"></div>`;
  document.body.appendChild(stand);
  stand.animate([{ transform: "scale(3)" }, { transform: "rotate(2deg)" }], { duration: 400 });
  timers.push(setTimeout(() => { b.style.transform = "none"; }, 250));
  timers.push(setTimeout(() => endWarmUp?.(false), 600));
}

/** When a print was pinned, for picking the newest of a backlog. */
function arrivedAt(print) {
  return print.data.pinnedAt || print.addedAt || 0;
}

export function tuck() {
  if (!state.revealed && dom.board.classList.contains("tucked")) return;
  state.revealed = false;
  stopGusts();
  abortFlights();
  dropTip();
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

// The board's shared UI state, the page elements every module works on, and
// a few small helpers. One plain object, mutated in place.

export const dom = {
  board: document.getElementById("board"),
  cork: document.querySelector("#board .cork"),
  rail: document.getElementById("rail"),
  prints: document.getElementById("prints"),
  hint: document.getElementById("hint"),
};

export const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

export const state = {
  revealed: false,
  sound: true,
  prints: new Map(),    // id -> print (see print.js)
  order: [],            // ids in display order: kept first, then newest first
  hovered: null,        // print under the pointer (gestures.js)
  press: null,          // press in progress (gestures.js)
  pendingClick: null,   // click waiting to see if it becomes a double click
  draggingId: null,
  keyMenuAt: -1e9,      // performance.now() of the last keyboard context-menu request
  gustTimer: 0,
  timers: new Set(),    // short-lived animation timeouts, cleared on tuck
  awaiting: [],         // prints to pin on once the board is revealed
  boardW: 0,            // current board width, CSS px
  reduced: reducedMotion.matches,
};

export const rand = (a, b) => a + Math.random() * (b - a);

/** setTimeout that tuck() cancels: for animation steps only. */
export function later(fn, ms) {
  const t = setTimeout(() => { state.timers.delete(t); fn(); }, ms);
  state.timers.add(t);
  return t;
}

/** Cancels a `later` timer. */
export function cancel(t) {
  if (!t) return;
  clearTimeout(t);
  state.timers.delete(t);
}

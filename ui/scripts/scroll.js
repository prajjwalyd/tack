// Scrolling the row: the track (#prints) is translated sideways, nothing is
// laid out. A mouse wheel's coarse steps glide to their target (exponential
// approach, so quick flicks accumulate into one run); a touchpad's fine deltas
// are followed almost directly. Vertical wheel turns scroll sideways. The rAF
// loop runs only while the row moves; edge fades show where more prints wait.

import { state, dom } from "./state.js";

const WHEEL_TAU = 95;      // ms: how quickly a wheel glide catches up with its target
const PAD_TAU = 22;        // ms: a touchpad brings its own momentum
const GLIDE_TAU = 110;     // ms: scrolls the board makes itself (new print, shorter row)
const LINE_PX = 40;        // deltaMode 1 (lines) to px
const SETTLED = 0.25;      // px: close enough, snap and stop

let pos = 0;               // shown
let target = 0;            // heading to
let max = 0;               // furthest scroll, 0 when the row fits
let tau = WHEEL_TAU;
let raf = 0;
let lastT = 0;
let edges = "";
const listeners = new Set();

/** Called with (moving: boolean) when a scroll starts and once it settles. */
export function onScrollState(fn) { listeners.add(fn); }

export function scrollPos() { return pos; }
export function isScrolling() { return raf !== 0; }

/** The row's full width and the visible width changed (layout.js). */
export function setExtent(contentW, viewW) {
  max = Math.max(0, Math.round(contentW - viewW));
  const scrolls = max > 0;
  if (dom.rail.classList.contains("scrolls") !== scrolls) dom.rail.classList.toggle("scrolls", scrolls);
  if (target > max || pos > max) {
    // Shorter row while scrolled to its end: glide back, never jump.
    target = Math.min(target, max);
    if (state.revealed && !state.reduced) { tau = GLIDE_TAU; start(); }
    else { stop(); pos = Math.min(pos, max); apply(); }
  }
  updateEdges();
}

/** Scrolls to `x` (clamped), gliding unless `instant`. */
export function scrollTo(x, instant = false) {
  target = clamp(x);
  if (instant || state.reduced || !state.revealed) { stop(); pos = target; apply(); updateEdges(); return; }
  tau = GLIDE_TAU;
  start();
}

/** Brings the span [x0, x1] (track coordinates relative to the view's left edge at scroll 0) into view; true if it scrolls. */
export function reveal(x0, x1, viewW, margin = 24) {
  const before = target;
  if (x0 - margin < target) scrollTo(x0 - margin);
  else if (x1 + margin > target + viewW) scrollTo(x1 + margin - viewW);
  return Math.abs(target - before) > 1;
}

/** Stops dead where it is (tuck). */
export function halt() {
  stop();
  target = pos;
}

function clamp(x) { return Math.max(0, Math.min(max, x)); }

function apply() {
  dom.prints.style.transform = pos ? `translate3d(${-pos}px, 0, 0)` : "";
}

function updateEdges() {
  const k = max > 0 ? `${pos > 0.5 ? "l" : ""}${pos < max - 0.5 ? "r" : ""}` : "";
  if (k === edges) return;
  edges = k;
  dom.rail.classList.toggle("more-l", k.includes("l"));
  dom.rail.classList.toggle("more-r", k.includes("r"));
}

function start() {
  if (raf) return;
  lastT = performance.now();
  raf = requestAnimationFrame(step);
  for (const fn of listeners) fn(true);
}

function stop() {
  if (!raf) return;
  cancelAnimationFrame(raf);
  raf = 0;
  for (const fn of listeners) fn(false);
}

function step(now) {
  const dt = Math.min(48, now - lastT);
  lastT = now;
  const k = 1 - Math.exp(-dt / tau);
  pos += (target - pos) * k;
  if (Math.abs(target - pos) < SETTLED) pos = target;
  apply();
  updateEdges();
  if (pos === target) { raf = 0; for (const fn of listeners) fn(false); return; }
  raf = requestAnimationFrame(step);
}

function onWheel(e) {
  if (e.ctrlKey) { e.preventDefault(); return; }       // no page zoom
  if (!state.revealed || max <= 0) return;
  e.preventDefault();
  const unit = e.deltaMode === 1 ? LINE_PX : e.deltaMode === 2 ? dom.rail.clientWidth : 1;
  const dx = e.deltaX * unit, dy = e.deltaY * unit;
  const d = Math.abs(dx) > Math.abs(dy) ? dx : dy;
  if (!d) return;
  // Wheels step in large round notches (100/120 px); touchpads send many small, fractional deltas.
  const notch = e.deltaMode !== 0 || (Math.abs(d) >= 50 && Number.isInteger(d) && !dx);
  tau = state.reduced ? 1 : notch ? WHEEL_TAU : PAD_TAU;
  // A notch while gliding stacks onto the target, not the current spot.
  target = clamp(target + d);
  start();
}

dom.board.addEventListener("wheel", onWheel, { passive: false });

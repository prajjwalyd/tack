// Pendulum motion for prints on their pins. One damped oscillator drives
// every swing: the nudge after a copy, the settle after pin-on, and the
// breeze, a rare, faint ripple along the row while the board is out.

import { inView } from "../layout.js";
import { later, rand, state } from "../state.js";

const SW_W = (2 * Math.PI) / 1.3;   // ~1.3 s period: paper on a pin, not a pendulum clock
const SW_A = 1.7;                   // decay rate (1/s)

const BREEZE_MIN_MS = 12000;        // design.md: every 12-25 s while revealed
const BREEZE_MAX_MS = 25000;
const BREEZE_MAX_DEG = 0.8;
const RIPPLE_MS = 55;               // delay from one print to the next

function swingAngle(sw, now) {
  const t = (now - sw.t0) / 1000;
  if (t >= sw.dur) return { th: 0, v: 0 };
  const e = Math.exp(-SW_A * t), c = Math.cos(SW_W * t), s = Math.sin(SW_W * t);
  return {
    th: e * (sw.c1 * c + sw.c2 * s),
    v: e * ((-SW_A * sw.c1 + SW_W * sw.c2) * c + (-SW_A * sw.c2 - SW_W * sw.c1) * s),
  };
}

/**
 * Gives a print a push that peaks at roughly `amp` degrees. Pushes add to the
 * motion the print already has, so overlapping nudges blend instead of jumping.
 */
export function swing(print, amp) {
  if (state.reduced || print.leaving || !state.revealed) return;
  const now = performance.now();
  let th = 0, v = 0;
  if (print.sw) ({ th, v } = swingAngle(print.sw, now));
  v += amp * SW_W * 1.25;
  const c1 = th, c2 = (v + SW_A * th) / SW_W;
  const peak = Math.max(Math.abs(c1), Math.abs(c2), 0.01);
  const dur = Math.min(3.5, Math.max(0.6, Math.log(peak / 0.03) / SW_A));
  const sw = { t0: now, c1, c2, dur };
  const n = Math.ceil(dur * 30);
  const frames = [];
  for (let i = 0; i <= n; i++) {
    const deg = i === n ? 0 : swingAngle(sw, now + (i / n) * dur * 1000).th;
    frames.push({ transform: `rotate(${deg.toFixed(3)}deg)` });
  }
  print.swAnim?.cancel();
  print.sw = sw;
  print.swAnim = print.swingLayer.animate(frames, { duration: dur * 1000, easing: "linear" });
  print.swAnim.onfinish = () => { if (print.sw === sw) { print.sw = null; print.swAnim = null; } };
}

// ---------------------------------------------------------------- the breeze

/** Plans the next breeze while the board is revealed. */
export function scheduleGust() {
  clearTimeout(state.gustTimer);
  state.gustTimer = 0;
  if (!state.revealed || state.reduced) return;
  state.gustTimer = setTimeout(() => { gust(); scheduleGust(); }, rand(BREEZE_MIN_MS, BREEZE_MAX_MS));
}

export function stopGusts() { clearTimeout(state.gustTimer); state.gustTimer = 0; }

/** One breeze: a faint push that ripples along the visible prints. */
export function gust() {
  if (!state.revealed || state.reduced || state.press || state.draggingId) return;
  const live = state.order
    .map((id) => state.prints.get(id))
    .filter((c) => c && !c.leaving && !c.slot.classList.contains("awaiting") && inView(c));
  if (!live.length) return;
  const dir = Math.random() < 0.5 ? -1 : 1;
  const seq = dir > 0 ? live : [...live].reverse();
  const strength = rand(0.65, 1);
  seq.forEach((c, i) => {
    later(() => {
      if (state.press || state.draggingId || c === state.hovered) return;
      swing(c, dir * BREEZE_MAX_DEG * strength * rand(0.75, 1));
    }, i * RIPPLE_MS + rand(0, 30));
  });
}

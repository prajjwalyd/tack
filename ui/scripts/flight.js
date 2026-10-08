// Capture flight: a new snip lifts off the screen where it was taken and
// flies into its slot on the board, plus the one-time tip under the board.
// Used by print.js. Only transform and opacity, precomputed and run through
// WAAPI, so a busy main thread can delay the tock but never leave the print
// hanging in the air.
//
//   lift    ~110 ms  rises in place, shadow grows
//   travel  ~560 ms  arcs to the slot, shrinking and turning to its angle
//   press   ~220 ms  pressed onto the cork; the pin drives in at the touch

import * as ipc from "./ipc.js";
import { PIN_Y } from "./layout.js";
import { cancel, dom, later, rand, state } from "./state.js";

const LIFT_MS = 110;
const TRAVEL_MS = 560;
const PRESS_MS = 220;
const FLIGHT_MS = LIFT_MS + TRAVEL_MS + PRESS_MS;
const PIN_MS = 170;           // pin drop, as in print.js's pin-on
const PIN_TOUCH = 100;        // ms into it the head meets the paper
const LIFT_SWELL = 1.025;
const HELD_Y = -6;            // print.js pin-on's held pose
const HELD_SCALE = 1.06;
const BORDER = 4;             // --print-border
const FRAMES = 72;            // keyframes per flight (linear between them)
const DECODE_WAIT = 120;      // ms to wait for the picture before using the thumbnail
const TIP_DELAY = 380;        // ms after landing

/** Flights in progress, by print id. */
const flying = new Map();

/** The backend's `flight` for a new print with its picture loading, or null. */
export function prepareFlight(flight) {
  const f = flight?.from;
  if (!f || !(f.w > 0 && f.h > 0) || !flight.image) return null;
  const img = new Image();
  img.decoding = "async";
  img.alt = "";
  img.draggable = false;
  img.src = flight.image;
  const ready = img.decode().then(() => true, () => false);
  return { from: { x: +f.x, y: +f.y, w: +f.w, h: +f.h }, found: !!flight.found, tip: !!flight.tip, img, ready };
}

/**
 * The slot's box in viewport px. The board may be folded up, so it is held
 * flat for the read (never painted). Mid-swing it is read as is, since
 * flattening would cancel the swing.
 */
export function restingBox(print) {
  const b = dom.board;
  const swinging = b.getAnimations().length > 0;
  if (!swinging) {
    b.style.transition = "none";
    b.classList.add("measuring");
  }
  const r = print.slot.getBoundingClientRect();
  if (!swinging) {
    b.classList.remove("measuring");
    void b.offsetWidth;        // back as it was, with no transition started
    b.style.transition = "";
  }
  return { left: r.left, top: r.top, w: r.width, h: r.height };
}

/**
 * Flies `print` in to `rest` (restingBox). Calls back `land.pin()` as the
 * pin drives in, `land.settled()` at the end, `land.skipped()` if it never
 * took off.
 */
export async function launch(print, flight, rest, land) {
  // Wait briefly for the sharp picture so the first frame is not a thumbnail.
  const decoded = await Promise.race([flight.ready, new Promise((r) => setTimeout(() => r(false), DECODE_WAIT))]);
  if (print.leaving || !state.revealed) { releaseImage(flight.img); land.skipped(); return; }
  if (!decoded) { flight.img.src = print.data.thumb; }

  const lean = Math.sign(print.tilt || 1) * rand(0.8, 1.6);
  const frames = keyframes(flight.from, rest, print.tilt || 0, lean, flight.found);
  // Hidden on the frame the print takes over; the last frame is its resting pose.
  const last = frames[frames.length - 1];
  frames.splice(frames.length - 1, 0, { ...last, offset: 1 - 0.5 / FLIGHT_MS, opacity: 1 });
  last.opacity = 0;

  const el = document.createElement("div");
  el.className = "flight";
  Object.assign(el.style, {
    left: `${rest.left}px`, top: `${rest.top}px`, width: `${rest.w}px`, height: `${rest.h}px`,
    transform: frames[0].transform, opacity: frames[0].opacity,
  });
  el.innerHTML = `<div class="flight-shade"></div><div class="flight-paper"></div><div class="flight-photo"></div>`;
  el.querySelector(".flight-photo").appendChild(flight.img);
  // The stand-in carries a pin copy; the print's own pin carries on from the hand-over.
  const pin = print.pin.cloneNode(true);
  el.appendChild(pin);
  document.body.appendChild(el);

  const at = (ms) => ms / FLIGHT_MS;
  const pressAt = LIFT_MS + TRAVEL_MS;
  const pinAt = FLIGHT_MS - PIN_TOUCH;
  // Started in one task so they share a start time on the compositor.
  const anims = [
    el.animate(frames, { duration: FLIGHT_MS, easing: "linear", fill: "forwards" }),
    el.querySelector(".flight-shade").animate([
      { opacity: 0 },
      { offset: at(LIFT_MS), opacity: 0.75, easing: "ease-in-out" },
      { offset: at(pressAt), opacity: 1, easing: "cubic-bezier(.5, 0, .3, 1)" },
      { opacity: 0 },
    ], { duration: FLIGHT_MS, fill: "forwards" }),
    el.querySelector(".flight-paper").animate([
      { opacity: 0 },
      { offset: at(LIFT_MS + TRAVEL_MS * 0.12), opacity: 0, easing: "ease-in-out" },
      { offset: at(LIFT_MS + TRAVEL_MS * 0.6), opacity: 1 },
      { opacity: 1 },
    ], { duration: FLIGHT_MS, fill: "forwards" }),
    // The print waits unseen and takes over as the stand-in touches the cork.
    print.drop.animate([{ opacity: 0 }, { opacity: 0 }], { duration: FLIGHT_MS }),
    ...[pin, print.pin].map((p) => p.animate(PIN_DROP, {
      duration: PIN_MS, delay: pinAt, easing: "cubic-bezier(.5, 0, .9, .4)", fill: "backwards",
    })),
  ];
  print.slot.classList.remove("awaiting");
  const timers = [];
  flying.set(print.id, { el, anims, timers, img: flight.img });
  ipc.debugAck("flight", { id: print.id });

  // These only sound or settle, so a late timer shows nothing wrong.
  const step = (fn, ms) => timers.push(later(fn, ms));
  step(() => print.slot.classList.add("arriving", "landing"), pinAt);
  step(() => land.pin(), pinAt + 95);
  step(() => {
    flying.delete(print.id);
    el.remove();
    releaseImage(flight.img);
    print.slot.classList.remove("arriving", "landing");
    land.settled();
    if (flight.tip && !print.leaving) later(showTip, TIP_DELAY);
  }, FLIGHT_MS + 60);
}

const PIN_DROP = [
  { opacity: 0, transform: "translateY(-4px) scale(1.7)" },
  { offset: 0.6, opacity: 1, transform: "scale(.9)", easing: "cubic-bezier(.3, 1.4, .5, 1)" },
  { opacity: 1, transform: "none" },
];

/** Ends a flight at once. */
export function abortFlight(print) {
  const f = flying.get(print.id);
  if (!f) return;
  flying.delete(print.id);
  for (const a of f.anims) a.cancel();
  for (const t of f.timers) cancel(t);
  f.el.remove();
  releaseImage(f.img);
  print.slot?.classList.remove("arriving", "landing");
}

/** Drops the picture's source so the decoded image (up to 1600 px wide) is freed now, not at the next GC. */
function releaseImage(img) {
  if (!img) return;
  img.remove();
  img.removeAttribute("src");
}

/** Ends every flight at once. */
export function abortFlights() {
  for (const id of [...flying.keys()]) abortFlight(state.prints.get(id) || { id });
}

/**
 * Keyframes from the snip `from` to the held pose over `rest`, then pressed
 * down to the resting pose. The stand-in is laid out at `rest` and turns and
 * scales about the pin point O, so its transform is translate(t) rotate(r)
 * scale(sx, sy) with t chosen to put the photo's centre on the flight path.
 * At the start the photo (inset by the border) covers `from` exactly.
 */
function keyframes(from, rest, tilt, lean, found) {
  const O = { x: rest.left + rest.w / 2, y: rest.top + PIN_Y };
  const pw = Math.max(1, rest.w - 2 * BORDER), ph = Math.max(1, rest.h - 2 * BORDER);
  const d = rest.h / 2 - PIN_Y;            // photo centre below the pin, unscaled
  const sx0 = from.w / pw, sy0 = from.h / ph;
  const r1 = tilt + lean;

  const C0 = { x: from.x + from.w / 2, y: from.y + from.h / 2 };
  const end = turn(r1, 0, HELD_SCALE * d);
  const C1 = { x: O.x + end.x, y: O.y + HELD_Y + end.y };
  const P1 = control(C0, C1);
  const restC = turn(tilt, 0, d);
  const C2 = { x: O.x + restC.x, y: O.y + restC.y };

  const frames = [];
  for (let i = 0; i <= FRAMES; i++) {
    const ms = (i / FRAMES) * FLIGHT_MS;
    let c, sx, sy, r, opacity = 1;
    if (ms > LIFT_MS + TRAVEL_MS) {
      const k = press((ms - LIFT_MS - TRAVEL_MS) / PRESS_MS);
      c = { x: C1.x + (C2.x - C1.x) * k, y: C1.y + (C2.y - C1.y) * k };
      sx = sy = HELD_SCALE + (1 - HELD_SCALE) * k;
      r = r1 + (tilt - r1) * k;
    } else if (ms <= LIFT_MS) {
      const k = easeOut(ms / LIFT_MS);
      const swell = 1 + (LIFT_SWELL - 1) * k;
      c = C0; sx = sx0 * swell; sy = sy0 * swell; r = 0;
    } else {
      const u = travel((ms - LIFT_MS) / TRAVEL_MS);
      c = bezier(C0, P1, C1, u);
      // Shrinks slightly ahead of the travel.
      const us = Math.min(1, u * 1.08);
      sx = geo(sx0 * LIFT_SWELL, HELD_SCALE, us);
      sy = geo(sy0 * LIFT_SWELL, HELD_SCALE, us);
      r = r1 * smooth(0.25, 1, u);
    }
    if (!found) {
      // Not found on screen: grows out of the pointer instead.
      const k = easeOut(Math.min(1, ms / 220));
      opacity = k;
      const grow = 0.55 + 0.45 * k;
      sx *= grow; sy *= grow;
    }
    const v = turn(r, 0, sy * d);
    const tx = c.x - O.x - v.x, ty = c.y - O.y - v.y;
    frames.push({
      offset: i / FRAMES,
      transform: `translate(${tx.toFixed(2)}px, ${ty.toFixed(2)}px) rotate(${r.toFixed(3)}deg) scale(${sx.toFixed(4)}, ${sy.toFixed(4)})`,
      opacity,
    });
  }
  return frames;
}

/** Arc control point: most of the way across early, so the print rises into its slot from below; bows sideways when straight below. */
function control(a, b) {
  const dx = b.x - a.x, dy = b.y - a.y;
  const p = { x: a.x + dx * 0.7, y: a.y + dy * 0.2 };
  const len = Math.hypot(dx, dy) || 1;
  const bow = Math.max(0, 0.14 - Math.abs(dx) / len * 0.3) * len;
  if (bow > 0) p.x += (a.x < window.innerWidth / 2 ? -1 : 1) * bow;
  return p;
}

function bezier(a, p, b, u) {
  const v = 1 - u;
  return { x: v * v * a.x + 2 * v * u * p.x + u * u * b.x, y: v * v * a.y + 2 * v * u * p.y + u * u * b.y };
}

function turn(deg, x, y) {
  const a = (deg * Math.PI) / 180, c = Math.cos(a), s = Math.sin(a);
  return { x: x * c - y * s, y: x * s + y * c };
}

const geo = (a, b, u) => a * Math.pow(b / a, u);
const easeOut = (t) => 1 - Math.pow(1 - t, 3);
function smooth(e0, e1, x) {
  const t = Math.min(1, Math.max(0, (x - e0) / (e1 - e0)));
  return t * t * (3 - 2 * t);
}

const travel = cubicBezier(0.5, 0, 0.25, 1);
const press = cubicBezier(0.45, 0, 0.25, 1);

function cubicBezier(x1, y1, x2, y2) {
  const bx = (t) => 3 * x1 * t * (1 - t) ** 2 + 3 * x2 * t * t * (1 - t) + t ** 3;
  const by = (t) => 3 * y1 * t * (1 - t) ** 2 + 3 * y2 * t * t * (1 - t) + t ** 3;
  return (x) => {
    if (x <= 0) return 0;
    if (x >= 1) return 1;
    let lo = 0, hi = 1, t = x;
    for (let i = 0; i < 24; i++) {
      t = (lo + hi) / 2;
      if (bx(t) < x) lo = t; else hi = t;
    }
    return by(t);
  };
}

const CLOSE_SVG = `<svg viewBox="0 0 18 18" aria-hidden="true"><path d="M6.4 6.4l5.2 5.2M11.6 6.4l-5.2 5.2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" fill="none"/></svg>`;

let tip = null;

/** Pins the one-time tip under the board and tells the backend where it is, so it takes clicks and the board stays down. */
export function showTip() {
  if (tip || !state.revealed) return;
  const el = document.createElement("div");
  el.className = "tip";
  el.setAttribute("role", "note");
  el.innerHTML = `<span class="tip-pin"></span>
    <p>Tack shows your snips now. You can turn off Snipping Tool’s notifications in <strong>Settings › System › Notifications</strong>.</p>
    <button class="tip-close" tabindex="-1" aria-label="Close">${CLOSE_SVG}</button>`;
  el.querySelector(".tip-close").addEventListener("click", closeTip);
  dom.board.appendChild(el);
  tip = el;
  // Measured at rest, before the entrance animation moves it.
  const r = el.getBoundingClientRect();
  const dpr = window.devicePixelRatio || 1;
  ipc.setTip({
    x: Math.round(r.left * dpr), y: Math.round(r.top * dpr),
    w: Math.round(r.width * dpr), h: Math.round(r.height * dpr),
  });
  if (state.reduced) {
    el.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 200, easing: "ease-out" });
    return;
  }
  el.animate([
    { opacity: 0, transform: "translateY(-10px) rotate(-2.4deg)" },
    { offset: 0.45, opacity: 1, transform: "translateY(1px) rotate(.4deg)" },
    { offset: 0.75, transform: "translateY(0) rotate(-.9deg)" },
    { opacity: 1, transform: "rotate(-.6deg)" },
  ], { duration: 520, easing: "cubic-bezier(.3, .7, .4, 1)" });
}

/** The tip's close button. */
function closeTip() {
  const el = tip;
  if (!el) return;
  tip = null;
  ipc.setTip(null);
  const a = el.animate([
    { opacity: 1, transform: "rotate(-.6deg)" },
    { opacity: 0, transform: "translateY(-6px) rotate(-1.2deg) scale(.98)" },
  ], { duration: state.reduced ? 120 : 200, easing: "ease-in", fill: "forwards" });
  a.onfinish = () => el.remove();
}

/** Removes the tip at once. */
export function dropTip() {
  if (!tip) return;
  tip.remove();
  tip = null;
}

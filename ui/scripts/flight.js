// The capture flight: a new snip lifts off the screen exactly where it was
// taken and flies up into its place on the board. Then the one-time tip
// that may hang under the board after the first one.
//
// The backend sends where the snip was (`flight.from`, CSS px relative to
// this window, which covers the monitor's work area) and a sharp picture of
// it with `board:print-added` (docs/ipc.md). The print's slot stays hidden
// while a stand-in, `.flight` (styles/flight.css), is laid out exactly
// where the print will rest and transformed, about the pin point, from the
// snip's rectangle to the print's "held" pose (just above the cork, scaled
// 1.06 and leaning, where print.js's pin-on starts), and then pressed onto
// the cork. As it touches, the print takes its place and the pin drives in.
//
//   lift    ~110 ms  the snip rises off the screen in place: a shadow grows
//                    under it and it swells a little
//   travel  ~560 ms  along a gentle arc to the slot, shrinking to print size
//                    and turning toward its resting angle; its paper border
//                    fades in on the way
//   press   ~220 ms  pressed onto the cork; the pin goes in as it touches
//
// Transform and opacity only, every frame computed up front and handed to
// the compositor (WAAPI), hand-over and pin included, so nothing it shows
// waits for the main thread: a page busy on its first reveal after a long
// sleep can only make the tock late, never the print hang in the air.

import * as ipc from "./ipc.js";
import { PIN_Y } from "./layout.js";
import { cancel, dom, later, rand, state } from "./state.js";

const LIFT_MS = 110;
const TRAVEL_MS = 560;
const PRESS_MS = 220;
const FLIGHT_MS = LIFT_MS + TRAVEL_MS + PRESS_MS;
const PIN_MS = 170;           // the pin's drop, as in print.js's pin-on
const PIN_TOUCH = 100;        // ms into it the head meets the paper
const LIFT_SWELL = 1.025;     // how much the snip swells as it lifts off
const HELD_Y = -6;            // print.js pin-on's held pose: raised,
const HELD_SCALE = 1.06;      // scaled,
const BORDER = 4;             // --print-border
const FRAMES = 72;            // keyframes per flight (linear between them)
const DECODE_WAIT = 120;      // ms the flight waits for its picture before using the thumbnail
const TIP_DELAY = 380;        // ms after landing

/** Flights in progress, by print id: { el, anims }. */
const flying = new Map();

// ---------------------------------------------------------------- setup

/**
 * The backend's `flight` for a print that has just arrived, with its picture
 * already loading, or null if there is none.
 */
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
 * Where the print will rest: its slot's box in viewport px. The board may
 * be folded up or mid-swing, so it is held flat for the read; all within
 * this task, so that is never painted. While a swing is running it is read
 * as it is (holding it flat would cancel the swing).
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

// ---------------------------------------------------------------- the flight

/**
 * Flies `print` in. `rest` is where it lands (restingBox). `land` is told
 * what happens, for what only print.js does: `pin()` as the pin drives in
 * (the sound), `settled()` once it is all over, and `skipped()` if it never
 * took off (the board went up, or the print away, before the picture was
 * ready).
 */
export async function launch(print, flight, rest, land) {
  // A sharp first frame: wait (briefly) for the flight's own picture.
  const decoded = await Promise.race([flight.ready, new Promise((r) => setTimeout(() => r(false), DECODE_WAIT))]);
  if (print.leaving || !state.revealed) { releaseImage(flight.img); land.skipped(); return; }
  if (!decoded) { flight.img.src = print.data.thumb; }

  const lean = Math.sign(print.tilt || 1) * rand(0.8, 1.6);
  const frames = keyframes(flight.from, rest, print.tilt || 0, lean, flight.found);
  // Gone on the very frame the print takes over (its last frame is the
  // print's resting pose exactly).
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
  // The pin starts to drive in just before the print touches, while the
  // stand-in still covers it: the stand-in carries a copy, and the print's
  // own pin, in step with it, carries on from the hand-over.
  const pin = print.pin.cloneNode(true);
  el.appendChild(pin);
  document.body.appendChild(el);

  const at = (ms) => ms / FLIGHT_MS;
  const pressAt = LIFT_MS + TRAVEL_MS;
  const pinAt = FLIGHT_MS - PIN_TOUCH;
  // All started in this task, so they share one start time and the
  // compositor keeps them in step.
  const anims = [
    el.animate(frames, { duration: FLIGHT_MS, easing: "linear", fill: "forwards" }),
    // The shadow grows as it lifts off, then goes as it is pressed down.
    el.querySelector(".flight-shade").animate([
      { opacity: 0 },
      { offset: at(LIFT_MS), opacity: 0.75, easing: "ease-in-out" },
      { offset: at(pressAt), opacity: 1, easing: "cubic-bezier(.5, 0, .3, 1)" },
      { opacity: 0 },
    ], { duration: FLIGHT_MS, fill: "forwards" }),
    // The paper border appears once it is on its way.
    el.querySelector(".flight-paper").animate([
      { opacity: 0 },
      { offset: at(LIFT_MS + TRAVEL_MS * 0.12), opacity: 0, easing: "ease-in-out" },
      { offset: at(LIFT_MS + TRAVEL_MS * 0.6), opacity: 1 },
      { opacity: 1 },
    ], { duration: FLIGHT_MS, fill: "forwards" }),
    // The print itself waits, unseen, and takes over on the frame the
    // stand-in touches the cork...
    print.drop.animate([{ opacity: 0 }, { opacity: 0 }], { duration: FLIGHT_MS }),
    // ...as the pin drives in.
    ...[pin, print.pin].map((p) => p.animate(PIN_DROP, {
      duration: PIN_MS, delay: pinAt, easing: "cubic-bezier(.5, 0, .9, .4)", fill: "backwards",
    })),
  ];
  print.slot.classList.remove("awaiting");
  const timers = [];
  flying.set(print.id, { el, anims, timers, img: flight.img });
  ipc.debugAck("flight", { id: print.id });

  // The rest only sounds or settles, so a late timer shows nothing wrong.
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

/** The pin driving in, as in print.js's pin-on. */
const PIN_DROP = [
  { opacity: 0, transform: "translateY(-4px) scale(1.7)" },
  { offset: 0.6, opacity: 1, transform: "scale(.9)", easing: "cubic-bezier(.3, 1.4, .5, 1)" },
  { opacity: 1, transform: "none" },
];

/** Ends a flight at once (the board is tucking, or the print is going). */
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

/**
 * Lets go of a flight's picture, a snip up to 1600 px wide, once it is
 * done with: out of the page, and its source dropped so the decoded image
 * goes with it rather than waiting for the next garbage collection.
 */
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
 * Every frame of a flight from the snip `from` to the held pose over `rest`,
 * then pressed down to the print's resting pose there.
 *
 * The stand-in is laid out at `rest` and turns and scales about the pin
 * point O, like the print's own layers, so its transform is
 * translate(t) rotate(r) scale(sx, sy) with t chosen to put the photo's
 * centre on the flight path. At the start the photo (inset by the border)
 * covers `from` exactly: scale (from.w / photo w, from.h / photo h), no turn.
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
      // Pressed onto the cork, straightening to its resting angle.
      const k = press((ms - LIFT_MS - TRAVEL_MS) / PRESS_MS);
      c = { x: C1.x + (C2.x - C1.x) * k, y: C1.y + (C2.y - C1.y) * k };
      sx = sy = HELD_SCALE + (1 - HELD_SCALE) * k;
      r = r1 + (tilt - r1) * k;
    } else if (ms <= LIFT_MS) {
      // Lifting off in place.
      const k = easeOut(ms / LIFT_MS);
      const swell = 1 + (LIFT_SWELL - 1) * k;
      c = C0; sx = sx0 * swell; sy = sy0 * swell; r = 0;
    } else {
      const u = travel((ms - LIFT_MS) / TRAVEL_MS);
      c = bezier(C0, P1, C1, u);
      // Shrinks a little ahead of the travel, as a thing moving away does.
      const us = Math.min(1, u * 1.08);
      sx = geo(sx0 * LIFT_SWELL, HELD_SCALE, us);
      sy = geo(sy0 * LIFT_SWELL, HELD_SCALE, us);
      // Turns to its resting angle in the second half.
      r = r1 * smooth(0.25, 1, u);
    }
    if (!found) {
      // Not found on screen: it grows out of the pointer instead.
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

/**
 * The arc's control point: most of the way across early, so the print
 * swings over and rises into its slot from below. Straight below the slot
 * it still bows a little to the side.
 */
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

/** The travel's pace: away briskly, a soft arrival. */
const travel = cubicBezier(0.5, 0, 0.25, 1);
/** The press onto the cork, as print.js's pin-on. */
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

// ---------------------------------------------------------------- the tip

const CLOSE_SVG = `<svg viewBox="0 0 18 18" aria-hidden="true"><path d="M6.4 6.4l5.2 5.2M11.6 6.4l-5.2 5.2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" fill="none"/></svg>`;

let tip = null;

/**
 * Pins the one-time tip under the board, and tells the backend where it is
 * (so it takes clicks, and the board stays down to read it; the backend
 * also remembers it has been shown).
 */
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
  // Where it rests, before the entrance moves it.
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
  // Dropped onto the board's edge and pinned: it swings once and settles.
  el.animate([
    { opacity: 0, transform: "translateY(-10px) rotate(-2.4deg)" },
    { offset: 0.45, opacity: 1, transform: "translateY(1px) rotate(.4deg)" },
    { offset: 0.75, transform: "translateY(0) rotate(-.9deg)" },
    { opacity: 1, transform: "rotate(-.6deg)" },
  ], { duration: 520, easing: "cubic-bezier(.3, .7, .4, 1)" });
}

/** The tip's ×: it lifts away, and the board may go back up as usual. */
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

/** Takes the tip away at once (the board is tucking; the backend knows). */
export function dropTip() {
  if (!tip) return;
  tip.remove();
  tip = null;
}

// Board geometry: each print's size and place in the row, the board's width,
// the pin colours, and the board rect the backend uses for click-through
// (grown to take in an unfolded note, see note.js).
// The CSS sizes in styles/tokens.css mirror the constants here.
//
// One row, always. Kept prints lead, then a wider gap, then the rest newest
// first. The board fits its content up to the window width minus SIDE;
// beyond that the row scrolls (scroll.js). Prints are placed relative to the
// board's centre, so a width change never shifts the ones already there.

import * as ipc from "./ipc.js";
import { LAYOUT_EASING, LAYOUT_MS } from "./motion/spring.js";
import { reveal as scrollIntoView, scrollPos, scrollTo, setExtent } from "./scroll.js";
import { dom, state } from "./state.js";

// Keep in sync with tokens.css.
export const TOP_HIDDEN = 24;     // --top-hidden
export const BOARD_H = 150;       // --board-h
const BORDER = 4;                 // --print-border
const PIN_SIZE = 10.5;            // --pin-size
export const PIN_Y = 7;           // pin centre, from a print's top (--pin-y in print.css)

export const PRINT_TOP = 13;             // prints hang from a common line this far below the screen edge
const PHOTO_MAX_W = 160;          // long side at most 160 px (design.md)
const PHOTO_MAX_H = 100;          // leaves room for the age caption under the tallest print
const PHOTO_MIN = 40;
// A paper note is one fixed size, so a row of mixed prints and notes stays
// calm. Its height leaves room under it for a link's three-line caption.
const NOTE_W = 136;
const NOTE_H = 86;
const PAD = 24;                   // cork left and right of the outermost prints
const GAP = 18;                   // between prints
const GROUP_GAP = 40;             // between the kept prints and the recent ones
export const MIN_W = 360;
const SIDE = 80;                  // board max width = window width - SIDE
const LIFT = 8;                   // margin around the board rect for hover lift

const PALETTE = ["terracotta", "sage", "ink", "sand", "graphite"];

let viewW = MIN_W;                // the row's visible width (= board width)
let fits = true;                  // the whole row is visible, no scrolling
const layoutHooks = new Set();

/** Calls `fn` after every layout (note.js folds an unfolded note that moved). */
export function onLayout(fn) { layoutHooks.add(fn); }

/** The photo's size on the board for an image of w x h. */
function photoSize(w, h) {
  if (!(w > 0 && h > 0)) return { w: PHOTO_MAX_W, h: Math.round(PHOTO_MAX_W * 9 / 16) };
  const s = Math.min(PHOTO_MAX_W / w, PHOTO_MAX_H / h);
  return {
    w: Math.round(Math.max(PHOTO_MIN, w * s)),
    h: Math.round(Math.max(PHOTO_MIN * 0.75, h * s)),
  };
}

/** Sizes a print's element (and places its pin) for its image size. */
export function measure(print) {
  if (print.data.kind === "note") {
    print.w = NOTE_W;
    print.h = NOTE_H;
  } else {
    const p = photoSize(print.data.width, print.data.height);
    print.w = p.w + BORDER * 2;
    print.h = p.h + BORDER * 2;
  }
  print.slot.style.width = `${print.w}px`;
  print.slot.style.height = `${print.h}px`;
  print.pin.style.left = `${print.w / 2 - PIN_SIZE / 2}px`;
  print.pin.style.top = `${PIN_Y - PIN_SIZE / 2}px`;
}

/** A stable first colour for a print, from its id. */
export function colorFor(id) {
  let h = 2166136261;
  for (let i = 0; i < id.length; i++) h = Math.imul(h ^ id.charCodeAt(i), 16777619);
  return PALETTE[(h >>> 0) % PALETTE.length];
}

// Neighbouring pins never share a colour. Kept pins are brass, so only
// unkept neighbours count. A clash recolours the print that is newer to the
// board, so pins people have already seen keep theirs.
function fixPinColors(live) {
  for (let i = 1; i < live.length; i++) {
    const a = live[i - 1], b = live[i];
    if (a.data.kept || b.data.kept || a.color !== b.color) continue;
    const victim = b.settled && !a.settled ? a : b;
    const near = new Set([live[i - 2]?.color, a.color, b.color, live[i + 1]?.color]);
    const next = PALETTE.find((c) => !near.has(c)) || PALETTE.find((c) => c !== a.color);
    victim.color = next;
    victim.pin.dataset.c = next;
  }
  for (const c of live) c.settled = true;
}

/**
 * Lays the row out and sizes the board. `instant`: jump, no slides (boot,
 * a new monitor). Slots glide to new places with the CSS transition on
 * their transform; the board's width change plays as a scaleX FLIP on the
 * cork, so nothing animates layout.
 */
export function layout({ instant = false } = {}) {
  const live = state.order.map((id) => state.prints.get(id)).filter(Boolean);
  const n = live.length;
  const winW = window.innerWidth;
  const maxW = winW > 0 ? Math.max(MIN_W, winW - SIDE) : Infinity;

  let cursor = 0;
  const offs = live.map((c, i) => {
    if (i > 0) cursor += live[i - 1].data.kept && !c.data.kept ? GROUP_GAP : GAP;
    const o = cursor;
    cursor += c.w;
    return o;
  });
  const contentW = n ? cursor + 2 * PAD : 0;
  const boardW = Math.round(Math.min(maxW, Math.max(MIN_W, contentW)));
  viewW = boardW;
  fits = contentW <= viewW;
  const base = (fits ? -contentW : -viewW) / 2 + PAD;

  live.forEach((c, i) => {
    c.x = Math.round(base + offs[i]);
    c.y = PRINT_TOP;
    const t = `translate(${c.x}px, ${c.y}px)`;
    if (c.t !== t) { c.t = t; c.slot.style.transform = t; }
  });
  fixPinColors(live);
  setExtent(contentW, viewW);

  if (boardW !== state.boardW) {
    const old = state.boardW;
    state.boardW = boardW;
    dom.board.style.width = `${boardW}px`;
    if (old && !instant && state.revealed && !state.reduced) {
      dom.cork.getAnimations().forEach((a) => a.cancel());
      dom.cork.animate([{ transform: `scaleX(${old / boardW})` }, { transform: "none" }],
        { duration: LAYOUT_MS, easing: LAYOUT_EASING });
    }
  }
  if (dom.hint.classList.contains("show") !== (n === 0)) {
    dom.hint.classList.toggle("show", n === 0);
    if (n === 0) dom.board.setAttribute("aria-description", "Empty. Take a screenshot with Windows key, Shift, S.");
    else dom.board.removeAttribute("aria-description");
  }
  sendRect(instant);
  for (const fn of layoutHooks) fn();
}

/** True when some of the print shows in the row's visible window. */
export function inView(print) {
  if (fits) return true;
  const pos = scrollPos();
  return print.x + print.w > -viewW / 2 + pos && print.x < viewW / 2 + pos;
}

/** Scrolls the row so the print is fully visible; true if it had to scroll. */
export function showPrint(print) {
  if (fits) return false;
  return scrollIntoView(print.x + viewW / 2, print.x + print.w + viewW / 2, viewW);
}

/**
 * Where the row rests when the board comes down: at the start, unless the
 * kept prints fill the view, then just far enough to show the newest print.
 */
export function restScroll() {
  scrollTo(0, true);
  if (fits) return;
  const first = state.order.map((id) => state.prints.get(id)).find((c) => c && !c.data.kept);
  if (!first) return;
  const right = first.x + first.w + viewW / 2 + PAD;
  if (right > viewW) scrollTo(right - viewW, true);
}

// ---------------------------------------------------------------- board rect
// In physical px relative to the window, for click-through. Scrolling moves
// the prints inside the board, never the board, so the rect only follows the
// width. While the cork's width animates, the rect covers old and new.
let rectTimer = 0, rectSettle = 0, lastRect = "";
let rectPrevW = 0;
let extraRect = null;             // an unfolded note's sheet, CSS px relative to the window
export function sendRect(instant = false) {
  clearTimeout(rectTimer);
  rectTimer = setTimeout(() => {
    rectTimer = 0;
    if (instant) rectPrevW = state.boardW;
    pushRect(Math.max(rectPrevW, state.boardW));
    clearTimeout(rectSettle);
    rectSettle = setTimeout(() => { rectSettle = 0; rectPrevW = state.boardW; pushRect(state.boardW); }, LAYOUT_MS + 40);
  }, 16);
}

/**
 * Something hangs below the board that must take clicks and wheel turns too
 * (an unfolded note): `r` in CSS px relative to the window, or null. The
 * rect sent is the union of the board and it.
 */
export function setExtraRect(r) {
  extraRect = r;
  pushRect(Math.max(rectPrevW, state.boardW));
}

/** Forgets the last rect sent, so the next one goes out even if unchanged. */
export function forgetRect() {
  lastRect = "";
}

function pushRect(w) {
  const dpr = window.devicePixelRatio || 1;
  const winW = window.innerWidth;
  let x = Math.max(0, (winW - w) / 2 - LIFT);
  let right = Math.min(winW, (winW + w) / 2 + LIFT);
  let bottom = BOARD_H + LIFT;
  if (extraRect) {
    x = Math.max(0, Math.min(x, extraRect.x - LIFT));
    right = Math.min(winW, Math.max(right, extraRect.x + extraRect.w + LIFT));
    bottom = Math.max(bottom, extraRect.y + extraRect.h + LIFT);
  }
  const r = {
    x: Math.round(x * dpr),
    y: 0,
    w: Math.round((right - x) * dpr),
    h: Math.round(bottom * dpr),
  };
  const key = `${r.x},${r.y},${r.w},${r.h}`;
  if (key === lastRect) return;
  lastRect = key;
  ipc.setBoardRect(r);
}

/** Cancels pending rect updates (tuck): no timers while tucked. */
export function stopRectTimers() {
  if (rectTimer) { clearTimeout(rectTimer); rectTimer = 0; }
  if (rectSettle) { clearTimeout(rectSettle); rectSettle = 0; rectPrevW = state.boardW; pushRect(state.boardW); }
}

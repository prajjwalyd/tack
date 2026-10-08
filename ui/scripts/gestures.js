// What the pointer does to a print:
//   click          copy_print (held back DBL_MS in case a second click follows)
//   double click   open (note.js openItem: open_print, or a text note unfolds)
//   press and hold onHold below (today edit_print; the print sinks as the hold builds)
//   drag > 4 px    start_drag (a native file drag; the backend takes over)
//   right click    context_menu
//   × button       discard_print (unpin)
//   Keep button    set_kept (toggle)
// Hover lives here too. The window can turn click-through under the pointer
// (board:pointer-left), and the row can scroll under a still pointer, so the
// browser's own enter/leave events are not the whole story. So does the
// hovered print's lean toward the pointer and the sheen that follows it
// (print.css), updated at most once a frame and only while hovered.

import * as ipc from "./ipc.js";
import { openItem } from "./note.js";
import { hideCaption, showCaption, toggleKeep } from "./print.js";
import { isScrolling, onScrollState } from "./scroll.js";
import { dom, state } from "./state.js";

const HOLD_MS = 520;     // keep in step with --dur-press in tokens.css
const DBL_MS = 260;
const DRAG_PX = 4;

const LEAN_DEG = 0.9;     // the most a hovered print leans on its pin toward the pointer

// What a press-and-hold does. The owner is still deciding; to make hold keep
// a print instead, change this one line to:
//   const onHold = (print) => toggleKeep(print);
const onHold = (print) => ipc.editPrint(print.id);

// ---------------------------------------------------------------- hover

/** Makes `print` the hovered one (or none). At most one print is hovered. */
export function setHover(print, on = true) {
  if (on) {
    if (!print || print.leaving || state.draggingId === print.id || isScrolling()) return;
    if (state.hovered === print) return;
    if (state.hovered) clearHover();
    state.hovered = print;
    print.slot.classList.add("hover");
    showCaption(print);
  } else if (state.hovered === print) {
    clearHover();
  }
}

/** Nothing is hovered any more. */
export function clearHover() {
  const p = state.hovered;
  if (!p) return;
  state.hovered = null;
  p.slot.classList.remove("hover");
  hideCaption(p);
  resetTilt(p);
}

// ---------------------------------------------------------------- lean and sheen

let tiltPrint = null, tiltX = 0, tiltY = 0, tiltRaf = 0;

/** The pointer moved over `print` (client px): lean it next frame. */
function trackTilt(print, x, y) {
  if (state.reduced || state.hovered !== print || state.press || print.leaving) return;
  // The slot's box is untouched by the lean (that is on the card), so it
  // is a steady frame to measure against; read once per hover.
  print.tiltRect ||= print.slot.getBoundingClientRect();
  tiltPrint = print; tiltX = x; tiltY = y;
  if (!tiltRaf) tiltRaf = requestAnimationFrame(applyTilt);
}

function applyTilt() {
  tiltRaf = 0;
  const print = tiltPrint, r = print?.tiltRect;
  if (!print || !r || state.hovered !== print || state.press) return;
  const nx = clamp(((tiltX - r.left) / r.width) * 2 - 1);
  const ny = clamp(((tiltY - r.top) / r.height) * 2 - 1);
  // Its free end swings toward the pointer, more the further down it is,
  // the way a finger near the bottom of a pinned print would move it.
  const lean = -nx * LEAN_DEG * (0.35 + 0.65 * (ny + 1) / 2);
  const style = print.el.style;
  style.setProperty("--lean", `${lean.toFixed(2)}deg`);
  style.setProperty("--sheen-x", nx.toFixed(3));
  style.setProperty("--sheen-y", ny.toFixed(3));
}

function resetTilt(print) {
  print.tiltRect = null;
  if (tiltPrint === print) { tiltPrint = null; cancelAnimationFrame(tiltRaf); tiltRaf = 0; }
  const style = print.el.style;
  style.removeProperty("--lean");
  style.removeProperty("--sheen-x");
  style.removeProperty("--sheen-y");
}

const clamp = (v) => Math.max(-1, Math.min(1, v));

// The last pointer position over the board, to find what is under it once
// a scroll settles (the pointer did not move, so no enter event comes).
let pointer = null;
dom.board.addEventListener("pointermove", (e) => { pointer = { x: e.clientX, y: e.clientY }; }, { passive: true });
dom.board.addEventListener("pointerleave", () => { pointer = null; });

onScrollState((moving) => {
  if (moving) { if (!state.press) clearHover(); return; }
  rehover();
});

/** Hovers whatever print is under the (still) pointer now. */
export function rehover() {
  if (!pointer || state.press || !state.revealed) return;
  const slot = document.elementFromPoint(pointer.x, pointer.y)?.closest?.(".slot");
  const print = slot && state.prints.get(slot.dataset.id);
  if (print) setHover(print, true);
}

/** The backend made the window click-through: the pointer is gone. */
export function onPointerLeft() {
  pointer = null;
  if (!state.press) clearHover();
}

// ---------------------------------------------------------------- presses

/** Ends the press in progress, if any. */
export function endPress(release = true) {
  const p = state.press;
  if (!p) return;
  clearTimeout(p.hold);
  p.print.slot.classList.remove("pressing");
  if (release) { try { p.print.slot.releasePointerCapture(p.pointerId); } catch {} }
  state.press = null;
}

/** Drops a click on `id` that was waiting to become a copy. */
export function forgetPendingClick(id) {
  if (state.pendingClick?.id === id) { clearTimeout(state.pendingClick.timer); state.pendingClick = null; }
}

/** The backend started the native drag: the print fades while it is away. */
export function onDragging(print, id) {
  state.draggingId = id;
  if (print) {
    if (state.hovered === print) clearHover();
    print.slot.classList.add("dragging");
    print.slot.classList.remove("pressing");
  }
}

/** The drop is over. The webview never saw the mouseup, so reset by hand. */
export function onDragEnded(print, id) {
  if (state.press?.print === print) endPress();
  if (state.draggingId === id) state.draggingId = null;
  if (print) print.slot.classList.remove("dragging", "pressing");
}

export function wireGestures(print) {
  const { slot } = print;
  const id = print.id;

  slot.addEventListener("pointerenter", (e) => { setHover(print, true); trackTilt(print, e.clientX, e.clientY); });
  slot.addEventListener("pointerleave", () => { if (state.press?.print !== print) setHover(print, false); });
  slot.addEventListener("pointermove", (e) => trackTilt(print, e.clientX, e.clientY), { passive: true });

  slot.addEventListener("pointerdown", (e) => {
    if (e.button !== 0 || print.leaving) return;
    e.preventDefault();
    const now = performance.now();
    const pc = state.pendingClick;
    if (pc && pc.id === id && now - pc.t < DBL_MS) {
      // Second click: open instead of copying twice.
      clearTimeout(pc.timer);
      state.pendingClick = null;
      endPress();
      state.press = { print, pointerId: e.pointerId, ignore: true };
      openItem(print);
      return;
    }
    endPress();
    try { slot.setPointerCapture(e.pointerId); } catch {}
    const p = {
      print, pointerId: e.pointerId, x: e.clientX, y: e.clientY,
      long: false, dragged: false, ignore: false,
    };
    p.hold = setTimeout(() => {
      if (state.press !== p || p.dragged) return;
      p.long = true;
      slot.classList.remove("pressing");
      onHold(print);
    }, HOLD_MS);
    state.press = p;
    slot.classList.add("pressing");
    setHover(print, true);
  });

  slot.addEventListener("pointermove", (e) => {
    const p = state.press;
    if (!p || p.print !== print || p.ignore || p.long || p.dragged) return;
    if (Math.hypot(e.clientX - p.x, e.clientY - p.y) <= DRAG_PX) return;
    p.dragged = true;
    clearTimeout(p.hold);
    slot.classList.remove("pressing");
    try { slot.releasePointerCapture(p.pointerId); } catch {}
    state.draggingId = id;   // no breeze until drag-ended
    ipc.startDrag(id);
  });

  slot.addEventListener("pointerup", (e) => {
    const p = state.press;
    if (!p || p.print !== print) return;
    const click = !p.ignore && !p.long && !p.dragged && e.button === 0;
    endPress();
    if (!slot.matches(":hover")) setHover(print, false);
    if (!click) return;
    const pc = { id, t: performance.now() };
    pc.timer = setTimeout(() => {
      if (state.pendingClick === pc) state.pendingClick = null;
      ipc.copyPrint(id);
    }, DBL_MS);
    if (state.pendingClick) { // a different print was clicked just before: let it copy now
      clearTimeout(state.pendingClick.timer);
      ipc.copyPrint(state.pendingClick.id);
    }
    state.pendingClick = pc;
  });

  slot.addEventListener("pointercancel", () => {
    if (state.press?.print === print) endPress(false);
  });

  slot.addEventListener("contextmenu", (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (print.leaving) return;
    // The menu key and Shift+F10 also fire this; keyboard.js has already
    // asked for the menu, placed at the print rather than at the pointer.
    if (performance.now() - state.keyMenuAt < 600) return;
    if (state.press?.print === print) endPress();
    forgetPendingClick(id);
    ipc.contextMenu(id);
  });

  // The two hover buttons act on click and keep the press machinery out.
  const button = (el, fn) => {
    el.addEventListener("pointerdown", (e) => { e.stopPropagation(); e.preventDefault(); });
    el.addEventListener("pointerup", (e) => e.stopPropagation());
    el.addEventListener("click", (e) => {
      e.stopPropagation();
      if (print.leaving || e.button !== 0) return;
      fn();
    });
  };
  button(print.discardButton, () => ipc.discardPrint(id));
  button(print.keepButton, () => toggleKeep(print));
}

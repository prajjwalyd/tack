// Tells the backend when the pointer is off the board. The window covers the
// whole work area and takes clicks only while the pointer is on the board (or
// the one-time tip); the backend turns it click-through the moment the pointer
// is not, and it hears the pointer everywhere else (the guard window under the
// board). Here the page is the only one to see it leave, so it says so once,
// never for every move. A press holds the board: its end says it instead.

import * as ipc from "./ipc.js";
import { state } from "./state.js";

/** The backend has been told the pointer is off the board and has not turned the window click-through yet. */
let told = false;

/** Whether the point (client px) is on the board or the tip: the very rects the backend has (state.hit), so both agree on where the window takes clicks. */
function overBoard(x, y) {
  const dpr = window.devicePixelRatio || 1;
  const px = Math.round(x * dpr);
  const py = Math.round(y * dpr);
  return state.hit.some((r) => r && px >= r.x && px < r.x + r.w && py >= r.y && py < r.y + r.h);
}

function tell() {
  if (told) return;
  told = true;
  ipc.pointerOffBoard();
}

function track(e, held) {
  if (overBoard(e.clientX, e.clientY)) told = false;
  else if (!held) tell();
}

document.addEventListener("pointermove", (e) => track(e, !!state.press), { passive: true });
// A drag, of a print out or of something in from another app, sends no pointer events but these.
document.addEventListener("dragover", (e) => track(e, false), { passive: true });
document.addEventListener("pointerup", (e) => { told = false; track(e, false); });
document.addEventListener("pointercancel", (e) => { told = false; track(e, false); });
// Gone from the window altogether, in one move.
document.documentElement.addEventListener("mouseleave", () => { if (!state.press) tell(); });

/** board:pointer-left: the window is click-through now, so the next time it takes clicks the pointer is on the board. */
export function onClickThrough() {
  told = false;
}

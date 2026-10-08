// The keyboard. The board window is never activated by the pointer, so the
// keyboard only reaches it after a keyboard open: the hotkey (Win+Alt+S)
// brings the board down with `board:reveal { reason: "hotkey" }`, the page
// asks for the focus (take_focus) and puts it on the first print. Any other
// reveal leaves the focus alone and shows no ring. On the tuck the focus
// goes back (release_focus).
//
// The row is a listbox with a roving focus: the focused print has
// tabindex 0 and aria-selected, the rest -1. It wears a focus ring and the
// hover look while the keyboard drives (#board.kbd); the pointer drops the
// ring. Keys:
//   ← →  Home End        move (the row scrolls to show the print)
//   Enter                copy
//   Ctrl+Enter           open (a text note unfolds)
//   K                    keep, or stop keeping
//   Delete               unpin
//   Shift+F10, menu key  the context menu, at the print
//   Esc                  fold an unfolded note, else tuck the board
//   Tab                  stays in the row

import * as ipc from "./ipc.js";
import { clearHover, setHover } from "./gestures.js";
import { PRINT_TOP, showPrint } from "./layout.js";
import { REVEAL_MS } from "./motion/spring.js";
import { fold, isUnfolded, openItem, sheetElement } from "./note.js";
import { toggleKeep } from "./print.js";
import { onScrollState, scrollPos } from "./scroll.js";
import { dom, later, state } from "./state.js";

let focusId = null;   // the print with the roving focus
let took = false;     // the page asked for the focus and owes it back

const items = () => state.order.map((id) => state.prints.get(id)).filter((p) => p && !p.leaving);
const focused = () => (focusId ? state.prints.get(focusId) || null : null);

/** A keyboard open (board:reveal with reason "hotkey"). `settleMs`: until the board is at rest. */
export function keyboardOpen(settleMs = REVEAL_MS) {
  took = true;
  dom.board.classList.add("kbd");
  const first = focused() && !focused().leaving ? focused() : items()[0] || null;
  focusOn(first, settleMs);
  // The window becomes the foreground one asynchronously; focus again once
  // it is, in case the webview dropped the first call.
  ipc.takeFocus().then(() => {
    if (!took || !state.revealed) return;
    const p = focused();
    (p ? p.slot : dom.board).focus({ preventScroll: true });
  });
}

/** The tuck: the focus goes back, keyboard mode ends. */
export function keyboardTuck() {
  dom.board.classList.remove("kbd");
  const p = focused();
  if (p) p.slot.classList.remove("focused");
  if (dom.board.contains(document.activeElement)) document.activeElement.blur();
  if (took) {
    took = false;
    ipc.releaseFocus();
  }
}

/**
 * A print is leaving (already out of state.order; it stood at `index`): if
 * it had the focus, the one now in its place takes it, else the one before.
 */
export function itemRemoved(print, index) {
  if (print.id !== focusId) return;
  const hadFocus = print.slot.contains(document.activeElement);
  print.slot.classList.remove("focused");
  setRoving(null);
  if (!hadFocus && !took) return;
  const list = items().filter((p) => p !== print);
  const next = list[Math.min(index, list.length - 1)] || null;
  focusOn(next);
}

/** Moves the roving focus (tabindex, aria-selected, the ring) to `print`. */
function setRoving(print) {
  const old = focused();
  if (old && old !== print) {
    old.slot.tabIndex = -1;
    old.slot.setAttribute("aria-selected", "false");
    old.slot.classList.remove("focused");
  }
  focusId = print ? print.id : null;
  dom.board.tabIndex = print ? -1 : 0;
  dom.board.classList.toggle("focused", !print);   // an empty board has the focus itself (hint.css)
  if (!print) return;
  print.slot.tabIndex = 0;
  print.slot.setAttribute("aria-selected", "true");
  print.slot.classList.add("focused");
}

/** Focuses `print` (or the board itself, when empty) and gives it the hover look. */
function focusOn(print, hoverAfter = 0) {
  setRoving(print);
  if (!print) {
    if (state.hovered) clearHover();
    dom.board.focus({ preventScroll: true });
    return;
  }
  print.slot.focus({ preventScroll: true });
  showPrint(print);
  // The hover look brings the frosted buttons; never while the board still swings.
  if (hoverAfter > 0) later(hoverFocused, hoverAfter);
  else hoverFocused();
}

function hoverFocused() {
  const p = focused();
  if (p && dom.board.classList.contains("kbd") && state.revealed) setHover(p, true);
}

// The row stopped scrolling under a keyboard move: hover the focused print
// again (hover is dropped while the row moves).
onScrollState((moving) => { if (!moving) hoverFocused(); });

// The pointer takes over: no ring until the next key.
dom.board.addEventListener("pointerdown", () => dom.board.classList.remove("kbd"));

/**
 * The context menu at the print's bottom-left corner, physical px relative
 * to the window. From the layout, not a measured box: the board may still
 * be turning on its hinge. (The board's top edge sits at the window's.)
 */
function menu(print) {
  const dpr = window.devicePixelRatio || 1;
  const left = (window.innerWidth - state.boardW) / 2 + state.boardW / 2 + print.x - scrollPos();
  const bottom = PRINT_TOP + print.h;
  state.keyMenuAt = performance.now();
  ipc.contextMenu(print.id, { x: Math.round(Math.max(0, left) * dpr), y: Math.round(bottom * dpr) });
}

document.addEventListener("keydown", (e) => {
  if (!state.revealed || e.defaultPrevented) return;
  if (!took && !dom.board.contains(document.activeElement)) return;
  const inSheet = !!sheetElement()?.contains(document.activeElement);
  const key = e.key;
  const plain = !e.ctrlKey && !e.altKey && !e.metaKey;
  const list = items();
  let p = focused();
  if (p && p.leaving) p = null;
  const i = p ? list.indexOf(p) : -1;
  const go = (target) => { e.preventDefault(); dom.board.classList.add("kbd"); if (isUnfolded()) fold(); focusOn(target); };

  switch (key) {
    case "ArrowRight":
    case "ArrowLeft": {
      if (!plain || !list.length) return;
      const step = key === "ArrowRight" ? 1 : -1;
      go(list[Math.max(0, Math.min(list.length - 1, i < 0 ? 0 : i + step))]);
      return;
    }
    case "Home":
    case "End":
      // Inside an unfolded note these scroll it.
      if (inSheet || !plain || !list.length) return;
      go(key === "Home" ? list[0] : list[list.length - 1]);
      return;
    case "Tab":
      e.preventDefault();
      dom.board.classList.add("kbd");
      if (!p && list.length) focusOn(list[0]);
      return;
    case "Escape":
      e.preventDefault();
      if (isUnfolded()) { fold(); return; }
      ipc.hideBoard();
      return;
    case "Enter":
      if (!p || e.altKey || e.metaKey) return;
      e.preventDefault();
      dom.board.classList.add("kbd");
      if (e.ctrlKey) openItem(p, { focus: true });
      else ipc.copyPrint(p.id);
      return;
    case "Delete":
      if (!p || !plain) return;
      e.preventDefault();
      ipc.discardPrint(p.id);
      return;
    case "ContextMenu":
      if (!p) return;
      e.preventDefault();
      menu(p);
      return;
    case "F10":
      if (!p || !e.shiftKey || !plain) return;
      e.preventDefault();
      menu(p);
      return;
    default:
      if ((key === "k" || key === "K") && plain && p && !e.repeat) {
        e.preventDefault();
        dom.board.classList.add("kbd");
        toggleKeep(p);
      }
  }
});

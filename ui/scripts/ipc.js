// The UI's side of the IPC contract (docs/ipc.md). The only module that
// touches window.__TAURI__: commands go out through the wrappers below,
// events come in through `on`. The Rust side is crates/tack-app/src/ipc/.
// Both webviews use it: the board (main.js) and the shortcuts dialog
// (shortcuts.js).

/** Event names, backend -> UI. Mirrors crates/tack-app/src/ipc/events.rs. */
export const EVENTS = Object.freeze({
  PRINT_ADDED: "board:print-added",          // { print: Print, animate: boolean, flight: Flight | null }
  PRINT_REMOVED: "board:print-removed",      // { id, how: "fall" | "quiet" }
  PRINT_UPDATED: "board:print-updated",      // { print: Print }
  PRINT_COPIED: "board:print-copied",        // { id }
  PRINT_DRAGGING: "board:print-dragging",    // { id }
  PRINT_DRAG_ENDED: "board:print-drag-ended",// { id }
  REVEAL: "board:reveal",                    // { reason: "edge" | "hotkey" | "new" | "tray" }
  TUCK: "board:tuck",                        // none
  WARM_UP: "board:warm-up",                  // none: draw the board once, unseen (startup)
  GUST: "board:gust",                        // none
  SETTINGS: "board:settings",                // { sound: boolean }
  POINTER_LEFT: "board:pointer-left",        // none: the window went click-through
  ORDER_CHANGED: "board:order-changed",      // { ids: string[] } the whole row, display order
  NOTICE: "board:notice",                    // { text: string } a short message under the board
});

/** Command names, UI -> backend. Mirrors crates/tack-app/src/ipc/commands.rs. */
export const COMMANDS = Object.freeze({
  BOARD_READY: "board_ready",
  SET_BOARD_RECT: "set_board_rect",
  COPY_PRINT: "copy_print",
  OPEN_PRINT: "open_print",
  EDIT_PRINT: "edit_print",
  START_DRAG: "start_drag",
  DISCARD_PRINT: "discard_print",
  CONTEXT_MENU: "context_menu",
  SET_HOVERING: "set_hovering",
  SET_KEPT: "set_kept",
  SET_TIP: "set_tip",
  PIN_TEXT: "pin_text",
  PIN_IMAGE: "pin_image",
  TAKE_FOCUS: "take_focus",
  RELEASE_FOCUS: "release_focus",
  HIDE_BOARD: "hide_board",
  // The shortcuts dialog (its own small window).
  SHORTCUTS_STATE: "shortcuts_state",
  SET_SHORTCUTS: "set_shortcuts",
  PAUSE_SHORTCUTS: "pause_shortcuts",
  CLOSE_SHORTCUTS: "close_shortcuts",
});

/**
 * @typedef {{ id: string, name: string, thumb: string, width: number, height: number,
 *             kept: boolean, keptAt: number | null, pinnedAt: number,
 *             kind?: "image" | "note", note?: Note | null }} Print
 * Times are ms since the epoch. The backend sends prints in display order:
 * kept first (by keptAt), then the rest newest first. `kind` is absent on
 * old payloads: treat that as "image". A note has `thumb` "", `width` and
 * `height` 0, and a name like "Note 2026-10-08 141530.txt".
 *
 * @typedef {{ text: string, link: string | null, domain: string | null,
 *             truncated: boolean }} Note
 * `text`: at most 20 KB (UTF-8), line breaks "\n". `link`: the URL when the
 * whole note is one http(s) link; `domain`: its host without "www.".
 * `truncated`: the text was longer than 20 KB and was cut there.
 *
 * @typedef {{ from: { x: number, y: number, w: number, h: number }, image: string,
 *             found: boolean, tip: boolean }} Flight
 * A new capture flying in (see flight.js): `from` is where it was taken, CSS
 * px relative to the window; `image` a sharp JPEG data URL of it; `found`
 * false if `from` is only a small rect at the pointer; `tip`: show the
 * one-time tip after it lands.
 */

// Read lazily: the preview harness installs its fake bridge before boot.
const tauri = () => window.__TAURI__;

/** True when the bridge is there (it always is inside the app). */
export function available() {
  const t = tauri();
  return !!(t?.core?.invoke && t?.event?.listen);
}

// A failed command is logged, never thrown: the board keeps working.
function invoke(cmd, args) {
  return Promise.resolve()
    .then(() => tauri().core.invoke(cmd, args))
    .catch((err) => { console.warn(`[tack] ${cmd} failed:`, err); return null; });
}

/** Listens to an event; `fn` gets the payload (or {}). Resolves once registered. */
export function on(name, fn) {
  return tauri().event.listen(name, (e) => fn(e.payload || {}));
}

/** @returns {Promise<{ prints: Print[], sound: boolean } | null>} */
export const boardReady = () => invoke(COMMANDS.BOARD_READY);
/** The board rect in physical px relative to the window. */
export const setBoardRect = (rect) => invoke(COMMANDS.SET_BOARD_RECT, rect);
export const copyPrint = (id) => invoke(COMMANDS.COPY_PRINT, { id });
export const openPrint = (id) => invoke(COMMANDS.OPEN_PRINT, { id });
export const editPrint = (id) => invoke(COMMANDS.EDIT_PRINT, { id });
export const startDrag = (id) => invoke(COMMANDS.START_DRAG, { id });
export const discardPrint = (id) => invoke(COMMANDS.DISCARD_PRINT, { id });
/**
 * The native menu for a print. `at`: where to show it, physical px relative
 * to the window (a keyboard request); null: at the pointer.
 */
export const contextMenu = (id, at = null) => invoke(COMMANDS.CONTEXT_MENU, { id, at });
export const setHovering = (hovering) => invoke(COMMANDS.SET_HOVERING, { hovering });
export const setKept = (id, kept) => invoke(COMMANDS.SET_KEPT, { id, kept });
/** The one-time tip shows at `rect` (physical px relative to the window), or `null`: closed. */
export const setTip = (rect) => invoke(COMMANDS.SET_TIP, { rect });
/** Pins text dropped on the board (a lone http(s) URL becomes a link note). */
export const pinText = (text) => invoke(COMMANDS.PIN_TEXT, { text });
/** Pins a PNG or JPEG dropped on the board: `data` is base64, no data: prefix. */
export const pinImage = (name, data) => invoke(COMMANDS.PIN_IMAGE, { name, data });
/** A keyboard open: the window takes the keyboard focus (it is never activated otherwise). */
export const takeFocus = () => invoke(COMMANDS.TAKE_FOCUS);
/** Gives the focus back to the app that had it. */
export const releaseFocus = () => invoke(COMMANDS.RELEASE_FOCUS);
/** Esc: tuck the board (the backend sends board:tuck). */
export const hideBoard = () => invoke(COMMANDS.HIDE_BOARD);

/**
 * @typedef {{ chord: string, status: "ok" | "in-use" | "off" | "invalid" }} Shortcut
 * `chord`: modifiers in the order Win, Ctrl, Alt, Shift, then the key, joined
 * with "+", e.g. "Win+Alt+S"; "" is off.
 */
/** @returns {Promise<{ toggle: Shortcut, pin: Shortcut, defaults: { toggle: string, pin: string } } | null>} */
export const shortcutsState = () => invoke(COMMANDS.SHORTCUTS_STATE);
/**
 * Saves both shortcuts, only if every one is free ("ok") or off.
 * @returns {Promise<{ toggle: Shortcut, pin: Shortcut } | null>} the statuses of the requested chords
 */
export const setShortcuts = (toggle, pin) => invoke(COMMANDS.SET_SHORTCUTS, { toggle, pin });
/** While a chord is being captured, Tack's own hotkeys must not fire. */
export const pauseShortcuts = (paused) => invoke(COMMANDS.PAUSE_SHORTCUTS, { paused });
export const closeShortcuts = () => invoke(COMMANDS.CLOSE_SHORTCUTS);

/**
 * Debug builds only: tells the backend's stress test what the page just did
 * ("reveal", "pin-on", "tock"). Does nothing unless the backend has set
 * `window.__tackDebug`, which release builds never do.
 */
export function debugAck(what, { id = null, queued = null } = {}) {
  if (window.__tackDebug) invoke("debug_ack", { what, id, queued });
}

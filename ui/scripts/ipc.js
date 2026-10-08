// The UI's side of the IPC contract (docs/ipc.md). The only module that
// touches window.__TAURI__: commands go out through the wrappers below,
// events come in through `on`. The Rust side is crates/tack-app/src/ipc/.

/** Event names, backend -> UI. Mirrors crates/tack-app/src/ipc/events.rs. */
export const EVENTS = Object.freeze({
  PRINT_ADDED: "board:print-added",          // { print: Print, animate: boolean }
  PRINT_REMOVED: "board:print-removed",      // { id, how: "fall" | "quiet" }
  PRINT_UPDATED: "board:print-updated",      // { print: Print }
  PRINT_COPIED: "board:print-copied",        // { id }
  PRINT_DRAGGING: "board:print-dragging",    // { id }
  PRINT_DRAG_ENDED: "board:print-drag-ended",// { id }
  REVEAL: "board:reveal",                    // { reason: "edge" | "hotkey" | "new" | "tray" }
  TUCK: "board:tuck",                        // none
  GUST: "board:gust",                        // none
  SETTINGS: "board:settings",                // { sound: boolean }
  POINTER_LEFT: "board:pointer-left",        // none: the window went click-through
  ORDER_CHANGED: "board:order-changed",      // { ids: string[] } the whole row, display order
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
});

/**
 * @typedef {{ id: string, name: string, thumb: string, width: number, height: number,
 *             kept: boolean, keptAt: number | null, pinnedAt: number }} Print
 * Times are ms since the epoch. The backend sends prints in display order:
 * kept first (by keptAt), then the rest newest first.
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
export const contextMenu = (id) => invoke(COMMANDS.CONTEXT_MENU, { id });
export const setHovering = (hovering) => invoke(COMMANDS.SET_HOVERING, { hovering });
export const setKept = (id, kept) => invoke(COMMANDS.SET_KEPT, { id, kept });

/**
 * Debug builds only: tells the backend's stress test what the page just did
 * ("reveal", "pin-on", "tock"). Does nothing unless the backend has set
 * `window.__tackDebug`, which release builds never do.
 */
export function debugAck(what, { id = null, queued = null } = {}) {
  if (window.__tackDebug) invoke("debug_ack", { what, id, queued });
}

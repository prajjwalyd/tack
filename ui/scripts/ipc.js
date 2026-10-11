// The UI's side of the IPC contract. The only module that
// touches window.__TAURI__: commands go out through the wrappers below,
// events come in through `on`. The Rust side is crates/tack-app/src/ipc/.
// Every webview uses it: the board (main.js), the settings window
// (settings/main.js, with the phone page).

/** Event names, backend -> UI. Mirrors crates/tack-app/src/ipc/events.rs. */
export const EVENTS = Object.freeze({
  PRINT_ADDED: "board:print-added",          // { print: Print, animate: boolean, flight: Flight | null }
  PRINT_REMOVED: "board:print-removed",      // { id, how: "fall" | "quiet" }
  PRINT_UPDATED: "board:print-updated",      // { print: Print }
  PRINT_COPIED: "board:print-copied",        // { id }
  PRINT_DRAGGING: "board:print-dragging",    // { id }
  PRINT_DRAG_ENDED: "board:print-drag-ended",// { id }
  REVEAL: "board:reveal",                    // { reason: "edge" | "hotkey" | "new" | "tray" | "settings", motionScale: number } the speed as it is now, applied before anything moves
  TUCK: "board:tuck",                        // none
  WARM_UP: "board:warm-up",                  // none: draw the board once, unseen (startup)
  GUST: "board:gust",                        // none
  SETTINGS: "settings:changed",              // SettingsView: all of them, whenever any changes (every window)
  POINTER_LEFT: "board:pointer-left",        // none: the window went click-through
  ORDER_CHANGED: "board:order-changed",      // { ids: string[] } the whole row, display order
  NOTICE: "board:notice",                    // { text: string } a short message under the board
  PHONE_STATE: "phone:state",                // PhoneState (settings/pages/phone.js), whenever it changes
  UPDATES_STATUS: "updates:status",          // UpdateStatus, whenever the update check or install moves on
  SETTINGS_SECTION: "settings:section",      // string: an open Settings window is asked to show that page or section ("phone", "appearance", "shortcuts", "updates")
});

/**
 * @typedef {{ id: string, name: string, thumb: string, width: number, height: number,
 *             kept: boolean, keptAt: number | null, pinnedAt: number,
 *             kind?: "image" | "note", note?: Note | null }} Print
 * Times are ms since the epoch. The backend sends prints in display order:
 * kept first (by keptAt), then the rest newest first. A missing `kind` means
 * "image". A note has `thumb` "", `width` and `height` 0, and a name like
 * "Note 2026-10-08 141530.txt".
 *
 * @typedef {{ id: string, name: string, hex: string }} FeltPreset
 * `hex`: the felt in a light theme, "#rrggbb"; darken it for a dark one.
 *
 * @typedef {{ id: string, name: string }} FramePreset
 *
 * @typedef {{ id: string, name: string, hex: string }} PaperPreset
 * `hex`: the paper in a light theme, "#rrggbb"; dim it for a dark one.
 *
 * @typedef {{ sound: boolean, edgeReveal: boolean, autostart: boolean,
 *             reveal: "instant" | "normal" | "slow",
 *             felt: string, feltPresets: FeltPreset[],
 *             frame: string, framePresets: FramePreset[],
 *             paper: string, paperPresets: PaperPreset[], motionScale: number,
 *             updatesCheck: boolean, version: string }} SettingsView
 * `felt`: the board colour: a preset's id ("green", "navy", "charcoal",
 * "burgundy", "plum", "teal", "slate"), "accent" (the Windows accent colour:
 * the CSS `AccentColor` keyword) or a custom "#rrggbb". `feltPresets`: the
 * presets, in order. `motionScale`: what every animation's duration (and
 * the holds after an event) is multiplied by (0.35 for Instant, 0.65 for
 * Normal, 1 for Slow): motion/scale.js. `frame`: the finish of the board's frame, a
 * preset's id ("aluminium", "graphite", "brass", "silver"); `paper`: the
 * colour of the notes, a preset's id ("white", "cream", "butter", "blush",
 * "sky", "mint"). Each preset list is in order, the first the default (the
 * finishes' gradients are the UI's: scripts/frame.js). `version`: this Tack,
 * "0.2.0".
 *
 * @typedef {{ state: "idle" | "checking" | "upToDate" | "available" | "downloading" | "installing" | "error",
 *             version: string | null, notes: string | null, progress: number | null,
 *             error: string | null, checkedAt: number | null }} UpdateStatus
 * `version`: the new version, once there is one. `notes`: its release notes,
 * UNTRUSTED text: show it as text only. `progress`: 0..1 while downloading
 * (null: not known). `checkedAt`: ms since the epoch of the last check.
 *
 * @typedef {{ text: string, link: string | null, domain: string | null,
 *             truncated: boolean }} Note
 * `text`: at most 20 KB (UTF-8), line breaks "\n". `link`: the URL when the
 * whole note is one http(s) link; `domain`: its host without "www.".
 * `truncated`: the text was longer than 20 KB and was cut there (the board does not show it).
 *
 * @typedef {{ from: { x: number, y: number, w: number, h: number }, image: string,
 *             found: boolean, tip: boolean, motionScale: number }} Flight
 * A new capture flying in (see flight.js): `from` is where it was taken, CSS
 * px relative to the window; `image` a sharp JPEG data URL of it; `found`
 * false if `from` is only a small rect at the pointer; `tip`: show the
 * one-time tip after it lands; `motionScale`: the speed, as for board:reveal (the flight is sent before the reveal).
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

/** @returns {Promise<{ prints: Print[], sound: boolean, settings: SettingsView } | null>} */
export const boardReady = () => invoke("board_ready");
/** The board rect in physical px relative to the window. */
export const setBoardRect = (rect) => invoke("set_board_rect", rect);
export const copyPrint = (id) => invoke("copy_print", { id });
export const openPrint = (id) => invoke("open_print", { id });
export const editPrint = (id) => invoke("edit_print", { id });
export const startDrag = (id) => invoke("start_drag", { id });
export const discardPrint = (id) => invoke("discard_print", { id });
/**
 * The native menu for a print. `at`: where to show it, physical px relative
 * to the window (a keyboard request); null: at the pointer.
 */
export const contextMenu = (id, at = null) => invoke("context_menu", { id, at });
export const setKept = (id, kept) => invoke("set_kept", { id, kept });
/** The one-time tip shows at `rect` (physical px relative to the window), or `null`: closed. */
export const setTip = (rect) => invoke("set_tip", { rect });
/** Pins text dropped on the board (a lone http(s) URL becomes a link note). */
export const pinText = (text) => invoke("pin_text", { text });
/** Pins a PNG or JPEG dropped on the board: `data` is base64, no data: prefix. */
export const pinImage = (name, data) => invoke("pin_image", { name, data });
/** A keyboard open: the window takes the keyboard focus (it is never activated otherwise). */
export const takeFocus = () => invoke("take_focus");
/** Gives the focus back to the app that had it. */
export const releaseFocus = () => invoke("release_focus");
/** Esc: tuck the board (the backend sends board:tuck). */
export const hideBoard = () => invoke("hide_board");

// The settings window. Each setter answers with the new SettingsView, and
// every window gets EVENTS.SETTINGS. A speed, colour, finish or paper that is
// not valid changes nothing; an unknown setting or a value of the wrong type
// is refused (a logged failure).
/** @returns {Promise<SettingsView | null>} */
export const settingsState = () => invoke("settings_state");
/**
 * Changes one setting: a boolean for "sound", "edgeReveal", "autostart" or
 * "updatesCheck"; an id for "reveal" ("instant", "normal" or "slow"), "felt"
 * (a preset id, "accent" or "#rrggbb"), "frame" or "paper" (a preset's id).
 * @param {string} key a SettingsView property
 * @param {boolean | string} value
 */
export const setSetting = (key, value) => invoke("set_setting", { key, value });
/** The board colour, frame and note paper back to their defaults; the other settings stay. */
export const resetAppearance = () => invoke("reset_appearance");
// Updates. Each answers with the new UpdateStatus, and the progress comes as EVENTS.UPDATES_STATUS.
/** @returns {Promise<UpdateStatus | null>} */
export const updateStatus = () => invoke("update_status");
/** Looks for a new version now. @returns {Promise<UpdateStatus | null>} */
export const checkForUpdates = () => invoke("check_for_updates");
/** Downloads and installs the version found. @returns {Promise<UpdateStatus | null>} */
export const installUpdate = () => invoke("install_update");
export const closeSettings = () => invoke("close_settings");

/**
 * @typedef {{ chord: string, status: "ok" | "in-use" | "off" | "invalid" }} Shortcut
 * `chord`: modifiers in the order Win, Ctrl, Alt, Shift, then the key, joined
 * with "+", e.g. "Win+Alt+S"; "" is off.
 */
/** @returns {Promise<{ toggle: Shortcut, pin: Shortcut, defaults: { toggle: string, pin: string } } | null>} */
export const shortcutsState = () => invoke("shortcuts_state");
/**
 * Applies and saves both shortcuts, only if every one is free ("ok") or off.
 * @returns {Promise<{ toggle: Shortcut, pin: Shortcut } | null>} the statuses of the requested chords
 */
export const setShortcuts = (toggle, pin) => invoke("set_shortcuts", { toggle, pin });
/** While a chord is being captured, Tack's own hotkeys must not fire. */
export const pauseShortcuts = (paused) => invoke("pause_shortcuts", { paused });

// The phone page. Each answers with the new PhoneState (settings/pages/phone.js).
export const phoneState = () => invoke("phone_state");
export const setPhone = (on) => invoke("set_phone", { on });
export const answerDevice = (key, allow) => invoke("answer_device", { key, allow });
export const forgetDevice = (key) => invoke("forget_device", { key });
export const openNetbirdDownload = () => invoke("open_netbird_download");

/** Debug builds only: tells the backend's stress test what the page just did ("reveal", "pin-on", "tock"). No-op unless `window.__tackDebug` is set. */
export function debugAck(what, { id = null, queued = null } = {}) {
  if (window.__tackDebug) invoke("debug_ack", { what, id, queued });
}

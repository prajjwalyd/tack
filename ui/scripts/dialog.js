// What the small dialog windows share (shortcuts.js, phone-link.js): the
// query helper, the warning icon, the keys a dialog must not pass to the
// browser, and the "is the backend there" check on load.

import * as ipc from "./ipc.js";

export const $ = (sel) => document.querySelector(sel);

export const WARN_SVG = `<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.8l6.4 11.4H1.6z" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/><path d="M8 6.2v3.4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/><circle cx="8" cy="11.6" r=".8" fill="currentColor"/></svg>`;

/** Call from a keydown handler: stops reload, print, zoom... acting on a dialog window. */
export function blockBrowserKeys(e) {
  const k = e.key.toLowerCase();
  if (e.key === "F5" || ((e.ctrlKey || e.metaKey) && ["r", "p", "f", "g", "u", "s", "o", "n", "j", "h", "+", "-", "=", "0"].includes(k))) e.preventDefault();
}

/** True when the backend bridge is there (it always is inside the app); logs if not. */
export function backendReady() {
  if (ipc.available()) return true;
  console.error("[tack] window.__TAURI__ is not available");
  return false;
}

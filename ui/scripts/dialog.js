// What the small dialog windows share (phone-link.js, settings/main.js): the
// query helper, the warning icon, text escaping, a relative time, the
// screen-reader announcer, the keys a dialog must not pass to the browser, and
// the "is the backend there" check on load.

import * as ipc from "./ipc.js";

export const $ = (sel) => document.querySelector(sel);

export const WARN_SVG = `<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.8l6.4 11.4H1.6z" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/><path d="M8 6.2v3.4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/><circle cx="8" cy="11.6" r=".8" fill="currentColor"/></svg>`;

/** Escapes text for use in innerHTML. */
export const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

/** "just now", "5 minutes ago", "yesterday", "3 days ago", or a date. */
export function when(ms) {
  const mins = Math.floor((Date.now() - ms) / 60000);
  if (!(mins >= 1)) return "just now";
  if (mins < 60) return `${mins} minute${mins === 1 ? "" : "s"} ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? "" : "s"} ago`;
  const midnight = (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((midnight(new Date()) - midnight(new Date(ms))) / 86400000);
  if (days <= 1) return "yesterday";
  if (days < 7) return `${days} days ago`;
  return `on ${new Date(ms).toLocaleDateString(undefined, { day: "numeric", month: "short", year: days > 300 ? "numeric" : undefined })}`;
}

/** Returns a function that says something to a screen reader through the polite live region `el`, without moving anything on screen. */
export function announcer(el) {
  let timer = 0;
  return (text) => {
    clearTimeout(timer);
    el.textContent = "";
    timer = setTimeout(() => { el.textContent = text; }, 60);
  };
}

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

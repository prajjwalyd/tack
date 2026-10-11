// What a screen reader hears. The board is a listbox of prints and notes
// (keyboard.js moves the focus); each label says what it is and how old it
// is ("Screenshot, 2 minutes ago, kept", "Link: github.com, just now"). Things
// that happen without moving the focus ("Copied", "Pinned", notices) go
// through one visually hidden polite live region.

const region = document.createElement("div");
region.id = "announce";
region.className = "visually-hidden";
region.setAttribute("role", "status");
region.setAttribute("aria-live", "polite");
region.setAttribute("aria-atomic", "true");
document.body.appendChild(region);

import { ago } from "./age.js";
import { fixed } from "./motion/scale.js";

let clearTimer = 0;

/** Says `text` politely. The same text twice in a row is said twice. */
export function announce(text) {
  if (!text) return;
  clearTimeout(clearTimer);
  // Empty first, fill on the next task: a live region speaks only on change.
  region.textContent = "";
  clearTimer = setTimeout(() => {
    region.textContent = text;
    // Cleared later so a stale "Copied" is never read out by browsing.
    clearTimer = setTimeout(() => { clearTimer = 0; region.textContent = ""; },
      fixed(4000, "a screen reader needs the time to read it out"));
  }, fixed(40, "a live region speaks only on a change after a task of its own"));
}

/** The board tucked: clear the region and its timer. */
export function hush() {
  clearTimeout(clearTimer);
  clearTimer = 0;
  region.textContent = "";
}

/** "just now", "1 minute ago", "3 hours ago", "yesterday", "on Tuesday", "on 12 September". */
const spokenAge = (t) => ago(t, { on: "on ", month: "long" });

/** The first `n` words of a note, on one line. */
function firstWords(text, n = 8) {
  const words = (text || "").trim().split(/\s+/).filter(Boolean);
  if (!words.length) return "empty";
  const head = words.slice(0, n).join(" ");
  return words.length > n ? `${head}…` : head;
}

/** A print's label for the listbox (data: an IPC Print; t: when it was pinned). */
export function labelFor(data, t) {
  const note = data.kind === "note" ? data.note : null;
  let what = "Screenshot";
  if (note?.link) what = `Link: ${note.domain || note.link}`;
  else if (note) what = `Note: ${firstWords(note.text)}`;
  return `${what}, ${spokenAge(t)}${data.kept ? ", kept" : ""}`;
}

// What a screen reader hears. The board is a listbox of prints and notes
// (keyboard.js moves the focus); each one's label says what it is and how
// old it is, spelled out for speech ("Screenshot, 2 minutes ago, kept",
// "Note: buy milk on the way home, 5 minutes ago", "Link: github.com, just
// now"). Things that happen without moving the focus ("Copied", "Pinned",
// notices) go through one visually hidden polite live region.

const region = document.createElement("div");
region.id = "announce";
region.className = "visually-hidden";
region.setAttribute("role", "status");
region.setAttribute("aria-live", "polite");
region.setAttribute("aria-atomic", "true");
document.body.appendChild(region);

let clearTimer = 0;

/** Says `text` politely. The same text twice in a row is said twice. */
export function announce(text) {
  if (!text) return;
  clearTimeout(clearTimer);
  // Emptied first, then filled on the next task: a live region only speaks
  // when its content changes.
  region.textContent = "";
  clearTimer = setTimeout(() => {
    region.textContent = text;
    // Emptied again a little later, so a stale "Copied" is never read out
    // by someone browsing the page; nothing else runs meanwhile.
    clearTimer = setTimeout(() => { clearTimer = 0; region.textContent = ""; }, 4000);
  }, 40);
}

/** The board tucked: nothing left to say, and no timer left behind. */
export function hush() {
  clearTimeout(clearTimer);
  clearTimer = 0;
  region.textContent = "";
}

const dayFmt = new Intl.DateTimeFormat(undefined, { weekday: "long" });
const dateFmt = new Intl.DateTimeFormat(undefined, { day: "numeric", month: "long" });
const yearFmt = new Intl.DateTimeFormat(undefined, { day: "numeric", month: "long", year: "numeric" });

/** "just now", "1 minute ago", "3 hours ago", "yesterday", "on Tuesday", "on 12 September". */
export function spokenAge(t, now = Date.now()) {
  const s = Math.max(0, (now - t) / 1000);
  if (s < 45) return "just now";
  const min = Math.max(1, Math.round(s / 60));
  if (min < 60) return min === 1 ? "1 minute ago" : `${min} minutes ago`;
  const a = new Date(t), b = new Date(now);
  const days = Math.round((new Date(b.getFullYear(), b.getMonth(), b.getDate()) - new Date(a.getFullYear(), a.getMonth(), a.getDate())) / 86400000);
  if (days <= 0) { const h = Math.floor(min / 60); return h === 1 ? "1 hour ago" : `${h} hours ago`; }
  if (days === 1) return "yesterday";
  if (days < 7) return `on ${dayFmt.format(a)}`;
  return `on ${(a.getFullYear() === b.getFullYear() ? dateFmt : yearFmt).format(a)}`;
}

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

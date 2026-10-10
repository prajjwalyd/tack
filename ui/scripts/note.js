// Paper notes: text and links pinned in the row like prints. A note is a print
// with `data.kind` "note": print.js builds it with the same layers
// and calls `fillNote` for its paper. This module also decides what "open"
// means (`openItem`): every kind goes to the backend, which opens the file or
// address with the default app (a screenshot in the viewer, a link in the
// browser, a text note in the text editor).

import * as ipc from "./ipc.js";

const NOTE_CHARS = 360;       // about four lines; the paper clamps the rest

const LINK_SVG = `<svg viewBox="0 0 10 10" aria-hidden="true"><path d="M4.3 5.7l1.4-1.4M3.6 4.2L2.5 5.3a1.6 1.6 0 0 0 2.2 2.2l1.1-1.1M6.4 5.8l1.1-1.1a1.6 1.6 0 0 0-2.2-2.2L4.2 3.6" stroke="currentColor" stroke-width="1.1" stroke-linecap="round" fill="none"/></svg>`;

/** True for a note (text or link), false for a screenshot. */
export function isNote(data) {
  return data?.kind === "note" && !!data.note;
}

/** A note's link, if the whole note is one. */
export function linkOf(data) {
  return isNote(data) ? data.note.link || null : null;
}

/** An address as a note shows it: no scheme, no trailing slash. */
export function bareLink(url) {
  return String(url).replace(/^https?:\/\//i, "").replace(/\/$/, "");
}

/** Writes an address into `el` with break chances after / ? & = # and before a dot, so it wraps between parts. */
function writeAddress(el, url) {
  el.textContent = "";
  for (const part of url.split(/(?<=[/?&=#])|(?=\.)/)) {
    if (el.firstChild) el.appendChild(document.createElement("wbr"));
    el.appendChild(document.createTextNode(part));
  }
}

/** Builds (or rebuilds) a note's paper and caption parts. */
export function fillNote(print) {
  const note = print.data.note || { text: "" };
  const link = note.link || null;
  const paper = print.paper;
  print.slot.classList.add("is-note");
  print.slot.classList.toggle("is-link", !!link);
  paper.textContent = "";
  const p = document.createElement("p");
  p.className = "note-text";
  if (link) writeAddress(p, bareLink(link));
  else p.textContent = (note.text || "").trim().slice(0, NOTE_CHARS);
  paper.appendChild(p);
  if (link) {
    const d = document.createElement("span");
    d.className = "note-domain";
    d.innerHTML = LINK_SVG;
    const name = document.createElement("span");
    name.textContent = note.domain || bareLink(link).split("/")[0];
    d.appendChild(name);
    paper.appendChild(d);
  }
  // A link's caption: full address over the age (print.js fills the age).
  const caption = print.caption;
  caption.classList.toggle("link", !!link);
  caption.textContent = "";
  print.captionAge = null;
  if (link) {
    const url = document.createElement("span");
    url.className = "cap-url";
    writeAddress(url, link);
    const age = document.createElement("span");
    age.className = "cap-age";
    caption.append(url, age);
    print.captionAge = age;
  }
}

/** "Open" for any kind: the backend opens the screenshot, address or text file with the default app. */
export function openItem(print) {
  if (print && !print.leaving) ipc.openPrint(print.id);
}

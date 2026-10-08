// Drop to pin. Drag an image file, link or text to the top of the screen: the
// backend brings the board down, and dropping on the board pins it. The window
// has dragDropEnabled off so HTML5 drag and drop reaches the page:
//   - over the board, a pinnable drag gets the copy cursor and a soft light
//     wash on the cork with "Drop to pin";
//   - elsewhere the page refuses it and cancels every dragover and drop, or
//     the webview would navigate to a dropped file;
//   - a drop pins PNG/JPEG files (pin_image, base64), else the first address of
//     a text/uri-list, else plain text (pin_text). Nothing is ever fetched.
// A print dragged out of the board (state.draggingId) is never a drop.

import * as ipc from "./ipc.js";
import { notice } from "./notice.js";
import { dom, state } from "./state.js";

const MAX_BYTES = 40 * 1024 * 1024;
const CHUNK = 3 * 8192;            // multiple of 3 so base64 pieces join cleanly
const IMAGE_TYPES = new Set(["image/png", "image/jpeg"]);
const IMAGE_EXT = /\.(png|jpe?g)$/i;

const cue = document.createElement("div");
cue.className = "drop-cue";
cue.setAttribute("aria-hidden", "true");
cue.innerHTML = `<span class="drop-label"><svg viewBox="0 0 12 12" aria-hidden="true"><path d="M6 2.2v5.6M3.6 5.6L6 8l2.4-2.4M2.6 9.8h6.8" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round" fill="none"/></svg>Drop to pin</span>`;
dom.board.appendChild(cue);

let over = false;

/** True when the drag carries something the board can pin (types only: the data is locked until the drop). */
function pinnable(dt) {
  if (!dt) return false;
  const types = Array.from(dt.types || []);
  return types.includes("Files") || types.includes("text/uri-list") || types.includes("text/plain");
}

function setOver(on) {
  if (over === on) return;
  over = on;
  dom.board.classList.toggle("drop-over", on);
}

/** The board tucked or the drag went elsewhere. */
export function clearDrop() { setOver(false); }

function onBoardDrag(e) {
  e.preventDefault();
  const ok = state.revealed && !state.draggingId && pinnable(e.dataTransfer);
  if (e.dataTransfer) e.dataTransfer.dropEffect = ok ? "copy" : "none";
  setOver(ok);
}

dom.board.addEventListener("dragenter", onBoardDrag);
dom.board.addEventListener("dragover", onBoardDrag);
dom.board.addEventListener("dragleave", (e) => {
  // Moving between the board's own parts fires leave/enter pairs; only leaving the board (or window: no relatedTarget) counts.
  const to = e.relatedTarget;
  if (!to || !dom.board.contains(to)) setOver(false);
});

// Everywhere else: refuse, never navigate.
document.addEventListener("dragover", (e) => {
  e.preventDefault();
  if (!dom.board.contains(e.target)) {
    if (e.dataTransfer) e.dataTransfer.dropEffect = "none";
    setOver(false);
  }
});
document.addEventListener("drop", (e) => {
  e.preventDefault();
  setOver(false);
  if (!dom.board.contains(e.target) || !state.revealed || state.draggingId) return;
  pinFrom(e.dataTransfer);
});
document.addEventListener("dragend", () => setOver(false));

/** Pins what was dropped; the backend answers with board:print-added (or a notice). */
function pinFrom(dt) {
  if (!dt) return;
  const files = Array.from(dt.files || []);
  if (files.length) {
    const images = files.filter((f) => IMAGE_TYPES.has(f.type) || IMAGE_EXT.test(f.name || ""));
    if (!images.length) { notice("Only images and text can be pinned"); return; }
    const fits = images.filter((f) => f.size <= MAX_BYTES);
    if (!fits.length) { notice("Can't pin that image"); return; }
    pinImages(fits);
    return;
  }
  const uris = dt.getData("text/uri-list");
  const uri = uris && uris.split(/\r?\n/).map((l) => l.trim()).find((l) => l && !l.startsWith("#"));
  if (uri) { ipc.pinText(uri); return; }
  const text = dt.getData("text/plain");
  if (text && text.trim()) ipc.pinText(text);
}

async function pinImages(files) {
  for (const file of files) {
    try {
      const data = base64(new Uint8Array(await file.arrayBuffer()));
      await ipc.pinImage(file.name || "Image.png", data);
    } catch (err) {
      console.warn("[tack] could not read a dropped image:", err);
      notice("Can't pin that image");
    }
  }
}

/** Base64 of `bytes` a chunk at a time, avoiding giant argument lists. */
function base64(bytes) {
  const parts = [];
  for (let i = 0; i < bytes.length; i += CHUNK) {
    parts.push(btoa(String.fromCharCode.apply(null, bytes.subarray(i, i + CHUNK))));
  }
  return parts.join("");
}

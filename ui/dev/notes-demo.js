// Preview only: drop to pin, played with synthetic drag events (a browser
// tab cannot be dragged onto from a script any other way), and a few set
// scenes for screenshots. Loaded after fake-ipc.js (it needs
// window.preview) and main.js.
//
//   "Drop text"   dragenter, dragover, then a drop on the board, carrying
//                 text and a link in turn (text/plain, text/uri-list)
//   "Drop image"  the same with a PNG file drawn on a canvas
//
// ?demo= sets a scene up once the board is down (for headless renders):
//   keyboard      a keyboard open, then → twice: the focus ring
//   unfold        a text note unfolded (&note=<n>: which note, from 0)
//   drop          a drag held over the board: the drop affordance
//   notice        the "Nothing selected" notice
//   hover         the link note hovered: its full address and age
// ?theme=light|dark forces the board's theme (and a matching wallpaper), and
// ?clean hides the harness's buttons and log, for screenshots.

const preview = window.preview;
const board = document.getElementById("board");
const params = new URLSearchParams(location.search);

if (params.has("clean")) document.body.classList.add("clean");
const theme = params.get("theme");
if (theme === "light" || theme === "dark") {
  document.documentElement.dataset.theme = theme;
  document.body.classList.toggle("light", theme === "light");
}

/** Dispatches a synthetic drag event carrying `dt` on the board's cork. */
function fire(type, dt) {
  const target = board.querySelector(".cork") || board;
  const e = new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: dt });
  target.dispatchEvent(e);
  return e;
}

/** Drags `dt` over the board and, after `holdMs`, drops it (or holds it, if holdMs < 0). */
function dragOver(dt, holdMs = 700) {
  fire("dragenter", dt);
  fire("dragover", dt);
  if (holdMs < 0) return;
  setTimeout(() => fire("drop", dt), holdMs);
}

let textTurn = 0;
const DROP_TEXTS = [
  "Dropped from a browser: the board keeps short notes like this one too.",
  "https://www.rust-lang.org/learn",
];

function textTransfer() {
  const text = DROP_TEXTS[textTurn++ % DROP_TEXTS.length];
  const dt = new DataTransfer();
  if (/^https?:/.test(text)) dt.setData("text/uri-list", `# a comment line\n${text}`);
  dt.setData("text/plain", text);
  return dt;
}

/** A small made-up picture as a PNG file. */
async function imageFile() {
  const c = document.createElement("canvas");
  c.width = 640; c.height = 400;
  const g = c.getContext("2d");
  const sky = g.createLinearGradient(0, 0, 0, 400);
  sky.addColorStop(0, "#9fd3ff"); sky.addColorStop(1, "#f6e7c8");
  g.fillStyle = sky; g.fillRect(0, 0, 640, 400);
  g.fillStyle = "#ffd36b"; g.beginPath(); g.arc(470, 120, 46, 0, 7); g.fill();
  g.fillStyle = "#4f7a5a"; g.beginPath(); g.moveTo(0, 400);
  for (let x = 0; x <= 640; x += 16) g.lineTo(x, 300 - Math.sin(x / 60) * 30 - (x % 48) / 4);
  g.lineTo(640, 400); g.fill();
  const blob = await new Promise((r) => c.toBlob(r, "image/png"));
  return new File([blob], "Dropped picture.png", { type: "image/png" });
}

preview.dropText = () => dragOver(textTransfer());
preview.dropImage = async () => {
  const dt = new DataTransfer();
  dt.items.add(await imageFile());
  dragOver(dt);
};
preview.dropFile = () => {   // not pinnable: a notice
  const dt = new DataTransfer();
  dt.items.add(new File(["hello"], "notes.pdf", { type: "application/pdf" }));
  dragOver(dt);
};

// scenes
const demo = params.get("demo");
const slotOf = (pred) => {
  const p = preview.prints.find(pred);
  return p && board.querySelector(`.slot[data-id="${p.id}"]`);
};
const key = (k, opts = {}) => (document.activeElement || document).dispatchEvent(
  new KeyboardEvent("keydown", { key: k, bubbles: true, cancelable: true, ...opts }));
const atRest = (fn) => setTimeout(fn, 1100);

if (demo === "keyboard") {
  preview.reveal = preview.revealKeyboard;
  atRest(() => { key("ArrowRight"); setTimeout(() => key("ArrowRight"), 120); });
} else if (demo === "unfold") {
  let n = +params.get("note") || 0;
  atRest(() => {
    const slot = slotOf((p) => p.kind === "note" && !p.note.link && n-- === 0);
    if (!slot) return;
    // Two quick presses: a double click (gestures.js).
    for (let i = 0; i < 2; i++) {
      slot.dispatchEvent(new PointerEvent("pointerdown", { bubbles: true, button: 0, pointerId: 1, isPrimary: true }));
      slot.dispatchEvent(new PointerEvent("pointerup", { bubbles: true, button: 0, pointerId: 1, isPrimary: true }));
    }
  });
} else if (demo === "drop") {
  atRest(() => dragOver(textTransfer(), -1));
} else if (demo === "notice") {
  atRest(() => preview.nothingSelected());
} else if (demo === "hover") {
  atRest(() => slotOf((p) => p.note?.link)?.dispatchEvent(new PointerEvent("pointerenter", { clientX: 0, clientY: 0 })));
} else if (demo === "tour") {
  // Every button and key in turn, for a console check: "[demo] tour done" at the end.
  const steps = [
    "add", "addQuiet", "addNote", "addLink", "keep", "shuffle", "copied", "gust", "drag", "update",
    "pointerLeft", "nothingSelected", "dropText", "dropText", "dropImage", "dropFile", "fill", "fall", "quiet",
    "tuck", () => preview.emit("board:notice", { text: "Already pinned" }), "revealKeyboard",
    ...["ArrowRight", "ArrowRight", "End", "Home", "k", "Delete", "ArrowRight", "Tab"].map((k) => () => key(k)),
    () => key("Enter"), () => key("Enter", { ctrlKey: true }), () => key("F10", { shiftKey: true }),
    () => key("ContextMenu"), () => key("Escape"), () => key("Escape"),
    "reveal", () => preview.fly?.(true), "clear", "tuck", "revealKeyboard", () => key("Escape"),
  ];
  let i = 0;
  const next = () => {
    if (i >= steps.length) { console.info("[demo] tour done"); return; }
    const s = steps[i++];
    if (typeof s === "function") s(); else preview[s]();
    setTimeout(next, 450);
  };
  atRest(next);
}

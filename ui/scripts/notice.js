// Notices: a short, quiet message under the board ("Nothing selected",
// "Already pinned"...), from the backend (board:notice) or the page itself (a
// drop it cannot pin). A frosted pill hangs centred in the overhang for about
// 1.8 s; the live region says it too.
//
// The backend reveals the board just before a notice but the events can race:
// a notice arriving while tucked is held and shown after the next reveal,
// unless that reveal is over ~2 s away (then it is stale and dropped).
//
// The pill is outside #board so it never turns with the swing. It shows only
// once the board is at rest, since nothing frosted exists mid-swing (print.css).

import { announce } from "./announce.js";
import { REVEAL_MS } from "./motion/spring.js";
import { cancel, later, state } from "./state.js";

const SHOW_MS = 1800;
const STALE_MS = 2000;   // a held notice older than this at the reveal is dropped

const pill = document.createElement("div");
pill.id = "notice";
pill.setAttribute("aria-hidden", "true");   // the live region speaks it
document.body.appendChild(pill);

let held = null;          // { text, at } waiting for a reveal
let hideTimer = 0;
let showTimer = 0;
let anim = null;

/** A notice from the backend or the page. */
export function notice(text) {
  if (!text) return;
  if (!state.revealed) { held = { text, at: performance.now() }; return; }
  show(text);
}

/** The board is coming down (`wasRevealed`: it already was): a held notice shows once it is at rest. */
export function noticeAfterReveal(wasRevealed) {
  const h = held;
  held = null;
  if (!h || performance.now() - h.at > STALE_MS) return;
  if (wasRevealed) { show(h.text); return; }
  cancel(showTimer);
  showTimer = later(() => { showTimer = 0; show(h.text); }, state.reduced ? 100 : REVEAL_MS);
}

/** The tuck: away at once. */
export function hideNotice() {
  held = null;
  cancel(showTimer); showTimer = 0;
  cancel(hideTimer); hideTimer = 0;
  anim?.cancel(); anim = null;
  pill.classList.remove("show");
}

function show(text) {
  announce(text);
  cancel(hideTimer);
  anim?.cancel();
  pill.textContent = text;
  pill.classList.add("show");
  anim = pill.animate(
    state.reduced
      ? [{ opacity: 0 }, { opacity: 1 }]
      : [{ opacity: 0, transform: "translateY(-4px) scale(.98)" }, { opacity: 1, transform: "none" }],
    { duration: state.reduced ? 120 : 200, easing: "cubic-bezier(.2, .8, .25, 1)" });
  hideTimer = later(() => {
    anim = pill.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 240, easing: "ease-in", fill: "forwards" });
    anim.onfinish = () => { pill.classList.remove("show"); anim?.cancel(); anim = null; };
  }, SHOW_MS);
}

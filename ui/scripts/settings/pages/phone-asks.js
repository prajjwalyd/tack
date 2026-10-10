// The requests at the top of the phone page: "pixel wants to use your board",
// with the code the phone shows. A security prompt, so: it sits first, Allow
// wakes up a moment after the question appears and never takes the focus (a
// click or key meant for something else must not answer it), the focus moves
// only within it, and only if it was already there.

import * as ipc from "../../ipc.js";
import { esc } from "../../dialog.js";
import { slot } from "../markup.js";

const ASK_SVG = `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2.8l7 2.6v5.6c0 4.4-2.8 8-7 10-4.2-2-7-5.6-7-10V5.4z" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"/><path d="M9 11.8l2.2 2.2L15.2 10" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>`;

/** How long the question is on screen before Allow answers to a click. */
const ARM_MS = 1000;

/** The cards for the devices asking (Asking[]); Allow starts disabled. */
export function askHtml(pending) {
  return pending.map((p, i) => `
    <div class="ask" role="group" aria-labelledby="ask-${i}" aria-describedby="ask-${i}-who ask-${i}-code" data-key="${esc(p.key)}">
      <div class="ask-head">
        <span class="ask-icon">${ASK_SVG}</span>
        <div>
          <h2 class="ask-title" id="ask-${i}" tabindex="-1">${esc(p.name)} wants to use your board</h2>
          <p class="ask-sub" id="ask-${i}-who">${esc(p.fqdn)} · ${esc(p.ip)}</p>
        </div>
      </div>
      <p class="ask-code" id="ask-${i}-code">Allow only if your phone shows <b>${esc(p.code)}</b></p>
      <div class="ask-actions">
        <button type="button" class="std" data-act="deny" aria-label="Don't allow ${esc(p.name)}">Don't allow</button>
        <button type="button" class="std accent" data-act="allow" aria-label="Allow ${esc(p.name)}" disabled>Allow</button>
      </div>
    </div>`).join("");
}

export function mountAsks(host, api) {
  const update = slot(host);
  let keys = null;   // who was asking at the last render; null before the first
  let armTimer = 0;

  async function answer(card, allow) {
    const key = card.dataset.key;
    const name = api.phone.get().pending.find((p) => p.key === key)?.name || "The device";
    const buttons = card.querySelectorAll("button");
    buttons.forEach((b) => { b.disabled = true; });
    const state = await ipc.answerDevice(key, allow);
    if (state) {
      api.announce(allow ? `${name} can use your board` : `${name} was not allowed`);
      api.phone.set(state);
    } else {
      buttons.forEach((b) => { b.disabled = false; });
      api.announce("Couldn't answer. Try again.");
    }
  }

  host.addEventListener("click", (e) => {
    const button = e.target.closest("[data-act]");
    if (button) answer(button.closest(".ask"), button.dataset.act === "allow");
  });

  return {
    render({ pending }) {
      const hadFocus = host.contains(document.activeElement);
      const known = keys;
      keys = new Set(pending.map((p) => p.key));
      if (!update(pending, () => askHtml(pending))) return;
      // Every change of the question starts the wait again.
      clearTimeout(armTimer);
      armTimer = setTimeout(() => host.querySelectorAll('[data-act="allow"]').forEach((b) => { b.disabled = false; }), ARM_MS);
      if (!pending.length) {
        if (hadFocus) api.focusTitle();   // the focused button just went away
        return;
      }
      host.parentElement.scrollTop = 0;
      const fresh = known ? pending.filter((p) => !known.has(p.key)) : [];
      if (fresh.length && api.page() === "phone") {
        api.announce(`${fresh.map((p) => p.name).join(" and ")} ${fresh.length > 1 ? "are" : "is"} asking to use your board`);
      }
      if (hadFocus) host.querySelector(".ask-title").focus();
    },
  };
}

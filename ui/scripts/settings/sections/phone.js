// Use on your phone: the card at the top of Settings. It says how the phone
// link stands (the PhoneState of phone-link.js, the same phone_state command
// and phone:state event) and holds one button that opens the phone window,
// where the switch, the QR code and the allowed devices are. That window stays
// separate: it also opens by itself when a device asks to use the board.

import * as ipc from "../../ipc.js";
import { PHONE_SVG } from "../../phone-icon.js";

/** What the card says until the backend has answered (or if it cannot). */
const TAGLINE = "See your board on your phone, over NetBird.";

const devices = (n) => (n === 0 ? "no devices yet" : `${n} device${n === 1 ? "" : "s"}`);

/**
 * What to show for a PhoneState (null: not known yet): the status line, a
 * `tone` for its dot ("ok", "attn", "warn" or "none"), and the button's label.
 * Pure, so the wording is easy to check. The words are the phone window's.
 * @param {import("../../phone-link.js").PhoneState | null} s
 */
export function describe(s) {
  if (!s) return { status: TAGLINE, tone: "none", button: "Set up" };
  const button = s.on ? "Manage" : "Set up";
  const pending = s.pending?.length ?? 0;
  if (s.netbird === "missing") return { status: "NetBird isn't installed", tone: "warn", button };
  if (!s.on) return { status: "Off", tone: "none", button };
  if (s.netbird === "disconnected") return { status: "NetBird isn't connected", tone: "warn", button };
  if (pending) {
    const who = pending === 1 && s.pending[0].name ? s.pending[0].name : `${pending} devices`;
    return { status: `${who} ${pending === 1 ? "wants" : "want"} to use your board`, tone: "attn", button };
  }
  if (s.serving) return { status: `On · ${devices(s.devices?.length ?? 0)}`, tone: "ok", button };
  if (s.error) return { status: s.error, tone: "warn", button };
  return { status: "Getting ready…", tone: "none", button };
}

export function mount(host, api) {
  host.className = "phone-card";
  host.dataset.section = "phone";
  host.setAttribute("aria-labelledby", "phone-title");
  host.innerHTML = `
    <div class="card hero-card">
      <span class="tile" aria-hidden="true">${PHONE_SVG}</span>
      <div class="hero-text">
        <h2 class="hero-title" id="phone-title" tabindex="-1">Use on your phone</h2>
        <p class="row-desc" id="phone-status"></p>
      </div>
      <button type="button" class="std" id="phone-button" aria-describedby="phone-status"></button>
    </div>`;
  const status = host.querySelector("#phone-status");
  const button = host.querySelector("#phone-button");
  let shown = null;   // the status line last shown, so only a change is announced

  function render(state) {
    const d = describe(state);
    status.textContent = d.status;
    status.dataset.tone = d.tone;
    button.textContent = d.button;
    button.classList.toggle("accent", d.button === "Set up");
    if (shown !== null && shown !== d.status && state) api.announce(`Use on your phone: ${d.status}`);
    shown = d.status;
  }

  render(null);
  button.addEventListener("click", () => ipc.openPhoneLink());
  ipc.on(ipc.EVENTS.PHONE_STATE, render);
  ipc.phoneState().then((state) => { if (state) render(state); });
  return {};
}

// "Your devices": the ones allowed to use the board, each with Remove.

import * as ipc from "../../ipc.js";
import { esc, when } from "../../dialog.js";
import { slot } from "../markup.js";

/** The list for the allowed devices (Device[]); nothing when there are none. */
export function devicesHtml(devices) {
  if (!devices.length) return "";
  return `
    <h2 class="sec" id="devices-title">Your devices</h2>
    <ul class="card list">
      ${devices.map((d) => `
      <li class="dev">
        <div class="dev-text">
          <span class="dev-name">${esc(d.name)}</span>
          <span class="dev-when">Allowed ${esc(when(d.approvedAt))}</span>
        </div>
        <button type="button" class="text" data-act="forget" data-key="${esc(d.key)}" aria-label="Remove ${esc(d.name)}">Remove</button>
      </li>`).join("")}
    </ul>`;
}

export function mountDevices(host, api) {
  const update = slot(host);

  async function forget(key) {
    const name = api.phone.get().devices.find((d) => d.key === key)?.name || "The device";
    const state = await ipc.forgetDevice(key);
    if (state) {
      api.announce(`${name} removed`);
      api.phone.set(state);
    } else {
      api.announce("Couldn't remove it. Try again.");
    }
  }

  host.addEventListener("click", (e) => {
    const button = e.target.closest('[data-act="forget"]');
    if (button) forget(button.dataset.key);
  });

  return {
    render({ devices }) {
      const hadFocus = host.contains(document.activeElement);
      if (!update(devices, () => devicesHtml(devices)) || !hadFocus) return;
      // The Remove button that had the focus is gone: keep the keyboard nearby.
      const next = host.querySelector('[data-act="forget"]');
      if (next) next.focus();
      else api.focusTitle();
    },
  };
}

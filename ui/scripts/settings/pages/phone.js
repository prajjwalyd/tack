// The phone page of Settings: the phone card's button, the tray's "Use on your
// phone..." (settings.html#phone) and a device asking to use the board all
// land here. It holds the on/off switch, the QR code and address, the "pixel
// wants to use your board" prompt, and the list of allowed devices.
//
// Everything renders from one PhoneState, which main.js keeps in a store
// (`api.phone`): it is sent on phone:state and returned by every command. Each
// part is rebuilt only when its own slice changes, so keyboard focus is never
// pulled away.
//
// A request is a security prompt: it sits first, it never takes the focus
// from anywhere else, Enter acts only on the focused button, and Esc (the
// window's) closes without allowing (phone-asks.js).

import * as ipc from "../../ipc.js";
import { PHONE_SVG } from "../../phone-icon.js";
import { showSwitch, switchRow } from "../markup.js";
import { mountAsks } from "./phone-asks.js";
import { mountBody } from "./phone-body.js";
import { mountDevices } from "./phone-devices.js";

/**
 * @typedef {{ key: string, name: string, approvedAt: number }} Device
 * @typedef {{ key: string, name: string, fqdn: string, ip: string, code: string }} Asking
 * @typedef {{ on: boolean, netbird: "missing" | "disconnected" | "connected",
 *             serving: boolean, address: string | null, qr: string | null,
 *             error: string | null, pc: string | null,
 *             devices: Device[], pending: Asking[] }} PhoneState
 * `approvedAt`: ms since the epoch. `qr`: trusted SVG markup from the backend.
 * Device names are NetBird's names for them (set in NetBird, not by the
 * device over this connection); always shown as text.
 */

export function mount(host, api) {
  host.innerHTML = `
    <section id="phone-asks" aria-label="Requests to use your board"></section>
    <div class="hero">
      <span class="tile" aria-hidden="true">${PHONE_SVG}</span>
      <p>Snip on your PC, it's on your phone. Pin a photo on your phone, it's on your PC. Any network.</p>
    </div>
    <div class="card">${switchRow("phone-on", "Use on phone")}</div>
    <div id="phone-body"></div>
    <section id="phone-devices" aria-labelledby="devices-title"></section>
    <p class="quiet">Your board stays on this PC.</p>`;
  const sw = host.querySelector("#phone-on");
  const parts = [
    mountAsks(host.querySelector("#phone-asks"), api),
    mountBody(host.querySelector("#phone-body"), api),
    mountDevices(host.querySelector("#phone-devices"), api),
  ];
  let switching = false;   // set_phone in flight

  sw.addEventListener("click", async () => {
    if (switching) return;
    switching = true;
    sw.setAttribute("aria-busy", "true");
    const want = !api.phone.get()?.on;
    showSwitch(sw, want);   // at once; the answer settles it
    const state = await ipc.setPhone(want);
    switching = false;
    sw.removeAttribute("aria-busy");
    if (state) {
      api.phone.set(state);
    } else {
      showSwitch(sw, !!api.phone.get()?.on);
      api.announce("Couldn't change that. Try again.");
    }
  });

  api.phone.subscribe((next) => {
    const state = { ...next, devices: next.devices || [], pending: next.pending || [] };
    showSwitch(sw, state.on);
    for (const part of parts) part.render(state);
  });
}

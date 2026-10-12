// The body of the phone page under the switch: what is needed (NetBird), or
// the QR code and address once the board is being served.

import * as ipc from "../../ipc.js";
import { WARN_SVG, esc } from "../../dialog.js";
import { slot } from "../markup.js";

/** How long "Copied" shows on the button. */
const COPIED_MS = 1800;

/** What to show for a PhoneState: one card or line. `qr` is the backend's own SVG. */
export function bodyHtml(s) {
  if (s.netbird === "missing") {
    return `
      <div class="card block note">
        <p>Tack reaches your phone through NetBird, a free, open-source private network. Install it on this PC and on your phone, and sign in to the same account on both.</p>
        <button type="button" class="std accent" data-act="get-netbird">Get NetBird</button>
        <p class="small">Then turn this on.</p>
      </div>`;
  }
  if (!s.on) {
    return `<p class="quiet">Only devices on your NetBird network can ask, and only ones you allow get in.</p>`;
  }
  if (s.netbird === "disconnected") {
    return `<div class="card block note"><p>NetBird isn't connected on this PC. Connect it, and Tack picks it up.</p></div>`;
  }
  if (s.serving && s.address) {
    return `
      <div class="card block share">
        ${s.qr ? `<div class="qr" role="img" aria-label="QR code for ${esc(s.address)}">${s.qr}</div>` : ""}
        <p class="scan">${s.qr ? "Scan with your phone's camera" : "Open this on your phone"}</p>
        <div class="addr-row">
          <span class="addr" id="phone-addr">${esc(s.address)}</span>
          <button type="button" class="std" data-act="copy" aria-describedby="phone-addr">Copy</button>
        </div>
        <p class="quiet center needs">Your phone needs the NetBird app, signed in to the same account.</p>
      </div>`;
  }
  if (s.error) {
    return `<div class="card block problem" role="alert">${WARN_SVG}<span>${esc(s.error)}</span></div>`;
  }
  return `<p class="quiet">Getting ready…</p>`;
}

export function mountBody(host, api) {
  const update = slot(host);
  let serving = null;   // whether it was serving at the last render; null before the first
  let copyTimer = 0;

  async function copy(button) {
    const address = api.phone.get()?.address;
    if (!address) return;
    try {
      await navigator.clipboard.writeText(address);
    } catch (err) {
      console.warn("[tack] copy failed:", err);
      // Select it so Ctrl+C is one key away.
      getSelection().selectAllChildren(host.querySelector("#phone-addr"));
      api.announce("Couldn't copy. The address is selected: press Ctrl+C.");
      return;
    }
    button.textContent = "Copied";
    button.classList.add("done");
    api.announce("Address copied");
    clearTimeout(copyTimer);
    copyTimer = setTimeout(() => {
      button.textContent = "Copy";
      button.classList.remove("done");
    }, COPIED_MS);
  }

  host.addEventListener("click", (e) => {
    const button = e.target.closest("[data-act]");
    if (button?.dataset.act === "copy") copy(button);
    if (button?.dataset.act === "get-netbird") ipc.openNetbirdDownload();
  });

  return {
    render(s) {
      update({ n: s.netbird, on: s.on, serving: s.serving, a: s.address, q: s.qr, e: s.error }, () => bodyHtml(s));
      if (serving === false && s.serving && api.page() === "phone") api.announce("Ready. Scan the code with your phone.");
      serving = s.serving;
    },
  };
}

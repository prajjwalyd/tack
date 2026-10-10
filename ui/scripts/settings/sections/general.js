// General: the switches the tray's menu has too (Sound, Reveal at top edge,
// Start with Windows). A change shows at once and the backend's answer
// settles it (main.js's `change`).

import * as ipc from "../../ipc.js";
import { group, showSwitch, switchRow } from "../markup.js";

const SWITCHES = [
  { key: "sound", label: "Sound", desc: "A soft sound when a snip is pinned.", set: ipc.setSound },
  { key: "edgeReveal", label: "Reveal at top edge", desc: "Move the pointer to the top of the screen to show the board.", set: ipc.setEdgeReveal },
  { key: "autostart", label: "Start with Windows", desc: "Open Tack when you sign in.", set: ipc.setAutostart },
];

export function mount(host, api) {
  group(host, "general", "General", `<div class="card">${SWITCHES.map((s) => switchRow(`switch-${s.key}`, s.label, s.desc)).join("")}</div>`);
  const buttons = SWITCHES.map((s) => host.querySelector(`#switch-${s.key}`));
  SWITCHES.forEach((s, i) => buttons[i].addEventListener("click", () => {
    const on = buttons[i].getAttribute("aria-checked") !== "true";
    api.change({ [s.key]: on }, () => s.set(on));
  }));
  return {
    render: (view) => SWITCHES.forEach((s, i) => showSwitch(buttons[i], !!view[s.key])),
  };
}

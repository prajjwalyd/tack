// Related: rows that open the other windows (Shortcuts, Use on your phone).

import * as ipc from "../../ipc.js";
import { CHEVRON_SVG, group } from "../markup.js";

const LINKS = [
  { open: ipc.openShortcuts, title: "Shortcuts", desc: "Change the keys that show the board and pin a selection." },
  { open: ipc.openPhoneLink, title: "Use on your phone", desc: "See your board on your phone, over NetBird." },
];

export function mount(host) {
  group(host, "related", "Related", `<div class="card">${LINKS.map((l, i) => `
    <button type="button" class="row link" data-link="${i}">
      <span class="row-text"><span class="row-title">${l.title}</span><span class="row-desc">${l.desc}</span></span>
      ${CHEVRON_SVG}
    </button>`).join("")}</div>`);
  host.addEventListener("click", (e) => {
    const link = e.target.closest("[data-link]");
    if (link) LINKS[link.dataset.link].open();
  });
  return {};
}

// Markup the Settings sections share: a titled group, a switch row, the
// roving radio group and the chevron of a link row. Sections build their
// markup once and render by changing attributes and text, so a control that
// has the keyboard focus is never replaced under it.

import { esc } from "../dialog.js";

export const CHEVRON_SVG = `<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M6 3.5l4.5 4.5L6 12.5" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>`;

/** Fills `host` (a <section>) with a heading and `body`; the section is named by the heading. */
export function group(host, id, title, body) {
  host.className = "group";
  host.setAttribute("aria-labelledby", `${id}-title`);
  host.innerHTML = `<h2 class="group-title" id="${id}-title">${esc(title)}</h2>${body}`;
}

/** A switch row (see switch.css). Static text only: `label` and `desc` are ours. */
export function switchRow(id, label, desc) {
  return `
    <button type="button" class="switch" id="${id}" role="switch" aria-checked="false" aria-labelledby="${id}-label" aria-describedby="${id}-desc">
      <span class="switch-text"><span id="${id}-label">${label}</span><span class="switch-desc" id="${id}-desc">${desc}</span></span>
      <span class="state" aria-hidden="true">Off</span>
      <span class="track" aria-hidden="true"></span>
    </button>`;
}

/** Shows a switch as on or off. */
export function showSwitch(button, on) {
  button.setAttribute("aria-checked", String(on));
  button.querySelector(".state").textContent = on ? "On" : "Off";
}

// Appearance: how the board looks. Three cards, each a module in appearance/
// (the board colour, the frame finish, the note paper) with the same shape as
// a section, and a button that puts all three back. The board is a different
// window: it repaints from settings:changed.

import * as ipc from "../../ipc.js";
import { group } from "../markup.js";
import { defaults, isDefault } from "./appearance/defaults.js";
import * as felt from "./appearance/felt.js";
import * as frame from "./appearance/frame.js";
import * as paper from "./appearance/paper.js";

const CARDS = [felt, frame, paper];

export function mount(host, api) {
  group(host, "appearance", "Appearance", `
    ${CARDS.map((_, i) => `<div class="card block" data-card="${i}"></div>`).join("")}
    <div class="reset">
      <button type="button" class="text" id="appearance-reset" aria-disabled="true">Reset appearance</button>
    </div>`);
  const cards = CARDS.map((card, i) => card.mount(host.querySelector(`[data-card="${i}"]`), api));
  const reset = host.querySelector("#appearance-reset");

  // aria-disabled, not disabled, so the button keeps the keyboard focus once it has done its job.
  reset.addEventListener("click", async () => {
    if (reset.getAttribute("aria-disabled") === "true") return;
    await api.change(defaults(api.settings.get()), ipc.resetAppearance);
    api.announce("Appearance reset");
  });

  return {
    render(view) {
      for (const card of cards) card.render(view);
      reset.setAttribute("aria-disabled", String(isDefault(view)));
    },
  };
}

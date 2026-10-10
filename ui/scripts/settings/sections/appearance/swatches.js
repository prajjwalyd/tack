// What the Appearance cards share: a titled row of swatches in a radio group.
// A swatch is a button (role="radio") with a picture, a check mark when it is
// the one chosen, and its name under it. `mountChoice` builds a card for any
// list of presets (the frame, the note paper); the board colour has its own
// extras (felt.js) and uses the pieces.

import { esc } from "../../../dialog.js";
import { handleRadioKeys, showRadio } from "../../radio.js";

const CHECK_SVG = `<svg class="check" viewBox="0 0 16 16" aria-hidden="true"><path d="M3.5 8.4l3 3 6-6.4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>`;

/** The head of a card (its title, and the name of the choice at the right) and the empty radio group. */
export const choiceBlock = (id, title) => `
  <div class="block-head">
    <p class="row-title" id="${id}-label">${esc(title)}</p>
    <span class="row-value" id="${id}-name"></span>
  </div>
  <div class="swatches" role="radiogroup" aria-labelledby="${id}-label"></div>`;

/**
 * One swatch. `c`: { value (what is chosen), name (under it), label (what a
 * screen reader says) }, plus `manual` for one that opens a picker; `shape`
 * is the CSS class of its picture ("dot", "metal" or "note-chip"); `extra` is markup beside
 * the button.
 */
export function swatch(c, shape = "dot", extra = "") {
  return `
    <div class="swatch-cell" role="none">
      <button type="button" class="swatch" role="radio" aria-checked="false" tabindex="-1" data-value="${esc(c.value)}" aria-label="${esc(c.label)}"${c.manual ? " data-manual" : ""}>
        <span class="${shape}">${CHECK_SVG}</span>
        <span class="name" aria-hidden="true">${esc(c.name)}</span>
      </button>${extra}
    </div>`;
}

/**
 * Fills `card` with a titled row of swatches for a list of presets, and
 * returns `{ render }`. Options:
 *   id        a prefix for the element ids
 *   title     the card's title
 *   shape     the swatch picture's CSS class
 *   key       the SettingsView property holding the id that is chosen
 *   presets   the SettingsView property holding the list: [{ id, name }]
 *   set       sends a choice to the backend: (id) => Promise<SettingsView | null>
 *   paint     draws a swatch's picture: (picture element, preset, view)
 */
export function mountChoice(card, api, { id, title, shape, key, presets, set, paint }) {
  card.innerHTML = choiceBlock(id, title);
  const swatches = card.querySelector(".swatches");
  const nameOut = card.querySelector(`#${id}-name`);
  let built = "";

  handleRadioKeys(swatches);
  swatches.addEventListener("click", (e) => {
    const radio = e.target.closest('[role="radio"]');
    if (radio && api.settings.get()[key] !== radio.dataset.value) {
      api.change({ [key]: radio.dataset.value }, () => set(radio.dataset.value));
    }
  });

  function render(view) {
    const list = view[presets] || [];
    const listKey = JSON.stringify(list);
    if (listKey !== built) {
      built = listKey;
      swatches.innerHTML = list.map((p) => swatch({ value: p.id, name: p.name, label: p.name }, shape)).join("");
    }
    showRadio(swatches, view[key]);
    for (const radio of swatches.querySelectorAll('[role="radio"]')) {
      paint(radio.querySelector(`.${shape}`), list.find((p) => p.id === radio.dataset.value), view);
    }
    nameOut.textContent = list.find((p) => p.id === view[key])?.name ?? "";
  }
  return { render };
}

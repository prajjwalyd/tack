// Board colour: a swatch for each preset the backend lists (the hexes are
// Rust's alone), one that follows the Windows accent colour, and a custom one
// that opens the system colour picker. The swatches are drawn with the
// board's own theme code (theme.js), so each shows what the board will be, in
// this window's light or dark theme.

import * as ipc from "../../../ipc.js";
import { feltProperties } from "../../../theme.js";
import { handleRadioKeys, showRadio } from "../../radio.js";
import { choiceBlock, swatch } from "./swatches.js";

const ACCENT = { value: "accent", name: "Accent", label: "Windows accent colour" };
const CUSTOM = { value: "custom", name: "Custom", label: "Custom colour", manual: true };
const HEX = /^#[0-9a-f]{6}$/i;
const DRAG_MS = 120;   // while a colour is dragged in the picker, how often it is sent

const choicesOf = (presets) => [...presets.map((p) => ({ value: p.id, name: p.name, label: p.name })), ACCENT, CUSTOM];

/** Which swatch a felt is: a preset, "accent", or "custom" for any #rrggbb. */
const choiceOf = (felt) => (HEX.test(felt) ? "custom" : felt);

export function mount(card, api) {
  card.innerHTML = choiceBlock("felt", "Board colour");
  const swatches = card.querySelector(".swatches");
  const nameOut = card.querySelector("#felt-name");
  let built = "";
  let lastCustom = null;         // the custom colour last chosen: where the picker starts again
  let dragTimer = 0;
  let dragging = null;

  const picker = () => swatches.querySelector(".picker");
  const pick = (felt) => api.change({ felt }, () => ipc.setSetting("felt", felt));

  /** The picker starts on the last custom colour, or on the colour the board has now. */
  function startColour() {
    const view = api.settings.get();
    return lastCustom ?? view.feltPresets.find((p) => p.id === view.felt)?.hex ?? view.feltPresets[0]?.hex ?? "#000000";
  }

  function openPicker() {
    const input = picker();
    input.value = startColour();
    try { input.showPicker(); } catch { input.click(); }
  }

  /** Sends the colour being dragged, at most once per DRAG_MS. */
  function drag(value) {
    dragging = value;
    if (!dragTimer) dragTimer = setTimeout(settle, DRAG_MS);
  }
  function settle() {
    dragTimer = 0;
    if (dragging) pick(dragging);
    dragging = null;
  }

  handleRadioKeys(swatches);
  swatches.addEventListener("click", (e) => {
    const radio = e.target.closest('[role="radio"]');
    if (!radio) return;
    if (radio.dataset.value === "custom") openPicker();
    else if (api.settings.get().felt !== radio.dataset.value) pick(radio.dataset.value);
  });
  swatches.addEventListener("input", (e) => { if (e.target.matches(".picker")) drag(e.target.value); });
  // The picker is closed on this colour: the last one is sent now.
  swatches.addEventListener("change", (e) => {
    if (!e.target.matches(".picker")) return;
    clearTimeout(dragTimer);
    dragTimer = 0;
    dragging = null;
    pick(e.target.value);
  });

  function render(view) {
    const key = JSON.stringify(view.feltPresets);
    if (key !== built) {
      built = key;
      swatches.innerHTML = choicesOf(view.feltPresets)
        .map((c) => swatch(c, "dot", c === CUSTOM ? `<input type="color" class="picker" tabindex="-1" aria-hidden="true">` : ""))
        .join("");
    }
    const chosen = choiceOf(view.felt);
    showRadio(swatches, chosen);
    if (chosen === "custom") lastCustom = view.felt.toLowerCase();
    for (const radio of swatches.querySelectorAll('[role="radio"]')) {
      const dot = radio.querySelector(".dot");
      const value = radio.dataset.value;
      // A custom swatch shows no colour until it is chosen (CSS draws the colour wheel).
      const props = value === "custom" ? (chosen === "custom" && feltProperties(view.felt)) : feltProperties(value, view.feltPresets);
      if (props) dot.style.setProperty("--swatch", props["--felt"]); else dot.style.removeProperty("--swatch");
    }
    const choice = choicesOf(view.feltPresets).find((c) => c.value === chosen);
    nameOut.textContent = chosen === "custom" ? `Custom ${view.felt.toLowerCase()}` : (choice?.name ?? "");
  }
  return { render };
}

// Note paper: the colour of the paper notes. A swatch for each colour the
// backend lists (the hexes are Rust's alone), drawn with the board's own note
// code (paper.js), so each shows the paper in this window's light or dark
// theme.

import { paperProperties } from "../../../paper.js";
import { mountChoice } from "./swatches.js";

export function mount(card, api) {
  return mountChoice(card, api, {
    id: "paper",
    title: "Note paper",
    shape: "note-chip",
    key: "paper",
    presets: "paperPresets",
    paint(chip, preset, view) {
      const props = paperProperties(preset.id, view.paperPresets);
      if (props) chip.style.setProperty("--swatch", props["--note-paper"]); else chip.style.removeProperty("--swatch");
    },
  });
}

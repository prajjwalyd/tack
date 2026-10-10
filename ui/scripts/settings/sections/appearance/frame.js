// Frame: the finish of the board's metal rails. A round piece of each finish
// the backend lists, lit from above, drawn from the board's own frame tones
// (finish.js, from frame.js), so each swatch is the metal the rails will be,
// in this window's light or dark theme.

import * as ipc from "../../../ipc.js";
import { finishSwatch } from "./finish.js";
import { mountChoice } from "./swatches.js";

export function mount(card, api) {
  return mountChoice(card, api, {
    id: "frame",
    title: "Frame",
    shape: "metal",
    key: "frame",
    presets: "framePresets",
    set: ipc.setFrame,
    paint(disc, preset) {
      for (const [name, value] of Object.entries(finishSwatch(preset.id) || {})) disc.style.setProperty(name, value);
    },
  });
}

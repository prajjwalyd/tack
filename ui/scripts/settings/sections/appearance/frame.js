// Frame: the finish of the board's metal rails. A small picture of the board's
// corner for each finish the backend lists, drawn from the board's own frame
// code (frame.js): the rails around the felt colour the board has now.

import * as ipc from "../../../ipc.js";
import { frameProperties } from "../../../frame.js";
import { feltProperties } from "../../../theme.js";
import { mountChoice } from "./swatches.js";

/** The properties the picture draws with: the finish's rails (not the caps) and the felt. */
function chipProperties(finish, view) {
  const props = {};
  for (const [name, value] of Object.entries(frameProperties(finish) || {})) {
    if (name.startsWith("--rail-")) props[name] = value;
  }
  const felt = feltProperties(view.felt, view.feltPresets);
  if (felt) props["--felt"] = felt["--felt"];
  return props;
}

export function mount(card, api) {
  return mountChoice(card, api, {
    id: "frame",
    title: "Frame",
    shape: "chip",
    key: "frame",
    presets: "framePresets",
    set: ipc.setFrame,
    paint(chip, preset, view) {
      for (const [name, value] of Object.entries(chipProperties(preset.id, view))) chip.style.setProperty(name, value);
    },
  });
}

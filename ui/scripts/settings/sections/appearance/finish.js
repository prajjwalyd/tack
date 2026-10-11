// The picture of a frame finish on the Settings page: a round piece of that
// metal, lit from above, drawn from the finish's own tones (FINISHES in
// frame.js, the board's frame code), so the swatch is the metal the board's
// rails will be and no colour is written down twice.
//
// The disc is the bottom rail's tones turned into a vertical gradient (the
// lit edge at the top, the shaded one at the bottom), with a soft highlight
// at the top left and a few bands across it for the brushed grain. Each tone
// has a light and a dark-theme colour, joined with light-dark() as frame.js
// does. The check mark is whichever of white and near-black reads better on
// the face of the metal. Pure (it only imports frame.js's data and colour
// arithmetic), so the tests can check it.

import { contrast, rgb } from "../../../color.js";
import { FINISHES } from "../../../frame.js";

/** The bottom rail's tones, in order: the lit hairline, two bevel steps, the face, and two to the shaded edge. */
const STOPS = ["0%", "12%", "30%", "56%", "84%", "100%"];
const FACE = 3;

const INK = { light: "#ffffff", dark: "#1b1b1b" };

/** What the highlight and the grain are laid over the metal with, in white (a light and a dark-theme alpha). */
const SHINE = { light: 0.5, dark: 0.3 };
const GRAIN = { light: [0.34, 0.22, 0.3], dark: [0.2, 0.12, 0.18] };

const pair = (light, dark) => `light-dark(${light}, ${dark})`;
const white = (a) => `rgba(255, 255, 255, ${a})`;
const WHITE0 = white(0);

/** The check mark's colour on the face of `id`'s metal in `theme`: white or near-black, whichever has the more contrast. */
export function inkOn(id, theme) {
  const face = rgb(FINISHES[id][theme].bottom[FACE]);
  return contrast(rgb(INK.light), face) >= contrast(rgb(INK.dark), face) ? INK.light : INK.dark;
}

/**
 * The custom properties a swatch of the finish `id` is drawn with, or null
 * for one that is not a finish:
 *   --swatch       the metal, as a CSS background (layers of gradients)
 *   --swatch-ink   the check mark's colour
 *   --swatch-glow  a faint halo for the check, the opposite tone, so it holds on any part of the metal
 * @returns {Record<string, string> | null}
 */
export function finishSwatch(id) {
  if (!Object.hasOwn(FINISHES, id)) return null;
  const tones = FINISHES[id];
  const metal = STOPS.map((stop, i) => `${pair(tones.light.bottom[i], tones.dark.bottom[i])} ${stop}`).join(", ");
  const grain = (i) => pair(white(GRAIN.light[i]), white(GRAIN.dark[i]));
  return {
    "--swatch": [
      `radial-gradient(circle at 30% 18%, ${pair(white(SHINE.light), white(SHINE.dark))} 0, ${WHITE0} 52%)`,
      `conic-gradient(from 20deg, ${WHITE0}, ${grain(0)} 9%, ${WHITE0} 20%, ${grain(1)} 38%, ${WHITE0} 52%, ${grain(2)} 70%, ${WHITE0} 84%, ${grain(0)} 94%, ${WHITE0})`,
      `linear-gradient(180deg, ${metal})`,
    ].join(", "),
    "--swatch-ink": pair(inkOn(id, "light"), inkOn(id, "dark")),
    "--swatch-glow": pair(inkOn(id, "light") === INK.light ? "rgba(0, 0, 0, .55)" : "rgba(255, 255, 255, .6)", inkOn(id, "dark") === INK.light ? "rgba(0, 0, 0, .55)" : "rgba(255, 255, 255, .6)"),
  };
}

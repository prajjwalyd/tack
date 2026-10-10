// The paper of the notes: SettingsView.paper (a colour's id) turned into the
// custom properties the notes read. Only what depends on the paper's colour
// is set here: the paper itself and the link and domain colours printed on it
// (a blue that is darkened where the paper would make it too faint). The ink,
// the rules, the curl and the scrollbar, which sit on any paper, are in
// styles/notes/card.css.
//
// paperProperties is pure (and imports only colour arithmetic), so the
// settings page uses it for its swatches too: they show what the notes will
// be, in the window's light or dark theme.

import { contrast, hex, rgb } from "./color.js";

// The dark theme dims the paper a little, as it always did the white card:
// the light colour's channels times these (#fffefa -> #eeebe4).
const DIM = [0xee / 0xff, 0xeb / 0xfe, 0xe4 / 0xfa];

const LINK = "#2a4d8c";         // a link note's text and its domain: blue, so it differs from the ink
const LINK_MIN_CONTRAST = 6.5;  // darkened until it reads at least this well on the paper
const DOMAIN_ALPHA = 0.8;       // the domain is the link's colour a little softer

/** The paper of the dark theme, from the light one ([r, g, b]). */
export const dimmed = (light) => light.map((c, i) => Math.round(c * DIM[i]));

/** The link colour ([r, g, b]) for `paper`: LINK, darkened until it reads. */
export function linkOn(paper) {
  const base = rgb(LINK);
  for (let dark = 0; dark < 1; dark += 0.02) {
    const link = base.map((c) => Math.round(c * (1 - dark)));
    if (contrast(link, paper) >= LINK_MIN_CONTRAST) return link;
  }
  return [0, 0, 0];
}

/** The colours of a paper in each theme: { light, dark }, each { paper, link } as [r, g, b]. */
export function paperTones(lightHex) {
  const light = rgb(lightHex);
  const dark = dimmed(light);
  return { light: { paper: light, link: linkOn(light) }, dark: { paper: dark, link: linkOn(dark) } };
}

const rgba = ([r, g, b], alpha) => `rgba(${r}, ${g}, ${b}, ${alpha})`;
const both = (light, dark) => `light-dark(${light}, ${dark})`;

/**
 * The note's custom properties for `paper`, or null for one `presets`
 * (SettingsView.paperPresets, whose hexes are the light theme's) does not list.
 * @returns {Record<string, string> | null}
 */
export function paperProperties(paper, presets = []) {
  const preset = presets.find((p) => p.id === paper);
  if (!preset) return null;
  const { light, dark } = paperTones(preset.hex);
  return {
    "--note-paper": both(hex(light.paper), hex(dark.paper)),
    "--note-link": both(hex(light.link), hex(dark.link)),
    "--note-domain": both(rgba(light.link, DOMAIN_ALPHA), rgba(dark.link, DOMAIN_ALPHA)),
  };
}

const NAMES = ["--note-paper", "--note-link", "--note-domain"];

/**
 * Sets the paper's properties on `root` (the #board element). A paper that is
 * not listed gets the first, the default; with no presets at all nothing changes.
 */
export function applyPaper(root, paper, presets = []) {
  const props = paperProperties(paper, presets) || paperProperties(presets[0]?.id, presets);
  if (!props) return;
  for (const name of NAMES) root.style.setProperty(name, props[name]);
}

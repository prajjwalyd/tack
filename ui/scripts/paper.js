// The paper of the notes: SettingsView.paper (a colour's id) turned into the
// custom properties the notes read. Only what depends on the paper's colour
// is set here: the paper itself and the link and domain colours printed on it
// (a blue that is darkened where the paper would make it too faint). The ink,
// the rules, the curl and the scrollbar, which sit on any paper, are in
// styles/notes/card.css. The empty board's card and the one-time tip are not
// notes but hints, cut from the same paper: a shade off it, with their own
// ink (--hint-*).
//
// paperProperties is pure (and imports only colour arithmetic), so the
// settings page uses it for its swatches too: they show what the notes will
// be, in the window's light or dark theme.

import { contrast, hex, over, rgb } from "./color.js";

// The dark theme dims the paper a little, as it always did the white card:
// the light colour's channels times these (#fffefa -> #eeebe4).
const DIM = [0xee / 0xff, 0xeb / 0xfe, 0xe4 / 0xfa];

const LINK = "#2a4d8c";         // a link note's text and its domain: blue, so it differs from the ink
const LINK_MIN_CONTRAST = 6.5;  // darkened until it reads at least this well on the paper
const DOMAIN_ALPHA = 0.8;       // the domain is the link's colour a little softer

// The hint card's paper, as the note paper's channels times these: white's
// #fffefa -> #fbfaf7 in the light theme and #eeebe4 -> #e9e6df in the dark.
const HINT_OF_NOTE = {
  light: [0xfb / 0xff, 0xfa / 0xfe, 0xf7 / 0xfa],
  dark: [0xe9 / 0xee, 0xe6 / 0xeb, 0xdf / 0xe4],
};
const HINT_INK = [44, 34, 24];     // a warm brown, at two strengths:
const HINT_SOFT = 0.62;            //   the sentence, and
const HINT_STRONG = 0.82;          //   what it stresses (the keys, the setting's path)
const HINT_STRONG_MIN_CONTRAST = 7; // the stress is strengthened until it reads at least this well on the paper

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

/** Ink alpha: `alpha`, raised in steps of .01 until the ink laid on `paper` reaches `min` contrast (or is opaque). */
function inkAlpha(alpha, paper, min) {
  while (alpha < 1 && contrast(over(HINT_INK, alpha, paper), paper) < min) alpha = Math.round((alpha + 0.01) * 100) / 100;
  return alpha;
}

/**
 * The hint card's colours in each theme, { light, dark }, each { paper, soft, strong }
 * (paper as [r, g, b], the inks as alphas of HINT_INK). White gets exactly the
 * colours the hint always had; a tinted or dimmed paper gets stronger ink where
 * the same ink would read worse than it does on white (the soft ink) or under
 * 7:1 (the stress).
 */
export function hintTones(lightHex) {
  const notes = paperTones(lightHex);
  const white = paperTones("#fffefa");
  const tone = (theme) => {
    const cut = (t) => t[theme].paper.map((c, i) => Math.round(c * HINT_OF_NOTE[theme][i]));
    const paper = cut(notes), whitePaper = cut(white);
    const softFloor = contrast(over(HINT_INK, HINT_SOFT, whitePaper), whitePaper);
    return { paper, soft: inkAlpha(HINT_SOFT, paper, softFloor), strong: inkAlpha(HINT_STRONG, paper, HINT_STRONG_MIN_CONTRAST) };
  };
  return { light: tone("light"), dark: tone("dark") };
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
  const hint = hintTones(preset.hex);
  return {
    "--note-paper": both(hex(light.paper), hex(dark.paper)),
    "--note-link": both(hex(light.link), hex(dark.link)),
    "--note-domain": both(rgba(light.link, DOMAIN_ALPHA), rgba(dark.link, DOMAIN_ALPHA)),
    "--hint-paper": both(hex(hint.light.paper), hex(hint.dark.paper)),
    "--hint-ink": both(rgba(HINT_INK, hint.light.soft), rgba(HINT_INK, hint.dark.soft)),
    "--hint-ink-strong": both(rgba(HINT_INK, hint.light.strong), rgba(HINT_INK, hint.dark.strong)),
  };
}

const NAMES = ["--note-paper", "--note-link", "--note-domain", "--hint-paper", "--hint-ink", "--hint-ink-strong"];

/**
 * Sets the paper's properties on `root` (the #board element). A paper that is
 * not listed gets the first, the default; with no presets at all nothing changes.
 */
export function applyPaper(root, paper, presets = []) {
  const props = paperProperties(paper, presets) || paperProperties(presets[0]?.id, presets);
  if (!props) return;
  for (const name of NAMES) root.style.setProperty(name, props[name]);
}

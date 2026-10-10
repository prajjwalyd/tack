// Colour arithmetic for the board's themes: "#rrggbb" in and out, contrast
// the way WCAG measures it. Pure, and with no imports, so the page code
// (frame.js, paper.js) and the tests share it.

const HEX = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i;

/** [r, g, b] (0..255) of a "#rrggbb"; throws for anything else. */
export function rgb(hex) {
  const m = HEX.exec(hex);
  if (!m) throw new Error(`not a #rrggbb colour: ${hex}`);
  return [m[1], m[2], m[3]].map((h) => parseInt(h, 16));
}

/** "#rrggbb" (lowercase) of [r, g, b]; channels are rounded and kept within 0..255. */
export const hex = (channels) =>
  `#${channels.map((c) => Math.min(255, Math.max(0, Math.round(c))).toString(16).padStart(2, "0")).join("")}`;

const toLinear = (c) => { const v = c / 255; return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };

/** Relative luminance (0 black .. 1 white) of [r, g, b]. */
export const luminance = ([r, g, b]) => 0.2126 * toLinear(r) + 0.7152 * toLinear(g) + 0.0722 * toLinear(b);

/** [r, g, b] of `color` laid at `alpha` (0..1) over the opaque `under`. */
export const over = (color, alpha, under) => color.map((c, i) => c * alpha + under[i] * (1 - alpha));

/** The WCAG contrast ratio (1..21) between two opaque [r, g, b] colours. */
export function contrast(a, b) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** [hue 0..360, saturation 0..1, lightness 0..1] of [r, g, b]. */
export function hsl([r, g, b]) {
  const [R, G, B] = [r / 255, g / 255, b / 255];
  const max = Math.max(R, G, B), min = Math.min(R, G, B), l = (max + min) / 2;
  if (max === min) return [0, 0, l];
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  const h = max === R ? (G - B) / d + (G < B ? 6 : 0) : max === G ? (B - R) / d + 2 : (R - G) / d + 4;
  return [h * 60, s, l];
}

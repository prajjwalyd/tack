// The board's colour: SettingsView.felt (a preset's id, "accent" or a custom
// "#rrggbb") turned into the custom properties tokens.css defines for the
// green default. Only what depends on the felt's hue changes: the felt itself
// and the light and shade laid over it. Everything else (frame, paper, pins,
// the caption ink) stays, so the colour is clamped to what they read on:
// dark enough for the light caption ink and the white pins, and never so dark
// the felt's texture and the shadows vanish.
//
// feltProperties is pure (and imports only colour arithmetic), so the
// settings page uses it for its swatches too: they show what the board will be.

import { hex, rgb, toLinear } from "./color.js";

/** The Windows accent colour is only known to CSS (`AccentColor`), so it is darkened by a fixed mix and cannot be clamped here. */
const ACCENT_LIGHT = "color-mix(in srgb, AccentColor 42%, #0a0e12)";

// Relative luminance (0 black, 1 white) the felt must stay between, in the
// light theme; the dark theme goes a little lower.
const MAX_LUMA = 0.12;      // caption ink on it still reads at 4.5:1
const MIN_LUMA = 0.02;
const DARK_MIN_LUMA = 0.012;
const DARK_SCALE = 0.82;    // the dark theme's felt: the light one's channels x 0.82 (tokens.css's #24503f -> #1c4234)

const toByte = (l) => Math.round(255 * (l <= 0.0031308 ? l * 12.92 : 1.055 * l ** (1 / 2.4) - 0.055));
const luma = ([r, g, b]) => 0.2126 * r + 0.7152 * g + 0.0722 * b;   // linear channels

/** Brings a colour (linear channels) into [min, max] luminance, keeping its hue: darkened by scaling, lightened by adding grey. */
function fit(linear, min, max) {
  const y = luma(linear);
  const out = y > max ? linear.map((c) => c * (max / y)) : y < min ? linear.map((c) => c + (min - y)) : linear;
  return out.map((c) => Math.min(1, Math.max(0, c)));
}

/** [light, dark] "#rrggbb" for a base colour, or null if `base` is not one. */
function feltPair(base) {
  if (!/^#[0-9a-f]{6}$/i.test(base)) return null;
  const light = fit(rgb(base).map(toLinear), MIN_LUMA, MAX_LUMA).map(toByte);
  const dark = fit(light.map((b) => toLinear(b * DARK_SCALE)), DARK_MIN_LUMA, MAX_LUMA).map(toByte);
  return [hex(light), hex(dark)];
}

/** `color` at `alpha` (0..1): color-mix with transparent keeps the colour and scales the opacity. */
const veil = (color, alpha) => `color-mix(in srgb, ${color} ${+(alpha * 100).toFixed(1)}%, transparent)`;

/** The custom properties for a felt, given the light and dark CSS colours of --felt itself. */
function properties(light, dark) {
  // Both are written against var(--felt), so each theme gets its own.
  const dim = "color-mix(in srgb, var(--felt) 12%, black)";        // the felt's own shadow colour
  const sheen = "color-mix(in srgb, var(--felt) 25%, white)";      // and its light
  return {
    "--felt": `light-dark(${light}, ${dark})`,
    "--felt-light": `light-dark(${veil(sheen, 0.13)}, ${veil(sheen, 0.07)})`,
    "--felt-vignette": `light-dark(${veil(dim, 0.30)}, rgba(0, 0, 0, .36))`,
    "--felt-inner-shade": veil(dim, 0.45),
    "--tuck-shade": `light-dark(${veil(dim, 0.38)}, rgba(0, 0, 0, .48))`,
    "--board-unlit": veil(dim, 0.60),
  };
}

/**
 * The board's custom properties for `felt`, or null for one that is not a
 * preset, "accent" or "#rrggbb" (the board then keeps tokens.css's green).
 * `presets`: SettingsView.feltPresets, whose hexes are the light theme's.
 * @returns {Record<string, string> | null}
 */
export function feltProperties(felt, presets = []) {
  if (felt === "accent") return properties(ACCENT_LIGHT, `color-mix(in srgb, ${ACCENT_LIGHT} ${DARK_SCALE * 100}%, black)`);
  const base = presets.find((p) => p.id === felt)?.hex ?? felt;
  const pair = feltPair(base);
  return pair && properties(...pair);
}

const NAMES = Object.keys(properties("", ""));

/** Sets the felt's properties on `root` (the #board element); a felt that is not valid gets tokens.css's green back. */
export function applyFelt(root, felt, presets) {
  const props = feltProperties(felt, presets) || {};
  for (const name of NAMES) {
    if (name in props) root.style.setProperty(name, props[name]);
    else root.style.removeProperty(name);
  }
}

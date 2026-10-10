// What the UI tests share: the project's files as text, and the preset lists
// read out of the Rust source (it is the single source of the ids and the
// paper colours; the UI must agree with it).

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));

/** A project file's text (LF line ends), by path from the repository root. */
export const read = (path) => readFileSync(root + path, "utf8").replace(/\r\n/g, "\n");

/** The preset entries of a tack-core module: `{ id, name, hex? }` for each `Preset { ... }` line of its PRESETS array. */
export function rustPresets(file) {
  const source = read(`crates/tack-core/src/${file}`);
  const array = source.slice(source.indexOf("pub const PRESETS"), source.indexOf("];", source.indexOf("pub const PRESETS")));
  return [...array.matchAll(/id: "([^"]+)", name: "([^"]+)"(?:, hex: "([^"]+)")?/g)].map(([, id, name, hex]) => (hex ? { id, name, hex } : { id, name }));
}

/** The value of a CSS custom property (`--name: value;`) in a stylesheet; whitespace collapsed. */
export function cssValue(css, name) {
  const m = new RegExp(`${name}:\\s*([^;]+);`).exec(css);
  if (!m) throw new Error(`${name} is not defined`);
  return m[1].replace(/\s+/g, " ").trim();
}

/** CSS with numbers written one way (".30" is ".3") and spaces collapsed, for comparing what two stylesheets say. */
export const normalise = (css) => css.replace(/\s+/g, " ").replace(/(\.\d*?)0+(?=\D|$)/g, "$1").trim();

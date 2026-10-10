// Chords, as the Shortcuts section captures them. A chord travels as text:
// modifiers in the order Win, Ctrl, Alt, Shift, then the key, joined with "+",
// e.g. "Win+Alt+S"; "" is off. A chord needs Win, Ctrl or Alt, or is an
// F-key. Pure (no imports, no document), so the tests can drive it with
// plain objects shaped like a KeyboardEvent.

export const HINT = "Use Win, Ctrl or Alt with a letter, number or F-key.";

const MODS = ["Win", "Ctrl", "Alt", "Shift"];
const NAMED = {
  Space: "Space", Insert: "Insert", Delete: "Delete", Home: "Home", End: "End",
  PageUp: "PageUp", PageDown: "PageDown",
  ArrowUp: "Up", ArrowDown: "Down", ArrowLeft: "Left", ArrowRight: "Right",
};

/** A key's name in a chord, from its physical code; null if it cannot be one. */
export function keyName(code) {
  let m = /^Key([A-Z])$/.exec(code);
  if (m) return m[1];
  m = /^Digit([0-9])$/.exec(code);
  if (m) return m[1];
  m = /^F([1-9]|1[0-9]|2[0-4])$/.exec(code);
  if (m) return code;
  return NAMED[code] || null;
}

/** The modifiers held in an event, in a chord's order. */
export function heldMods(e) {
  return MODS.filter((m) => (m === "Win" ? e.metaKey : m === "Ctrl" ? e.ctrlKey : m === "Alt" ? e.altKey : e.shiftKey));
}

/** True for the press of a modifier key itself. */
export const isModifier = (e) => ["Meta", "OS", "Control", "Alt", "AltGraph", "Shift"].includes(e.key)
  || /^(Meta|OS|Control|Alt|Shift)(Left|Right)$/.test(e.code);

/** The modifiers still held when a key is released (the released one is not). */
export const heldAfterRelease = (e) => heldMods(e).filter((m) => !(e.key === "Meta" && m === "Win"));

/** True if `key` (a name from keyName) may stand alone, without a modifier. */
const isFKey = (key) => /^F\d+$/.test(key);

/**
 * What a key press means to a field that is listening:
 *   { act: "leave" }          Tab: let the focus move on
 *   { act: "held", mods }     a modifier on its own: show what is held
 *   { act: "cancel" }         Esc
 *   { act: "off" }            Backspace or Delete: turn the shortcut off
 *   { act: "hint" }           not a usable chord
 *   { act: "chord", chord }   a chord
 */
export function interpret(e) {
  const mods = heldMods(e);
  const plain = !e.ctrlKey && !e.altKey && !e.metaKey;
  if (e.key === "Tab" && plain) return { act: "leave" };
  if (isModifier(e)) return { act: "held", mods };
  if (!mods.length && e.key === "Escape") return { act: "cancel" };
  if (!mods.length && (e.key === "Backspace" || e.key === "Delete")) return { act: "off" };
  const key = keyName(e.code);
  if (!key || !(isFKey(key) || mods.some((m) => m !== "Shift"))) return { act: "hint" };
  return { act: "chord", chord: [...mods, key].join("+") };
}

/** A chord for a screen reader: "Win + Alt + S", or "off". */
export const speak = (chord) => (chord ? chord.split("+").join(" + ") : "off");

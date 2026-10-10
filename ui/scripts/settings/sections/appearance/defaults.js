// What "Reset appearance" goes back to. Each preset list the backend sends
// starts with its default (the board colour, the frame finish, the note
// paper), so the page does not carry a copy of them. Pure, with no imports.

const KEYS = [
  ["felt", "feltPresets"],
  ["frame", "framePresets"],
  ["paper", "paperPresets"],
];

/** The default of each appearance setting in `view`, as a part of a SettingsView: { felt, frame, paper } (a list that is missing leaves its setting out). */
export function defaults(view) {
  const out = {};
  for (const [key, list] of KEYS) {
    const first = view[list]?.[0]?.id;
    if (first !== undefined) out[key] = first;
  }
  return out;
}

/** True when every appearance setting is its default (and so there is nothing to reset). */
export function isDefault(view) {
  return Object.entries(defaults(view)).every(([key, value]) => view[key] === value);
}

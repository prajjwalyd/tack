// What the preview harnesses share about the Settings view: the board colours
// and speeds the backend offers. Mirrors tack_core::felt::PRESETS and
// RevealSpeed::motion_scale; the real pages get them from the backend.

export const PRESETS = [
  { id: "green", name: "Green", hex: "#24503f" },
  { id: "navy", name: "Navy", hex: "#233a5e" },
  { id: "charcoal", name: "Charcoal", hex: "#3a3d42" },
  { id: "burgundy", name: "Burgundy", hex: "#5e2a36" },
  { id: "plum", name: "Plum", hex: "#4a2f5c" },
  { id: "teal", name: "Teal", hex: "#1f5560" },
  { id: "slate", name: "Slate", hex: "#3f4f5f" },
];

/** Reveal speed -> the board's motion scale. */
export const SCALES = { instant: 0.6, fast: 0.8, normal: 1 };

/** True for what set_felt accepts: a preset id, "accent" or "#rrggbb". */
export const validFelt = (v) => v === "accent" || PRESETS.some((p) => p.id === v) || /^#[0-9a-f]{6}$/i.test(v);

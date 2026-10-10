// What the preview harnesses share about the Settings view: the board colours,
// frame finishes, note papers and speeds the backend offers. Mirrors
// tack_core::felt::PRESETS, frame::PRESETS, paper::PRESETS and
// RevealSpeed::motion_scale; the real pages get them from the backend.
// (tests/ui checks the lists against the Rust ones.)

export const PRESETS = [
  { id: "green", name: "Green", hex: "#24503f" },
  { id: "navy", name: "Navy", hex: "#233a5e" },
  { id: "charcoal", name: "Charcoal", hex: "#3b3a3e" },
  { id: "burgundy", name: "Burgundy", hex: "#682b3b" },
  { id: "plum", name: "Plum", hex: "#4a2f5c" },
  { id: "teal", name: "Teal", hex: "#1f5560" },
  { id: "slate", name: "Slate", hex: "#42586e" },
];

export const FRAME_PRESETS = [
  { id: "aluminium", name: "Aluminium" },
  { id: "graphite", name: "Graphite" },
  { id: "brass", name: "Brass" },
  { id: "silver", name: "Silver" },
];

export const PAPER_PRESETS = [
  { id: "white", name: "White", hex: "#fffefa" },
  { id: "cream", name: "Cream", hex: "#faf0d7" },
  { id: "butter", name: "Butter", hex: "#fff0a8" },
  { id: "blush", name: "Blush", hex: "#fadadd" },
  { id: "sky", name: "Sky", hex: "#d6e8fa" },
  { id: "mint", name: "Mint", hex: "#d5f0de" },
];

/** Reveal speed -> the board's motion scale. */
export const SCALES = { instant: 0.6, fast: 0.8, normal: 1 };

/** The appearance a fresh install has: each list's first (what reset_appearance restores). */
export const DEFAULT_APPEARANCE = { felt: PRESETS[0].id, frame: FRAME_PRESETS[0].id, paper: PAPER_PRESETS[0].id };

/** True for what set_frame and set_paper accept: a preset's id. */
export const validFrame = (v) => FRAME_PRESETS.some((p) => p.id === v);
export const validPaper = (v) => PAPER_PRESETS.some((p) => p.id === v);

/** True for what set_felt accepts: a preset id, "accent" or "#rrggbb". */
export const validFelt = (v) => v === "accent" || PRESETS.some((p) => p.id === v) || /^#[0-9a-f]{6}$/i.test(v);

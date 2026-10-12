import assert from "node:assert/strict";
import { test } from "node:test";

import { defaults, isDefault } from "../../ui/scripts/settings/sections/appearance/defaults.js";
import { DEFAULT_APPEARANCE, FRAME_PRESETS, PAPER_PRESETS, PRESETS, validFrame, validPaper } from "../../ui/dev/appearance-presets.js";
import { rustPresets } from "./support.js";

const view = (patch = {}) => ({
  felt: "green", feltPresets: PRESETS,
  frame: "aluminium", framePresets: FRAME_PRESETS,
  paper: "white", paperPresets: PAPER_PRESETS,
  ...patch,
});

test("the preview's lists are the Rust ones", () => {
  assert.deepEqual(PRESETS, rustPresets("felt.rs"));
  assert.deepEqual(FRAME_PRESETS, rustPresets("frame.rs"));
  assert.deepEqual(PAPER_PRESETS, rustPresets("paper.rs"));
});

test("a reset goes back to the first of each list", () => {
  assert.deepEqual(defaults(view({ felt: "plum", frame: "brass", paper: "mint" })), { felt: "green", frame: "aluminium", paper: "white" });
  assert.deepEqual(defaults(view()), DEFAULT_APPEARANCE);
});

test("a list that is missing leaves its setting out", () => {
  assert.deepEqual(defaults({ felt: "plum", feltPresets: PRESETS }), { felt: "green" });
  assert.deepEqual(defaults({}), {});
  assert.deepEqual(defaults({ framePresets: [] }), {});
});

test("there is nothing to reset when everything is the default, and each setting alone is enough to enable it", () => {
  assert.equal(isDefault(view()), true);
  assert.equal(isDefault(view({ felt: "accent" })), false);
  assert.equal(isDefault(view({ felt: "#a1b2c3" })), false);
  assert.equal(isDefault(view({ frame: "silver" })), false);
  assert.equal(isDefault(view({ paper: "sky" })), false);
  assert.equal(isDefault({}), true, "an older backend, with no lists");
});

test("the fake backend accepts only a listed frame or paper", () => {
  assert.equal(validFrame("brass"), true);
  assert.equal(validFrame("gold"), false);
  assert.equal(validPaper("sky"), true);
  assert.equal(validPaper("#fffefa"), false);
});

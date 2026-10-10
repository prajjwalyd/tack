import assert from "node:assert/strict";
import { test } from "node:test";

import { FINISHES, FINISH_IDS } from "../../ui/scripts/frame.js";
import { FRAME_PRESETS } from "../../ui/dev/appearance-presets.js";
import { contrast, finishSwatch, inkOn } from "../../ui/scripts/settings/sections/appearance/finish.js";

test("every finish the backend lists has a swatch, and nothing else does", () => {
  for (const { id } of FRAME_PRESETS) assert.ok(finishSwatch(id), id);
  assert.equal(finishSwatch("gold"), null);
  assert.equal(finishSwatch("__proto__"), null);
});

test("a swatch is drawn from its finish's own tones, in both themes", () => {
  for (const id of FINISH_IDS) {
    const { "--swatch": swatch } = finishSwatch(id);
    for (const theme of ["light", "dark"]) {
      for (const tone of FINISHES[id][theme].bottom) assert.ok(swatch.includes(tone), `${id} ${theme} ${tone}`);
    }
    assert.match(swatch, /light-dark\(/);
  }
});

test("the swatches differ from each other", () => {
  const drawn = FINISH_IDS.map((id) => finishSwatch(id)["--swatch"]);
  assert.equal(new Set(drawn).size, FINISH_IDS.length);
});

test("the check mark is readable on the face of every finish, in both themes", () => {
  for (const id of FINISH_IDS) {
    for (const theme of ["light", "dark"]) {
      const face = FINISHES[id][theme].bottom[3];
      assert.ok(contrast(inkOn(id, theme), face) >= 3, `${id} ${theme}`);
    }
  }
});

test("contrast is the WCAG ratio", () => {
  assert.equal(Math.round(contrast("#000000", "#ffffff")), 21);
  assert.equal(contrast("#777777", "#777777"), 1);
});

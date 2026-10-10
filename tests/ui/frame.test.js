import assert from "node:assert/strict";
import { test } from "node:test";

import { contrast, luminance, rgb } from "../../ui/scripts/color.js";
import { applyFrame, DEFAULT_FRAME, FINISH_IDS, FINISHES, frameProperties } from "../../ui/scripts/frame.js";
import { normalise, read, rustPresets } from "./support.js";

const shipped = JSON.parse(read("tests/ui/fixtures/aluminium-as-shipped.json"));
const PROPERTIES = ["--rail-bottom", "--rail-side-l", "--rail-side-r", "--rail-sheen", "--cap-l-light", "--cap-r-light", "--cap-l-dark", "--cap-r-dark"];

/** A stand-in for the #board element. */
const fakeRoot = () => {
  const values = new Map();
  return { values, style: { setProperty: (k, v) => values.set(k, v), removeProperty: (k) => values.delete(k) } };
};

test("the finishes are the ones the backend knows, default first", () => {
  assert.deepEqual(FINISH_IDS, rustPresets("frame.rs").map((p) => p.id));
  assert.equal(DEFAULT_FRAME, FINISH_IDS[0]);
});

test("aluminium is exactly the frame the board had before the setting", () => {
  const props = frameProperties("aluminium");
  assert.deepEqual(Object.keys(props).sort(), Object.keys(shipped).sort());
  for (const name of Object.keys(shipped)) assert.equal(normalise(props[name]), normalise(shipped[name]), name);
});

test("every finish sets the same properties, and no two finishes are alike", () => {
  const seen = new Set();
  for (const id of FINISH_IDS) {
    const props = frameProperties(id);
    assert.deepEqual(Object.keys(props), PROPERTIES, id);
    const key = JSON.stringify(props);
    assert.ok(!seen.has(key), `${id} repeats another finish`);
    seen.add(key);
  }
});

test("a finish that is not one has no properties", () => {
  for (const id of ["", "Brass", "gold", "toString", "__proto__", undefined, null, 3]) assert.equal(frameProperties(id), null, String(id));
});

test("every tone is a #rrggbb, in both themes", () => {
  for (const [id, finish] of Object.entries(FINISHES)) {
    for (const theme of ["light", "dark"]) {
      const { bottom, side, cap } = finish[theme];
      assert.equal(bottom.length, 6, `${id} ${theme} bottom`);
      assert.equal(side.length, 5, `${id} ${theme} side`);
      for (const tone of [...bottom, ...side, ...Object.values(cap)]) assert.doesNotThrow(() => rgb(tone), `${id} ${theme} ${tone}`);
    }
  }
});

test("every rail is lit from above: the lit edge brightest, the shaded edge darkest, with a real step between", () => {
  for (const [id, finish] of Object.entries(FINISHES)) {
    for (const theme of ["light", "dark"]) {
      const lum = (tones) => tones.map((t) => luminance(rgb(t)));
      const bottom = lum(finish[theme].bottom);
      const side = lum(finish[theme].side);
      // The bottom rail runs from its lit top face down to its shaded edge; a side rail from its shaded outer edge to its lit inner one.
      assert.deepEqual([...bottom].sort((a, b) => b - a), bottom, `${id} ${theme} bottom steps down`);
      assert.deepEqual([...side].sort((a, b) => a - b), side, `${id} ${theme} side steps up`);
      for (const [name, tones] of [["bottom", finish[theme].bottom], ["side", finish[theme].side]]) {
        const ratio = contrast(rgb(tones[name === "bottom" ? 0 : 4]), rgb(tones[name === "bottom" ? 5 : 0]));
        assert.ok(ratio >= 2, `${id} ${theme} ${name} is flat: ${ratio.toFixed(2)}:1 from lit edge to shaded edge`);
      }
    }
  }
});

test("the dark theme is dimmer than the light one, so a finish never glares", () => {
  const mean = (tones) => tones.reduce((sum, t) => sum + luminance(rgb(t)), 0) / tones.length;
  for (const [id, finish] of Object.entries(FINISHES)) {
    for (const rail of ["bottom", "side"]) {
      assert.ok(mean(finish.dark[rail]) < mean(finish.light[rail]), `${id} ${rail}`);
    }
  }
});

test("the brass frame is a quieter metal than the kept pin's polished brass", () => {
  const chroma = (tone) => { const c = rgb(tone); return (Math.max(...c) - Math.min(...c)) / 255; };
  const pin = chroma(/--brass:\s*(#[0-9a-f]{6})/i.exec(read("ui/styles/tokens.css"))[1]);
  for (const theme of ["light", "dark"]) {
    const tones = [...FINISHES.brass[theme].bottom, ...FINISHES.brass[theme].side];
    for (const tone of tones) assert.ok(chroma(tone) <= pin * 0.65, `${theme} ${tone} is as vivid as the pin`);
  }
});

test("the properties pair each tone as light-dark(light, dark), and the caps come in a light and a dark picture", () => {
  const props = frameProperties("graphite");
  assert.match(props["--rail-bottom"], /^linear-gradient\(180deg, light-dark\(#777f89, #7a808a\) 0,/);
  assert.match(props["--rail-side-l"], /^linear-gradient\(90deg, light-dark\(#2d3034, #2a2c30\) 0,/);
  assert.match(props["--rail-side-r"], /^linear-gradient\(270deg,/);
  for (const name of PROPERTIES.filter((n) => n.startsWith("--cap-"))) assert.match(props[name], /^url\("data:image\/svg\+xml,<svg /);
  assert.notEqual(props["--cap-l-light"], props["--cap-l-dark"]);
  assert.match(props["--cap-r-light"], /scale\(-1 1\)/);
  assert.doesNotMatch(props["--cap-l-light"], /scale\(-1 1\)/);
});

test("applyFrame sets every property on the board, and an unknown finish gets the default", () => {
  const root = fakeRoot();
  applyFrame(root, "brass");
  assert.deepEqual([...root.values.keys()], PROPERTIES);
  assert.deepEqual(Object.fromEntries(root.values), frameProperties("brass"));
  applyFrame(root, "gold");
  assert.deepEqual(Object.fromEntries(root.values), frameProperties(DEFAULT_FRAME));
});

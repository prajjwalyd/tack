import assert from "node:assert/strict";
import { test } from "node:test";

import { contrast, over, rgb } from "../../ui/scripts/color.js";
import { applyPaper, dimmed, hintTones, paperProperties, paperTones } from "../../ui/scripts/paper.js";
import { cssValue, normalise, read, rustPresets } from "./support.js";

const presets = rustPresets("paper.rs");
const tokens = read("ui/styles/tokens.css");

/** The ink as an [r, g, b] and its alpha, from tokens.css. */
const [, ir, ig, ib, ia] = /rgba\((\d+), (\d+), (\d+), ([\d.]+)\)/.exec(cssValue(tokens, "--note-ink")).map(Number);
const INK = { colour: [ir, ig, ib], alpha: ia };

/** Contrast of a note's ink on `paper` ([r, g, b]), the ink laid over it at its alpha. */
const inkOn = (paper) => contrast(over(INK.colour, INK.alpha, paper), paper);

const fakeRoot = () => {
  const values = new Map();
  return { values, style: { setProperty: (k, v) => values.set(k, v) } };
};

test("the paper presets are read out of the Rust list", () => {
  assert.deepEqual(presets.map((p) => p.id), ["white", "cream", "butter", "blush", "sky", "mint"]);
});

test("white is exactly the card the notes had before the setting", () => {
  const props = paperProperties("white", presets);
  assert.equal(props["--note-paper"], "light-dark(#fffefa, #eeebe4)");
  assert.equal(props["--note-link"], "light-dark(#2a4d8c, #2a4d8c)");
  assert.equal(props["--note-domain"], "light-dark(rgba(42, 77, 140, 0.8), rgba(42, 77, 140, 0.8))");
});

test("the dark theme dims every paper the way it dims the white card", () => {
  assert.deepEqual(dimmed(rgb("#fffefa")), rgb("#eeebe4"));
  for (const p of presets) {
    const { light, dark } = paperTones(p.hex);
    assert.deepEqual(light.paper, rgb(p.hex));
    assert.ok(dark.paper.every((c, i) => c < light.paper[i]), p.id);
  }
});

test("the ink reads on every paper, in both themes, at 7:1 or better", () => {
  for (const p of presets) {
    const { light, dark } = paperTones(p.hex);
    assert.ok(inkOn(light.paper) >= 7, `${p.id} light: ${inkOn(light.paper).toFixed(2)}`);
    assert.ok(inkOn(dark.paper) >= 7, `${p.id} dark: ${inkOn(dark.paper).toFixed(2)}`);
  }
});

test("the link colour reads on every paper, in both themes", () => {
  for (const p of presets) {
    const { light, dark } = paperTones(p.hex);
    for (const [theme, tone] of [["light", light], ["dark", dark]]) {
      assert.ok(contrast(tone.link, tone.paper) >= 6.5, `${p.id} ${theme}: ${contrast(tone.link, tone.paper).toFixed(2)}`);
    }
  }
});

test("a link keeps its blue unless the paper would make it too faint", () => {
  const { light, dark } = paperTones("#faf0d7");   // cream: fine in the light theme, darkened in the dark one
  assert.deepEqual(light.link, rgb("#2a4d8c"));
  assert.ok(dark.link.every((c, i) => c <= rgb("#2a4d8c")[i]) && dark.link.some((c, i) => c < rgb("#2a4d8c")[i]));
});

test("a paper that is not listed has no properties", () => {
  for (const id of ["", "pink", "White", "toString", undefined, 3]) assert.equal(paperProperties(id, presets), null, String(id));
  assert.equal(paperProperties("white"), null, "with no list");
});

test("applyPaper sets the note and hint properties, an unknown paper gets the default, and no list changes nothing", () => {
  const root = fakeRoot();
  applyPaper(root, "mint", presets);
  assert.deepEqual([...root.values.keys()], ["--note-paper", "--note-link", "--note-domain", "--hint-paper", "--hint-ink", "--hint-ink-strong"]);
  assert.deepEqual(Object.fromEntries(root.values), paperProperties("mint", presets));
  applyPaper(root, "pink", presets);
  assert.deepEqual(Object.fromEntries(root.values), paperProperties("white", presets));
  const empty = fakeRoot();
  applyPaper(empty, "mint", []);
  assert.equal(empty.values.size, 0);
});

test("tokens.css does not say what the paper's own colours are (paper.js does)", () => {
  for (const name of ["--note-paper", "--note-link", "--note-domain"]) assert.doesNotMatch(tokens, new RegExp(String.raw`^\s*${name}:`, "m"));
});

test("white is exactly the hint card the empty board had before it followed the paper", () => {
  const props = paperProperties("white", presets);
  assert.equal(props["--hint-paper"], "light-dark(#fbfaf7, #e9e6df)");
  assert.equal(props["--hint-ink"], "light-dark(rgba(44, 34, 24, 0.62), rgba(44, 34, 24, 0.62))");
  assert.equal(props["--hint-ink-strong"], "light-dark(rgba(44, 34, 24, 0.82), rgba(44, 34, 24, 0.82))");
});

test("tokens.css keeps the hint's white as the fallback until the paper is applied", () => {
  const props = paperProperties("white", presets);
  assert.equal(normalise(cssValue(tokens, "--hint-paper")), props["--hint-paper"]);
  // the fallback ink is written without light-dark(): the same in both themes
  for (const name of ["--hint-ink", "--hint-ink-strong"]) {
    const both = props[name].replace(/^light-dark\((.*)\)$/, "$1").split(/(?<=\)), /);
    assert.equal(both[0], both[1], name);
    assert.equal(normalise(cssValue(tokens, name)), normalise(both[0]).replace("(44, 34, 24, 0.", "(44, 34, 24, ."), name);
  }
});

test("the hint's ink reads on every paper, in both themes: the stress at 7:1, the sentence as well as on white", () => {
  const ink = [44, 34, 24];
  const onWhite = hintTones(presets[0].hex);
  for (const theme of ["light", "dark"]) {
    const floor = contrast(over(ink, onWhite[theme].soft, onWhite[theme].paper), onWhite[theme].paper);
    for (const p of presets) {
      const t = hintTones(p.hex)[theme];
      const soft = contrast(over(ink, t.soft, t.paper), t.paper), strong = contrast(over(ink, t.strong, t.paper), t.paper);
      assert.ok(strong >= 7, `${p.id} ${theme} stress: ${strong.toFixed(2)}`);
      assert.ok(soft >= floor - 1e-9, `${p.id} ${theme} sentence: ${soft.toFixed(2)} < ${floor.toFixed(2)}`);
    }
  }
});

test("the hint card is cut from the note paper: lighter than it nowhere, tinted like it", () => {
  for (const p of presets) {
    const note = paperTones(p.hex), hint = hintTones(p.hex);
    for (const theme of ["light", "dark"]) assert.ok(hint[theme].paper.every((c, i) => c <= note[theme].paper[i]), `${p.id} ${theme}`);
  }
});

import assert from "node:assert/strict";
import { test } from "node:test";

import { heldAfterRelease, interpret, keyName, speak } from "../../ui/scripts/settings/chord.js";

/** A key press shaped like a KeyboardEvent: `mods` is a string of the letters W, C, A, S. */
const press = (key, code, mods = "") => ({
  key, code,
  metaKey: mods.includes("W"), ctrlKey: mods.includes("C"), altKey: mods.includes("A"), shiftKey: mods.includes("S"),
});

test("a key is named from its physical code", () => {
  assert.equal(keyName("KeyS"), "S");
  assert.equal(keyName("Digit7"), "7");
  assert.equal(keyName("F12"), "F12");
  assert.equal(keyName("F25"), null);
  assert.equal(keyName("ArrowLeft"), "Left");
  assert.equal(keyName("Enter"), null);
});

test("Win, Ctrl or Alt with a key makes a chord, written in a fixed order", () => {
  assert.deepEqual(interpret(press("s", "KeyS", "WA")), { act: "chord", chord: "Win+Alt+S" });
  assert.deepEqual(interpret(press("k", "KeyK", "SCA")), { act: "chord", chord: "Ctrl+Alt+Shift+K" });
  assert.deepEqual(interpret(press("F9", "F9")), { act: "chord", chord: "F9" });
});

test("a key with no Win, Ctrl or Alt is not a chord, but gets a hint", () => {
  assert.deepEqual(interpret(press("s", "KeyS")), { act: "hint" });
  assert.deepEqual(interpret(press("S", "KeyS", "S")), { act: "hint" });
  assert.deepEqual(interpret(press("Enter", "Enter", "C")), { act: "hint" });
});

test("Esc cancels, Backspace and Delete turn it off, Tab leaves, a modifier shows what is held", () => {
  assert.deepEqual(interpret(press("Escape", "Escape")), { act: "cancel" });
  assert.deepEqual(interpret(press("Backspace", "Backspace")), { act: "off" });
  assert.deepEqual(interpret(press("Delete", "Delete")), { act: "off" });
  assert.deepEqual(interpret(press("Tab", "Tab")), { act: "leave" });
  assert.deepEqual(interpret(press("Control", "ControlLeft", "C")), { act: "held", mods: ["Ctrl"] });
  // With a modifier these are not the special keys.
  assert.deepEqual(interpret(press("Escape", "Escape", "C")), { act: "hint" });
  assert.deepEqual(interpret(press("Delete", "Delete", "A")), { act: "chord", chord: "Alt+Delete" });
});

test("releasing the Windows key drops it from what is held", () => {
  assert.deepEqual(heldAfterRelease(press("Meta", "MetaLeft", "WA")), ["Alt"]);
  assert.deepEqual(heldAfterRelease(press("a", "KeyA", "WA")), ["Win", "Alt"]);
});

test("a chord is spoken with spaces, and nothing is off", () => {
  assert.equal(speak("Win+Alt+S"), "Win + Alt + S");
  assert.equal(speak(""), "off");
});

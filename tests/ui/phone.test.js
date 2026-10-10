import assert from "node:assert/strict";
import { test } from "node:test";

import { describe } from "../../ui/scripts/settings/sections/phone.js";

const state = (patch = {}) => ({ on: false, netbird: "connected", serving: false, error: null, devices: [], pending: [], ...patch });

test("the card offers to set up while the phone link is off", () => {
  assert.deepEqual(describe(state()), { status: "Off", tone: "none", button: "Set up" });
});

test("the card counts the allowed devices once the board is being served", () => {
  const on = { on: true, serving: true };
  assert.equal(describe(state({ ...on, devices: [] })).status, "On · no devices yet");
  assert.equal(describe(state({ ...on, devices: [{ key: "a" }] })).status, "On · 1 device");
  assert.equal(describe(state({ ...on, devices: [{ key: "a" }, { key: "b" }] })).status, "On · 2 devices");
  assert.equal(describe(state({ ...on })).button, "Manage");
  assert.equal(describe(state({ ...on })).tone, "ok");
});

test("the card says what NetBird needs, in the phone window's words", () => {
  assert.equal(describe(state({ netbird: "missing" })).status, "NetBird isn't installed");
  assert.equal(describe(state({ on: true, netbird: "disconnected" })).status, "NetBird isn't connected");
  assert.equal(describe(state({ on: true, netbird: "disconnected" })).tone, "warn");
  // Off, NetBird is only a problem if it is not there at all.
  assert.equal(describe(state({ netbird: "disconnected" })).status, "Off");
});

test("a device asking comes first, then a failure to start", () => {
  const asking = { key: "k", name: "pixel" };
  assert.equal(describe(state({ on: true, serving: true, pending: [asking] })).status, "pixel wants to use your board");
  assert.equal(describe(state({ on: true, serving: true, pending: [asking, { ...asking, key: "j" }] })).status, "2 devices want to use your board");
  assert.equal(describe(state({ on: true, error: "Couldn't start: port 7717 is in use" })).status, "Couldn't start: port 7717 is in use");
  assert.equal(describe(state({ on: true })).status, "Getting ready…");
});

test("nothing is known until the backend answers", () => {
  assert.equal(describe(null).button, "Set up");
  assert.match(describe(null).status, /NetBird/);
});

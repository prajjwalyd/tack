import assert from "node:assert/strict";
import { test } from "node:test";

import { routeOf } from "../../ui/scripts/settings/router.js";

test("a plain link opens the list at its top", () => {
  assert.deepEqual(routeOf(""), { page: "settings", section: null });
  assert.deepEqual(routeOf("#"), { page: "settings", section: null });
});

test("#phone is the phone page, with or without the #", () => {
  assert.deepEqual(routeOf("#phone"), { page: "phone", section: null });
  assert.deepEqual(routeOf("phone"), { page: "phone", section: null });
});

test("any other id is a group of the list", () => {
  assert.deepEqual(routeOf("#shortcuts"), { page: "settings", section: "shortcuts" });
  assert.deepEqual(routeOf("appearance"), { page: "settings", section: "appearance" });
  assert.deepEqual(routeOf("#updates"), { page: "settings", section: "updates" });
  assert.deepEqual(routeOf("#%73hortcuts"), { page: "settings", section: "shortcuts" });
});

test("an id the list does not have stays a section for the page to ignore; a bad escape does not throw", () => {
  assert.deepEqual(routeOf("#nowhere"), { page: "settings", section: "nowhere" });
  assert.deepEqual(routeOf("#%E0%A4%A"), { page: "settings", section: "%E0%A4%A" });
});

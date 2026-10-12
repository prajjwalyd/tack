import assert from "node:assert/strict";
import { test } from "node:test";

import { askHtml } from "../../ui/scripts/settings/pages/phone-asks.js";
import { bodyHtml } from "../../ui/scripts/settings/pages/phone-body.js";
import { devicesHtml } from "../../ui/scripts/settings/pages/phone-devices.js";

const state = (patch = {}) => ({ on: false, netbird: "connected", serving: false, address: null, qr: null, error: null, devices: [], pending: [], ...patch });

test("a device's name and address are text, never markup, in the question", () => {
  const html = askHtml([{ key: "k<1>", name: `<img src=x onerror=alert(1)>`, fqdn: `"><b>`, ip: "100.1.1.1", code: "4821" }]);
  assert.ok(!html.includes("<img") && !html.includes("<b>\"") && !html.includes('k<1>'));
  assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt; wants to use your board/);
  assert.match(html, /Allow only if your phone shows <b>4821<\/b>/);
});

test("Allow starts disabled and each request is its own card", () => {
  const html = askHtml([{ key: "a", name: "pixel", fqdn: "", ip: "", code: "1" }, { key: "b", name: "ipad", fqdn: "", ip: "", code: "2" }]);
  assert.equal(html.match(/class="ask"/g).length, 2);
  assert.equal(html.match(/data-act="allow"[^>]*disabled/g).length, 2);
  assert.ok(!/data-act="deny"[^>]*disabled/.test(html), "Don't allow is always there");
  assert.equal(askHtml([]), "");
});

test("the body follows what is missing: NetBird, the switch, the connection, then the code", () => {
  assert.match(bodyHtml(state({ netbird: "missing" })), /Get NetBird/);
  assert.match(bodyHtml(state()), /Only devices on your NetBird network can ask/);
  assert.match(bodyHtml(state({ on: true, netbird: "disconnected" })), /NetBird isn't connected on this PC/);
  assert.match(bodyHtml(state({ on: true })), /Getting ready/);
  assert.match(bodyHtml(state({ on: true, error: "Couldn't start: <x>" })), /role="alert".*Couldn&#39;t start: &lt;x&gt;/s);
});

test("the address is shown as text, with the QR code when there is one", () => {
  const serving = state({ on: true, serving: true, address: "http://my-pc.netbird.cloud:7717" });
  assert.match(bodyHtml({ ...serving, qr: "<svg></svg>" }), /Scan with your phone's camera/);
  assert.match(bodyHtml(serving), /Open this on your phone/);
  assert.match(bodyHtml({ ...serving, address: `x"><i>` }), /x&quot;&gt;&lt;i&gt;/);
});

test("the devices list names each device with a way to remove it, and is empty with none", () => {
  assert.equal(devicesHtml([]), "");
  const html = devicesHtml([{ key: "k1", name: "pixel", approvedAt: Date.now() }, { key: "k2", name: "<b>", approvedAt: Date.now() - 3 * 3600000 }]);
  assert.match(html, /Allowed just now/);
  assert.match(html, /Allowed 3 hours ago/);
  assert.match(html, /aria-label="Remove pixel"/);
  assert.match(html, /&lt;b&gt;/);
});

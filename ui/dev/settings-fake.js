// The Settings window's preview (settings-preview.html): a fake
// window.__TAURI__ answering its commands and sending settings:changed and
// updates:status, so every state can be tried in a browser. URL parameters:
//   ?updates=idle|checking|upToDate|available|downloading|installing|error
//                       the update state to start in (default upToDate)
//   ?felt=navy|accent|%23aabbcc   ?finish=graphite   ?paper=blush   ?speed=instant|fast|normal   the starting settings
//   ?phone=off|on|noDevices|missing|disconnected|asking|error   the phone card's state (default off)
//   ?theme=light|dark   force the theme
//   ?frame=520x640      lay the page out at that size (the window's) whatever the browser's
// The panel at the right (wide browser windows only) switches the update
// state, the phone card's state and what the next check finds;
// window.__fake does the same from the console (also .section("shortcuts"),
// what the tray sends an open window). A change made in the page really
// changes the fake settings, so the board preview (preview.html) shows what
// the page would do.
// Must load before ../scripts/settings/main.js.

import { DEFAULT_APPEARANCE, FRAME_PRESETS, PAPER_PRESETS, PRESETS, SCALES, validFelt, validFrame, validPaper } from "./appearance-presets.js";

const params = new URLSearchParams(location.search);
const theme = params.get("theme");
if (theme === "light" || theme === "dark") document.documentElement.dataset.theme = theme;
const frame = /^(\d+)x(\d+)$/.exec(params.get("frame") || "");
if (frame) Object.assign(document.documentElement.style, { width: `${frame[1]}px`, height: `${frame[2]}px`, overflow: "hidden" });

const settings = {
  sound: true, edgeReveal: true, autostart: false, updatesCheck: true, version: "0.2.0",
  reveal: params.get("speed") in SCALES ? params.get("speed") : "normal",
  felt: validFelt(params.get("felt")) ? params.get("felt") : DEFAULT_APPEARANCE.felt,
  feltPresets: PRESETS,
  frame: validFrame(params.get("finish")) ? params.get("finish") : DEFAULT_APPEARANCE.frame,
  framePresets: FRAME_PRESETS,
  paper: validPaper(params.get("paper")) ? params.get("paper") : DEFAULT_APPEARANCE.paper,
  paperPresets: PAPER_PRESETS,
};
const copies = (list) => list.map((p) => ({ ...p }));
const view = () => ({
  ...settings,
  feltPresets: copies(settings.feltPresets),
  framePresets: copies(settings.framePresets),
  paperPresets: copies(settings.paperPresets),
  motionScale: SCALES[settings.reveal],
});

// The release notes are the update server's text: this one has markup in it, which must stay text.
const NOTES = [
  "- A new Settings window: board colour, reveal speed and updates.",
  "- The board follows your Windows accent colour if you ask it to.",
  "- Fixes a crash when the clipboard is busy.",
  "<img src=x onerror=alert(1)> is shown as it is.",
].join("\n");
const MIN = 60000;
const BASE = { version: null, notes: null, progress: null, error: null, checkedAt: Date.now() - 5 * MIN };
const STATES = {
  idle: { state: "idle", checkedAt: null },
  checking: { state: "checking" },
  upToDate: { state: "upToDate" },
  available: { state: "available", version: "0.3.0", notes: NOTES },
  downloading: { state: "downloading", version: "0.3.0", notes: NOTES, progress: 0.42 },
  unknownSize: { state: "downloading", version: "0.3.0", notes: NOTES, progress: null },
  installing: { state: "installing", version: "0.3.0", notes: NOTES },
  error: { state: "error", error: "Couldn't reach the update server (timed out)." },
};
let status = { ...BASE, ...(STATES[params.get("updates")] || STATES.upToDate) };
let nextCheck = "available";   // what the next check finds: available | upToDate | error
let timers = [];

// The phone card: a PhoneState, as phone-link.js describes it.
const PHONES = {
  off: { on: false, netbird: "connected", devices: [] },
  on: { on: true, netbird: "connected", serving: true, devices: [{ key: "k-pixel", name: "pixel" }, { key: "k-s24", name: "galaxy-s24" }] },
  noDevices: { on: true, netbird: "connected", serving: true, devices: [] },
  missing: { on: false, netbird: "missing", devices: [] },
  disconnected: { on: true, netbird: "disconnected", devices: [] },
  asking: { on: true, netbird: "connected", serving: true, devices: [], pending: [{ key: "k-tab", name: "galaxy-tab", fqdn: "galaxy-tab.netbird.cloud", ip: "100.90.12.34", code: "4821" }] },
  error: { on: true, netbird: "connected", serving: false, error: "Couldn't start: port 7717 is in use", devices: [] },
};
const phoneView = (name) => ({ address: null, qr: null, error: null, pc: "my-pc", pending: [], serving: false, ...PHONES[name] });
let phone = phoneView(PHONES[params.get("phone")] ? params.get("phone") : "off");

const listeners = { "settings:changed": [], "updates:status": [], "phone:state": [], "settings:section": [] };
const emit = (name, payload) => listeners[name].forEach((fn) => fn({ event: name, payload }));
const setStatus = (patch) => { status = { ...status, ...patch }; emit("updates:status", { ...status }); return { ...status }; };
const later = (fn, ms) => timers.push(setTimeout(fn, ms));
const stop = () => { timers.forEach(clearTimeout); timers = []; };

// A setter: changes the fake settings, tells the page (as every window is told) and answers.
const set = (patch) => { Object.assign(settings, patch); emit("settings:changed", view()); return view(); };

function check() {
  stop();
  later(() => setStatus({ ...BASE, checkedAt: Date.now(), ...STATES[nextCheck] }), 1200);
  return setStatus({ state: "checking", error: null });
}

function install() {
  stop();
  let p = 0;
  const tick = () => {
    p = Math.min(1, p + 0.08);
    setStatus({ progress: p });
    if (p < 1) later(tick, 250); else later(() => setStatus({ state: "installing", progress: null }), 300);
  };
  later(tick, 250);
  return setStatus({ state: "downloading", progress: 0 });
}

const commands = {
  settings_state: () => view(),
  set_sound: ({ on }) => set({ sound: on }),
  set_edge_reveal: ({ on }) => set({ edgeReveal: on }),
  set_autostart: ({ on }) => set({ autostart: on }),
  set_reveal: ({ speed }) => (speed in SCALES ? set({ reveal: speed }) : view()),
  set_felt: ({ value }) => (validFelt(value) ? set({ felt: value.toLowerCase() }) : view()),
  set_frame: ({ value }) => (validFrame(value) ? set({ frame: value }) : view()),
  set_paper: ({ value }) => (validPaper(value) ? set({ paper: value }) : view()),
  reset_appearance: () => set(DEFAULT_APPEARANCE),
  set_updates_check: ({ on }) => set({ updatesCheck: on }),
  open_shortcuts: () => null,
  phone_state: () => ({ ...phone }),
  open_phone_link: () => null,
  close_settings: () => {
    document.documentElement.classList.add("closed");
    return null;
  },
  update_status: () => ({ ...status }),
  check_for_updates: check,
  install_update: install,
};

window.__TAURI__ = {
  core: {
    invoke: (cmd, args = {}) => {
      console.info(`[settings-fake] ${cmd} ${JSON.stringify(args)}`);
      const fn = commands[cmd];
      return fn ? Promise.resolve(fn(args) ?? null) : Promise.reject(new Error(`unknown command ${cmd}`));
    },
  },
  event: {
    listen: (name, fn) => {
      listeners[name]?.push(fn);
      return Promise.resolve(() => {});
    },
  },
};

window.__fake = {
  /** Shows an update state: one of STATES' names. */
  updates(name) { stop(); setStatus({ ...BASE, ...STATES[name] }); },
  /** What the next check finds. */
  finds(what) { nextCheck = what; },
  set,
  /** Shows a phone state: one of PHONES' names. */
  phone(name) { phone = phoneView(name); emit("phone:state", { ...phone }); },
  /** What the tray sends a Settings window that is already open. */
  section(id) { emit("settings:section", id); },
};

// ---- the panel (after main.js has built the page, which replaces the body) ----
window.addEventListener("DOMContentLoaded", () => {
  const panel = document.createElement("aside");
  panel.id = "harness";
  panel.innerHTML = `
    <h2>Preview</h2>
    <label>Update state <select id="h-state">${Object.keys(STATES).map((k) => `<option>${k}</option>`).join("")}</select></label>
    <label>Phone card <select id="h-phone">${Object.keys(PHONES).map((k) => `<option>${k}</option>`).join("")}</select></label>
    <label>Next check finds <select id="h-finds"><option>available</option><option>upToDate</option><option>error</option></select></label>
    <label>Theme <select id="h-theme"><option value="">system</option><option>light</option><option>dark</option></select></label>`;
  document.body.append(panel);
  panel.querySelector("#h-state").value = Object.keys(STATES).includes(params.get("updates")) ? params.get("updates") : "upToDate";
  panel.querySelector("#h-state").addEventListener("change", (e) => window.__fake.updates(e.target.value));
  panel.querySelector("#h-phone").value = PHONES[params.get("phone")] ? params.get("phone") : "off";
  panel.querySelector("#h-phone").addEventListener("change", (e) => window.__fake.phone(e.target.value));
  panel.querySelector("#h-finds").addEventListener("change", (e) => window.__fake.finds(e.target.value));
  panel.querySelector("#h-theme").value = theme || "";
  panel.querySelector("#h-theme").addEventListener("change", (e) => {
    if (e.target.value) document.documentElement.dataset.theme = e.target.value; else delete document.documentElement.dataset.theme;
  });
});

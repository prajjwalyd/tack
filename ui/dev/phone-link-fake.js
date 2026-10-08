// The phone window's preview (phone-link-preview.html): a fake
// window.__TAURI__ answering its commands and sending phone:state, so every
// state can be tried in a browser. URL parameters:
//   ?state=missing|disconnected|off|serving|pending|combo|error
//                       where to start (default serving). pending: one device
//                       asking, none allowed yet (the window as it opens by
//                       itself the first time). combo: serving, two asking,
//                       two already allowed.
//   ?ask=3              after 3 s a device called "galaxy-tab" asks
//   ?theme=light|dark   force the theme
//   ?frame=420x600      lay the page out at that size whatever the browser's
// In the console, window.__fake.ask("name") and .set({ netbird: "connected" })
// poke it. The QR code is a plausible grid, not one that scans.
// Must load before ../scripts/phone-link.js.

const params = new URLSearchParams(location.search);
const theme = params.get("theme");
if (theme === "light" || theme === "dark") document.documentElement.dataset.theme = theme;
const frame = /^(\d+)x(\d+)$/.exec(params.get("frame") || "");
if (frame) Object.assign(document.documentElement.style, { width: `${frame[1]}px`, height: `${frame[2]}px`, overflow: "hidden" });

const PC = "my-pc";
const ADDRESS = `http://${PC}.netbird.cloud:7717`;
const MIN = 60000;
const now = Date.now();

const START = {
  missing: { on: false, netbird: "missing" },
  disconnected: { on: true, netbird: "disconnected" },
  off: { on: false, netbird: "connected" },
  serving: { on: true, netbird: "connected", devices: [{ key: "k-pixel", name: "pixel", approvedAt: now - 3 * 86400000 }] },
  pending: { on: true, netbird: "connected", pending: [{ key: "k-pixel", name: "pixel" }] },
  combo: {
    on: true, netbird: "connected",
    pending: [{ key: "k-pixel", name: "pixel" }, { key: "k-ipad", name: "Prajjwal's iPad" }],
    devices: [{ key: "k-s24", name: "galaxy-s24", approvedAt: now - 25 * MIN }, { key: "k-old", name: "old-oneplus-with-a-rather-long-hostname", approvedAt: now - 40 * 86400000 }],
  },
  error: { on: true, netbird: "connected", fail: true },
};
const s = { on: false, netbird: "connected", fail: false, devices: [], pending: [], ...START[params.get("state")] || START.serving };

// A grid that looks like a QR code: finder squares in three corners, noise elsewhere.
function qr() {
  const n = 25;
  let seed = 7717;
  const rnd = () => ((seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff);
  const finder = (x, y) => {
    const inBox = (ox, oy) => x >= ox && x < ox + 7 && y >= oy && y < oy + 7;
    for (const [ox, oy] of [[0, 0], [n - 7, 0], [0, n - 7]]) {
      if (inBox(ox, oy)) {
        const dx = x - ox, dy = y - oy;
        return dx === 0 || dy === 0 || dx === 6 || dy === 6 || (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4) ? 1 : 0;
      }
      if (x >= ox - 1 && x <= ox + 7 && y >= oy - 1 && y <= oy + 7) return 0;
    }
    return null;
  };
  let d = "";
  for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) {
    const f = finder(x, y);
    if (f === 1 || (f === null && rnd() < 0.5)) d += `M${x + 2} ${y + 2}h1v1h-1z`;
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${n + 4} ${n + 4}" width="${(n + 4) * 8}" height="${(n + 4) * 8}"><rect width="${n + 4}" height="${n + 4}" fill="#fff"/><path d="${d}" fill="#000"/></svg>`;
}
const QR = qr();

const view = () => {
  const serving = s.on && s.netbird === "connected" && !s.fail;
  return {
    on: s.on,
    netbird: s.netbird,
    serving,
    address: serving ? ADDRESS : null,
    qr: serving ? QR : null,
    error: s.on && s.netbird === "connected" && s.fail ? "Couldn't start: port 7717 is in use" : null,
    pc: s.netbird === "missing" ? null : PC,
    devices: s.devices.map((d) => ({ ...d })),
    pending: s.pending.map((p) => ({ ...p })),
  };
};

const listeners = [];
const emit = () => listeners.forEach((fn) => fn({ event: "phone:state", payload: view() }));

const commands = {
  phone_state: () => view(),
  set_phone: ({ on }) => { s.on = on; return view(); },
  answer_device: ({ key, allow }) => {
    const p = s.pending.find((x) => x.key === key);
    s.pending = s.pending.filter((x) => x.key !== key);
    if (p && allow) s.devices.push({ key, name: p.name, approvedAt: Date.now() });
    return view();
  },
  forget_device: ({ key }) => { s.devices = s.devices.filter((d) => d.key !== key); return view(); },
  open_netbird_download: () => null,
  close_phone_link: () => {
    document.documentElement.classList.add("closed");
    return null;
  },
};

window.__TAURI__ = {
  core: {
    invoke: (cmd, args = {}) => {
      console.info(`[phone-link-fake] ${cmd} ${JSON.stringify(args)}`);
      const fn = commands[cmd];
      return fn ? Promise.resolve(fn(args) ?? null) : Promise.reject(new Error(`unknown command ${cmd}`));
    },
  },
  event: {
    listen: (name, fn) => {
      if (name === "phone:state") listeners.push(fn);
      return Promise.resolve(() => {});
    },
  },
};

window.__fake = {
  ask(name = "galaxy-tab") { s.pending.push({ key: `k-${name}-${Date.now()}`, name }); emit(); },
  set(patch) { Object.assign(s, patch); emit(); },
};
const ask = Number(params.get("ask"));
if (ask > 0) setTimeout(() => window.__fake.ask(), ask * 1000);

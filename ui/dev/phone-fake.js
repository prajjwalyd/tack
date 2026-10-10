// The phone page's fake backend, for settings-fake.js: the PhoneState the
// phone card and the phone page show, and the commands that change it. The QR
// code is a plausible grid, not one that scans.

const PC = "my-pc";
const ADDRESS = `http://${PC}.netbird.cloud:7717`;
const MIN = 60000;
const DAY = 86400000;

// A device asking, as the backend describes it.
const asking = (name, code) => ({ key: `k-${name}`, name, fqdn: `${name}.netbird.cloud`, ip: "100.90.12.34", code });
const allowed = (name, ago) => ({ key: `k-${name}`, name, approvedAt: Date.now() - ago });

/** Where to start: `?phone=` in the preview's URL. */
const STARTS = {
  off: () => ({ on: false }),
  on: () => ({ on: true, devices: [allowed("pixel", 3 * DAY), allowed("galaxy-s24", 25 * MIN)] }),
  noDevices: () => ({ on: true }),
  missing: () => ({ on: false, netbird: "missing" }),
  disconnected: () => ({ on: true, netbird: "disconnected" }),
  asking: () => ({ on: true, pending: [asking("galaxy-tab", "4821")] }),
  combo: () => ({
    on: true,
    pending: [asking("pixel", "4821"), asking("prajjwals-ipad", "0937")],
    devices: [allowed("galaxy-s24", 25 * MIN), allowed("old-oneplus-with-a-rather-long-hostname", 40 * DAY)],
  }),
  error: () => ({ on: true, fail: true }),
};
export const PHONE_NAMES = Object.keys(STARTS);

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

/**
 * `start`: one of PHONE_NAMES. `changed(view)` is told of every change the
 * page did not ask for itself (`use`, `ask`), as phone:state would be.
 */
export function createPhone(start, changed) {
  let s;
  const use = (name) => { s = { on: false, netbird: "connected", fail: false, devices: [], pending: [], ...STARTS[name in STARTS ? name : "off"]() }; };
  use(start);

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

  return {
    /** Shows a start state. */
    use(name) { use(name); changed(view()); },
    /** A device asks. */
    ask(name = "galaxy-tab") {
      s.pending.push({ ...asking(name, String(Date.now() % 10000).padStart(4, "0")), key: `k-${name}-${Date.now()}` });
      changed(view());
    },
    commands: {
      phone_state: view,
      set_phone: ({ on }) => { s.on = on; return view(); },
      answer_device: ({ key, allow }) => {
        const p = s.pending.find((x) => x.key === key);
        s.pending = s.pending.filter((x) => x.key !== key);
        if (p && allow) s.devices.push({ key, name: p.name, approvedAt: Date.now() });
        return view();
      },
      forget_device: ({ key }) => { s.devices = s.devices.filter((d) => d.key !== key); return view(); },
      open_netbird_download: () => null,
    },
  };
}

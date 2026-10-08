// The shortcuts dialog's preview (shortcuts-preview.html): a fake
// window.__TAURI__ answering the dialog's four commands, so it can be tried
// in a browser. "Win+Alt+X" is held by "another app". URL parameters:
//   ?theme=light|dark   force the theme
//   ?inuse=1            start with the pin shortcut reported in use
//   ?frame=460x289      lay the page out at that size whatever the browser's
//                       (headless renders: Chrome's windows have a minimum width)
// Must load before ../scripts/shortcuts.js.

const params = new URLSearchParams(location.search);
const theme = params.get("theme");
if (theme === "light" || theme === "dark") document.documentElement.dataset.theme = theme;
const frame = /^(\d+)x(\d+)$/.exec(params.get("frame") || "");
if (frame) Object.assign(document.documentElement.style, { width: `${frame[1]}px`, height: `${frame[2]}px`, overflow: "hidden" });

const DEFAULTS = { toggle: "Win+Alt+S", pin: "Win+Alt+C" };
const saved = { toggle: DEFAULTS.toggle, pin: DEFAULTS.pin };
const TAKEN = new Set(["Win+Alt+X"]);
if (params.get("inuse") === "1") TAKEN.add(DEFAULTS.pin);

const status = (chord) => {
  if (!chord) return "off";
  const parts = chord.split("+");
  if (!parts.some((p) => ["Win", "Ctrl", "Alt"].includes(p)) && !/^F\d+$/.test(parts[parts.length - 1])) return "invalid";
  return TAKEN.has(chord) ? "in-use" : "ok";
};

const commands = {
  shortcuts_state: () => ({
    toggle: { chord: saved.toggle, status: status(saved.toggle) },
    pin: { chord: saved.pin, status: status(saved.pin) },
    defaults: { ...DEFAULTS },
  }),
  set_shortcuts: ({ toggle, pin }) => {
    const r = { toggle: { chord: toggle, status: status(toggle) }, pin: { chord: pin, status: status(pin) } };
    if ([r.toggle, r.pin].every((s) => s.status === "ok" || s.status === "off")) Object.assign(saved, { toggle, pin });
    return r;
  },
  pause_shortcuts: () => null,
  close_shortcuts: () => {
    document.documentElement.classList.add("closed");
    return null;
  },
};

window.__TAURI__ = {
  core: {
    invoke: (cmd, args = {}) => {
      console.info(`[shortcuts-fake] ${cmd} ${JSON.stringify(args)}`);
      const fn = commands[cmd];
      return fn ? Promise.resolve(fn(args) ?? null) : Promise.reject(new Error(`unknown command ${cmd}`));
    },
  },
  event: { listen: () => Promise.resolve(() => {}) },
};

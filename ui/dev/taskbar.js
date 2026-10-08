// A Windows 11-like taskbar for the README scenes (hero.html, demo.html,
// icon-sheet.html), built into every <div class="taskbar"> on the page. All
// of it is drawn here or in ./desktop/: generic app icons in the Fluent 3D
// style and outline system glyphs; no Microsoft logo, product icon or font
// glyph. Tack's own tray icon sits by the overflow chevron.
//
//   <div class="taskbar" data-apps="folder browser mail chat code music"
//        data-running="browser chat" data-active="chat"
//        data-time="10:42" data-date="08/10/2026" data-theme="light|dark"></div>
//
// An app token with a "/" in it is an icon path (icon-sheet.html pins Tack).
// data-search="icon" shows search as a button, as Windows does on a narrow screen.
//
// data-theme is optional: by default the bar follows the page (hero.css's
// [data-theme] or prefers-color-scheme).

// System glyphs on a 16 px grid, 1 px strokes (Fluent outline style).
const GLYPH = {
  search: `<circle cx="7" cy="7" r="4.5"/><path d="M10.4 10.4 14 14"/>`,
  chevron: `<path d="M4.5 9.75 8 6.25l3.5 3.5"/>`,
  wifi: `<path d="M1.75 7.6a8.8 8.8 0 0 1 12.5 0M3.9 9.75a5.75 5.75 0 0 1 8.2 0M6.05 11.9a2.7 2.7 0 0 1 3.9 0"/><circle cx="8" cy="13.9" r=".9" fill="currentColor" stroke="none"/>`,
  volume: `<path d="M2.5 6.25h2.2L8 3.5v9L4.7 9.75H2.5a.5.5 0 0 1-.5-.5v-2.5a.5.5 0 0 1 .5-.5Z"/><path d="M10.6 5.9a3 3 0 0 1 0 4.2M12.4 4.1a5.6 5.6 0 0 1 0 7.8"/>`,
  battery: `<rect x="1.5" y="4.75" width="11.5" height="6.5" rx="1.6"/><path d="M14.6 6.9v2.2"/><rect x="3" y="6.25" width="6.6" height="3.5" rx=".6" fill="currentColor" stroke="none"/>`,
  bell: `<path d="M8 2.25c-2.5 0-4.25 1.9-4.25 4.4v2.6L2.5 11.5h11l-1.25-2.25v-2.6c0-2.5-1.75-4.4-4.25-4.4Z"/><path d="M6.4 13.25a1.7 1.7 0 0 0 3.2 0"/>`,
};
const glyph = (name, cls = "") =>
  `<svg class="g ${cls}" viewBox="0 0 16 16" aria-hidden="true">${GLYPH[name]}</svg>`;

const base = new URL("./desktop/", import.meta.url).href;

function build(bar) {
  const d = bar.dataset;
  const list = (s) => (s || "").split(/\s+/).filter(Boolean);
  const apps = list(d.apps || "folder browser mail chat code music");
  const running = new Set(list(d.running));
  const active = d.active || "";
  if (d.theme) bar.classList.add(`tb-${d.theme}`);
  // Tack's tray icon, one per taskbar theme (data-tray="light.svg dark.svg" overrides)
  const [trayLight, trayDark] = d.tray ? list(d.tray) : [`${base}tack-tray-light.svg`, `${base}tack-tray-dark.svg`];
  const app = (name) => {
    const state = name === active ? " active" : running.has(name) ? " running" : "";
    const src = name.includes("/") ? name : `${base}${name}.svg`;   // a path: any icon, e.g. Tack's own
    return `<span class="tb-btn tb-app${state}"><img src="${src}" alt="" width="24" height="24"><i class="tb-pill"></i></span>`;
  };
  bar.innerHTML = `
    <div class="tb-center">
      <span class="tb-btn tb-start" title=""><i></i><i></i><i></i><i></i></span>
      ${d.search === "icon"
        ? `<span class="tb-btn tb-search-icon">${glyph("search")}</span>`
        : `<span class="tb-search">${glyph("search")}<span>Search</span></span>`}
      ${apps.map(app).join("")}
    </div>
    <div class="tb-right">
      <span class="tb-btn tb-chev">${glyph("chevron")}</span>
      <span class="tb-btn tb-tray"><img class="tb-on-light" src="${trayLight}" alt="" width="16" height="16"><img class="tb-on-dark" src="${trayDark}" alt="" width="16" height="16"></span>
      <span class="tb-btn tb-sys">${glyph("wifi")}${glyph("volume")}${glyph("battery")}</span>
      <span class="tb-btn tb-clock"><b>${d.time || "10:42"}</b><b>${d.date || "08/10/2026"}</b></span>
      <span class="tb-btn tb-bell">${glyph("bell")}</span>
      <span class="tb-desk"></span>
    </div>`;
}

for (const bar of document.querySelectorAll(".taskbar")) build(bar);

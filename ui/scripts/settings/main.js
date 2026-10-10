// The Settings window (settings.html), opened from the tray's "Settings...".
// Every change applies at once (there is no Save): the backend saves it and
// answers with the new SettingsView, and the board, which is another window,
// hears settings:changed and repaints itself.
//
// This file composes the page. Each part of it is a module in sections/ with
// the same shape, `mount(host, api)`, which builds its markup once and
// returns `{ render(view) }` for the SettingsView (or nothing to render).
// They render by changing attributes and text, never by rebuilding a control,
// so the keyboard focus stays where it is. The Updates section also follows
// the UpdateStatus store.
//
// A link to a section ("settings.html#shortcuts", from the tray) scrolls to it
// and moves the keyboard focus to its heading; a window that is already open
// is sent the same thing as settings:section. Esc closes the window.

import * as ipc from "../ipc.js";
import { $, announcer, backendReady, blockBrowserKeys } from "../dialog.js";
import { createStore } from "./store.js";
import * as appearance from "./sections/appearance.js";
import * as general from "./sections/general.js";
import * as phone from "./sections/phone.js";
import * as shortcuts from "./sections/shortcuts.js";
import * as speed from "./sections/speed.js";
import * as updates from "./sections/updates.js";

const SECTIONS = [phone, general, speed, appearance, shortcuts, updates];

/** Until the backend says otherwise (or if it does not know about updates). */
const IDLE = { state: "idle", version: null, notes: null, progress: null, error: null, checkedAt: null };

// The title stays where it is and the page scrolls under it: the window can be
// short (the board may hold the top of the screen, and the window sits below it).
document.body.innerHTML = `
  <header class="head" id="head"><h1 id="title">Settings</h1></header>
  <main class="page" id="page" aria-labelledby="title"></main>
  <div class="visually-hidden" id="live" role="status" aria-live="polite"></div>`;
const page = $("#page");
// A hairline under the title once there is something scrolled beneath it.
page.addEventListener("scroll", () => $("#head").classList.toggle("scrolled", page.scrollTop > 0), { passive: true });

const settings = createStore();
const updateStatus = createStore();
updateStatus.set(IDLE);
const announce = announcer($("#live"));

/** What the sections use: the stores, the announcer and `change`. */
const api = {
  settings,
  updates: updateStatus,
  announce,
  /**
   * Runs a setter. `optimistic` (a part of the SettingsView) shows at once;
   * the backend's answer settles it, and if there is none it is put back.
   */
  async change(optimistic, call) {
    const before = settings.get();
    settings.set({ ...before, ...optimistic });
    const view = await call();
    if (view) {
      settings.set(view);
    } else {
      settings.set(before);
      announce("Couldn't change that. Try again.");
    }
  },
};

function build() {
  for (const section of SECTIONS) {
    const host = document.createElement("section");
    page.append(host);
    const part = section.mount(host, api);
    if (part.render) settings.subscribe(part.render);
  }
}

let built = false;
let wanted = decodeURIComponent(location.hash.slice(1));   // the section to show once the page is built

/** Scrolls to a section and puts the keyboard focus on its heading. An id the page does not have does nothing. */
function showSection(id) {
  const host = [...page.querySelectorAll("[data-section]")].find((s) => s.dataset.section === id);
  if (!host) return;
  host.scrollIntoView({ block: "start" });   // scroll-margin-top (settings.css) leaves the heading clear of the edge
  host.querySelector("h2")?.focus({ preventScroll: true });
}

function goTo(id) {
  if (built) showSection(id); else wanted = id;
}

document.addEventListener("keydown", (e) => {
  // A shortcut being captured has used the key already (sections/shortcuts.js).
  if (e.key === "Escape" && !e.defaultPrevented) { e.preventDefault(); ipc.closeSettings(); return; }
  blockBrowserKeys(e);
}, true);
document.addEventListener("contextmenu", (e) => e.preventDefault());

async function load() {
  if (!backendReady()) return;
  await Promise.all([
    ipc.on(ipc.EVENTS.SETTINGS, settings.set),
    ipc.on(ipc.EVENTS.UPDATES_STATUS, updateStatus.set),
    ipc.on(ipc.EVENTS.SETTINGS_SECTION, goTo),
  ]);
  const [view, status] = await Promise.all([ipc.settingsState(), ipc.updateStatus()]);
  if (!view) {
    page.insertAdjacentHTML("beforeend", `<p class="quiet" role="alert">Couldn't load the settings. Close this window and open it again.</p>`);
    return;
  }
  settings.set(view);
  updateStatus.set(status);   // null (an older backend) keeps the idle one
  build();
  built = true;
  if (wanted) showSection(wanted);
}

// Nothing takes the focus on open unless a link asked for a section: the tray opens it by mouse and a ring would be noise.
load();

// The Settings window (settings.html), opened from the tray's "Settings...".
// Every change applies at once (there is no Save): the backend saves it and
// answers with the new SettingsView, and the board, which is another window,
// hears settings:changed and repaints itself.
//
// One window, two pages, switched in place like Windows Settings: the list and
// the phone page (pages/phone.js), which the phone card's button opens and
// which has its way back in the title ("Settings > Use on your phone"). It
// also opens on the phone page by itself when a device asks to use the board.
//
// This file composes the list. Each part of it is a module in sections/ with
// the same shape, `mount(host, api)`, which builds its markup once and
// returns `{ render(view) }` for the SettingsView (or nothing to render).
// They render by changing attributes and text, never by rebuilding a control,
// so the keyboard focus stays where it is. The Updates section also follows
// the UpdateStatus store; the phone card and the phone page the PhoneState's.
//
// A link ("settings.html#shortcuts", from the tray) goes where router.js says:
// to the phone page, or to a group of the list, scrolled to with the keyboard
// focus on its heading; a window that is already open is sent the same id as
// settings:section. Esc closes the window, Alt+Left goes back to the list.

import * as ipc from "../ipc.js";
import { $, announcer, backendReady, blockBrowserKeys } from "../dialog.js";
import { createStore } from "./store.js";
import { routeOf } from "./router.js";
import * as phonePage from "./pages/phone.js";
import * as appearance from "./sections/appearance.js";
import * as general from "./sections/general.js";
import * as phoneCard from "./sections/phone.js";
import * as shortcuts from "./sections/shortcuts.js";
import * as speed from "./sections/speed.js";
import * as updates from "./sections/updates.js";

const SECTIONS = [phoneCard, general, speed, appearance, shortcuts, updates];

/** Until the backend says otherwise (or if it does not know about updates). */
const IDLE = { state: "idle", version: null, notes: null, progress: null, error: null, checkedAt: null };

const TITLES = { settings: "Settings", phone: "Use on your phone" };

// The title stays where it is and the page scrolls under it: the window can be
// short (the board may hold the top of the screen, and the window sits below it).
document.body.innerHTML = `
  <header class="head" id="head">
    <button type="button" class="text back" id="back" aria-label="Back to Settings" hidden>Settings</button>
    <span class="crumb" id="crumb" aria-hidden="true" hidden>›</span>
    <h1 id="title" tabindex="-1">${TITLES.settings}</h1>
  </header>
  <main class="page" id="list" aria-labelledby="title"></main>
  <main class="page phone-page" id="phone-page" aria-labelledby="title" hidden></main>
  <div class="visually-hidden" id="live" role="status" aria-live="polite"></div>`;
const pages = { settings: $("#list"), phone: $("#phone-page") };
const head = $("#head");
const title = $("#title");
const back = $("#back");

/** The page showing: the one that is not hidden. */
const currentPage = () => (pages.phone.hidden ? "settings" : "phone");
/** A hairline under the title once there is something scrolled beneath it. */
const syncHead = () => head.classList.toggle("scrolled", pages[currentPage()].scrollTop > 0);
for (const page of Object.values(pages)) page.addEventListener("scroll", syncHead, { passive: true });

const settings = createStore();
const updateStatus = createStore();
updateStatus.set(IDLE);
const phone = createStore();
const announce = announcer($("#live"));

/** What the sections and the phone page use: the stores, the announcer, the page and `change`. */
const api = {
  settings,
  updates: updateStatus,
  phone,
  announce,
  /** The page showing: "settings" (the list) or "phone". */
  page: currentPage,
  /** Goes to what an id names ("phone", "shortcuts"...), as a link would. */
  go: (id) => goTo(id),
  /** The title takes the keyboard focus: what had it is gone. */
  focusTitle: () => title.focus(),
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
    pages.settings.append(host);
    const part = section.mount(host, api);
    if (part.render) settings.subscribe(part.render);
  }
  phonePage.mount(pages.phone, api);
}

let built = false;
let wanted = routeOf(location.hash);   // where to go once the page is built

/**
 * Shows a page, and for the list scrolls to a group of it. The keyboard focus
 * goes to the heading, but only if the window has it: one that opened by
 * itself (a device asking) never takes the keyboard from another app.
 */
function show({ page, section }) {
  for (const [name, el] of Object.entries(pages)) el.hidden = name !== page;
  title.textContent = TITLES[page];
  back.hidden = page === "settings";
  $("#crumb").hidden = back.hidden;
  let heading = title;
  if (page === "phone") {
    pages.phone.scrollTop = 0;
  } else {
    // An id the list does not have does nothing.
    const host = [...pages.settings.querySelectorAll("[data-section]")].find((s) => s.dataset.section === section);
    host?.scrollIntoView({ block: "start" });   // scroll-margin-top (settings.css) leaves the heading clear of the edge
    heading = host?.querySelector("h2");
  }
  syncHead();
  if (document.hasFocus()) heading?.focus({ preventScroll: true });
}

function goTo(id) {
  const route = routeOf(id);
  if (built) show(route); else wanted = route;
}

/** Back to the list, at the phone card the page was opened from. */
const toList = () => show({ page: "settings", section: "phone" });
back.addEventListener("click", toList);

document.addEventListener("keydown", (e) => {
  // A shortcut being captured has used the key already (sections/shortcuts.js).
  if (e.key === "Escape" && !e.defaultPrevented) { e.preventDefault(); ipc.closeSettings(); return; }
  if (e.altKey && e.key === "ArrowLeft" && currentPage() === "phone") { e.preventDefault(); toList(); return; }
  blockBrowserKeys(e);
}, true);
// The phone page's address is selectable, so right-click works only there.
document.addEventListener("contextmenu", (e) => { if (!e.target.closest?.(".addr")) e.preventDefault(); });

async function load() {
  if (!backendReady()) return;
  await Promise.all([
    ipc.on(ipc.EVENTS.SETTINGS, settings.set),
    ipc.on(ipc.EVENTS.UPDATES_STATUS, updateStatus.set),
    ipc.on(ipc.EVENTS.PHONE_STATE, phone.set),
    ipc.on(ipc.EVENTS.SETTINGS_SECTION, goTo),
  ]);
  const [view, status, phoneState] = await Promise.all([ipc.settingsState(), ipc.updateStatus(), ipc.phoneState()]);
  if (!view) {
    pages.settings.insertAdjacentHTML("beforeend", `<p class="quiet" role="alert">Couldn't load the settings. Close this window and open it again.</p>`);
    return;
  }
  settings.set(view);
  updateStatus.set(status);   // null (an older backend) keeps the idle one
  phone.set(phoneState);      // null (it could not say) leaves the card on its tagline
  build();
  built = true;
  show(wanted);
}

// Nothing takes the focus on open unless a link asked for a page or section: the tray opens it by mouse and a ring would be noise.
load();

// Updates: this version, the "check automatically" switch, and the state of
// the update check and install (an UpdateStatus, ipc.js). One row says what
// is happening and holds the one button that fits: check, install, or
// nothing while it works. The release notes are the update server's text, so
// they are only ever put on the page as text.

import * as ipc from "../../ipc.js";
import { when } from "../../dialog.js";
import { group, showSwitch, switchRow } from "../markup.js";

const CHECK = { label: "Check for updates", act: "check" };
const REFRESH_MS = 30000;   // "Last checked 5 minutes ago" is kept current

/**
 * What to show for a status: a headline, a line under it, the button (null:
 * none), and the download's progress (a number 0..1, "unknown", or null).
 * Pure, so the wording is easy to check.
 */
export function describe(s) {
  const checked = s.checkedAt ? `Last checked ${when(s.checkedAt)}` : "";
  switch (s.state) {
    case "checking":
      return { title: "Checking for updates…", sub: "", button: { label: "Checking…", busy: true } };
    case "upToDate":
      return { title: "Tack is up to date", sub: checked, button: CHECK };
    case "available":
      return { title: `Version ${s.version} is available`, sub: "Tack restarts to finish installing it.", button: { label: "Install", act: "install", accent: true } };
    case "downloading": {
      const known = typeof s.progress === "number";
      return {
        title: `Downloading version ${s.version}…`,
        sub: known ? `${Math.round(s.progress * 100)}%` : "",
        button: { label: "Downloading…", busy: true },
        progress: known ? s.progress : "unknown",
      };
    }
    case "installing":
      return { title: `Installing version ${s.version}…`, sub: "Tack restarts when it is done.", button: { label: "Installing…", busy: true }, progress: "unknown" };
    case "error":
      return { title: "Couldn't check for updates", sub: s.error || "Try again in a little while.", button: { label: "Try again", act: "check" } };
    default:
      return { title: "Check for updates", sub: checked, button: CHECK };
  }
}

export function mount(host, api) {
  group(host, "updates", "Updates", `
    <div class="card">
      <div class="row">
        <div class="row-text"><span class="row-title">Tack</span></div>
        <span class="row-value" id="version"></span>
      </div>
      ${switchRow("switch-updatesCheck", "Check for updates automatically", "Tack looks for a new version now and then.")}
      <div class="row update">
        <div class="row-text">
          <span class="row-title" id="update-title"></span>
          <span class="row-desc" id="update-sub"></span>
        </div>
        <button type="button" class="std" id="update-button"></button>
        <div class="meter" id="update-meter" role="progressbar" aria-label="Download progress" aria-valuemin="0" aria-valuemax="100" hidden><span class="meter-fill"></span></div>
        <details class="notes" id="update-notes" hidden>
          <summary id="update-notes-title"></summary>
          <div class="notes-body" tabindex="0" role="region" aria-labelledby="update-notes-title"></div>
        </details>
      </div>
    </div>`);
  const $ = (sel) => host.querySelector(sel);
  const title = $("#update-title"), sub = $("#update-sub"), button = $("#update-button");
  const meter = $("#update-meter"), notes = $("#update-notes"), notesBody = $(".notes-body");
  const autoSwitch = $("#switch-updatesCheck");
  let shown = null;   // the state last shown, so only a change of state is announced

  function render(status) {
    const d = describe(status);
    title.textContent = d.title;
    sub.textContent = d.sub;
    sub.hidden = !d.sub;
    button.textContent = d.button.label;
    button.dataset.act = d.button.act ?? "";
    button.classList.toggle("accent", !!d.button.accent);
    button.setAttribute("aria-disabled", String(!!d.button.busy));   // not `disabled`: the button keeps the focus
    host.querySelector(".update").setAttribute("aria-busy", String(!!d.button.busy));

    meter.hidden = d.progress == null;
    meter.classList.toggle("unknown", d.progress === "unknown");
    if (typeof d.progress === "number") {
      meter.setAttribute("aria-valuenow", String(Math.round(d.progress * 100)));
      meter.style.setProperty("--progress", String(d.progress));
    } else {
      meter.removeAttribute("aria-valuenow");
    }

    const text = status.notes?.trim() || "";
    notes.hidden = !text || !status.version;
    $("#update-notes-title").textContent = status.version ? `What's new in ${status.version}` : "";
    if (notesBody.textContent !== text) notesBody.textContent = text;

    if (shown !== null && shown !== status.state) api.announce(status.state === "error" ? `${d.title}. ${d.sub}` : d.title);
    shown = status.state;
  }

  /** Runs a command that answers with the new status; `next` is shown meanwhile. */
  async function run(next, call, failure) {
    const before = api.updates.get();
    api.updates.set({ ...before, ...next });
    const status = await call();
    if (status) {
      api.updates.set(status);
    } else {
      api.updates.set(before);
      api.announce(failure);
    }
  }

  button.addEventListener("click", () => {
    if (button.getAttribute("aria-disabled") === "true") return;
    if (button.dataset.act === "check") run({ state: "checking", error: null }, ipc.checkForUpdates, "Couldn't check for updates. Try again.");
    else if (button.dataset.act === "install") run({ state: "downloading", progress: null }, ipc.installUpdate, "Couldn't install the update. Try again.");
  });
  autoSwitch.addEventListener("click", () => {
    const on = autoSwitch.getAttribute("aria-checked") !== "true";
    api.change({ updatesCheck: on }, () => ipc.setUpdatesCheck(on));
  });

  api.updates.subscribe(render);
  setInterval(() => render(api.updates.get()), REFRESH_MS);
  return {
    render(view) {
      $("#version").textContent = `Version ${view.version}`;
      showSwitch(autoSwitch, !!view.updatesCheck);
    },
  };
}

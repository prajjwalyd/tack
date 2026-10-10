// The Settings window (settings.html), opened from the tray's "Settings...".
// Placeholder: the page is only a heading until the settings UI is built.

import { backendReady } from "../dialog.js";

if (backendReady()) {
  const heading = document.createElement("h1");
  heading.textContent = "Settings";
  document.body.append(heading);
}

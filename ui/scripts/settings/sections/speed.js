// Speed: how soon the board appears and how quickly it and new snips move.
// Three choices in a segmented radio group; the reveal dwell is the
// backend's, the animation's scale is applied by the board (motion/scale.js).

import * as ipc from "../../ipc.js";
import { group } from "../markup.js";
import { handleRadioKeys, showRadio } from "../radio.js";

const SPEEDS = [
  { value: "instant", label: "Instant" },
  { value: "normal", label: "Normal" },
  { value: "slow", label: "Slow" },
];

export function mount(host, api) {
  group(host, "speed", "Speed", `
    <div class="card block">
      <p class="row-desc" id="speed-desc">How soon the board appears, and how quickly it and new screenshots move.</p>
      <div class="seg" role="radiogroup" aria-labelledby="speed-title" aria-describedby="speed-desc">
        ${SPEEDS.map((s) => `<button type="button" class="seg-item" role="radio" aria-checked="false" tabindex="-1" data-value="${s.value}">${s.label}</button>`).join("")}
      </div>
    </div>`);
  const seg = host.querySelector(".seg");
  handleRadioKeys(seg);
  seg.addEventListener("click", (e) => {
    const radio = e.target.closest('[role="radio"]');
    if (radio && radio.getAttribute("aria-checked") !== "true") api.change({ reveal: radio.dataset.value }, () => ipc.setReveal(radio.dataset.value));
  });
  return { render: (view) => showRadio(seg, view.reveal) };
}

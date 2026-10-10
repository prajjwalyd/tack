// How fast the board moves: SettingsView.motionScale from the Reveal speed
// setting. 1 is Normal; Fast and Instant are less, and shorten the swing, the
// tuck and a new capture's flight together, so the choreography keeps its
// proportions. Scripts read `scaled()` when they start something; CSS
// durations multiply by --motion-scale (published on the root, 1 in
// tokens.css) and so follow without a script.

let scale = 1;

/** The current scale, 1 for Normal. */
export const motionScale = () => scale;

/** `ms` as the current speed plays it. */
export const scaled = (ms) => ms * scale;

/** Takes the backend's value; anything that is not a number in (0, 1] is ignored. */
export function setMotionScale(next) {
  if (!(next > 0 && next <= 1) || next === scale) return;
  scale = next;
  document.documentElement.style.setProperty("--motion-scale", String(next));
}

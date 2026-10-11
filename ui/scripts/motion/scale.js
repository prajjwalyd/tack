// How fast the board moves: SettingsView.motionScale from the Speed setting
// (Instant, Normal, Slow), 1 for Slow. The backend's timers use the same
// number (tack-core's RevealSpeed), so the board's motion and its holds
// shorten together. The board is sent the scale with every reveal, so it is
// right before the first thing animates, even if the page slept through the
// settings change.
//
// THE POLICY. Every duration, delay and timer that shapes motion or timing
// belongs to one class. A new one picks its class here and is written the way
// the class says (tests/ui/motion.test.js fails on a raw value):
//
//   A  Animation (a transition, a WAAPI animation, a swing, a slide, a glide,
//      a delay inside a choreography): scales.
//      CSS: calc(<ms> * var(--motion-scale)) (the --dur-* tokens do);
//      JS: scaled(ms).
//   B  Hold or wait after an event (the "copied" check staying up, the pin
//      landing visibly before the board goes back up): scales, but never below
//      a floor that keeps the result readable. JS: scaledMin(ms, floor).
//   C  Input thresholds and physical gesture times (press-and-hold, the
//      double-click window, drag distance, pointer polling, debounces), waits
//      on the system (an image decoding, a hidden warm-up) and sounds: never
//      scale, or the gesture would feel different. JS: fixed(ms, "why");
//      CSS: an entry in the allowlist in tests/ui/motion.test.js.
//   D  Reading time for text the user must read (the one-time tip, notices):
//      never scales, with one exception: the backend's notice peek is
//      shortened only by what the faster swing saves. JS: fixed(ms, "why").
//   E  prefers-reduced-motion keeps working: it swaps movement for plain
//      fades, and those fades are animations (A), so they scale like the rest.
//
// The same classes, with their numbers, are on RevealSpeed in tack-core.
// Dialog windows (Settings, Shortcuts, the phone link) are not the board: they
// keep their own fixed, native-feeling control timings and do not load this.

let scale = 1;

/** The current scale, 1 for Slow. */
export const motionScale = () => scale;

/** Class A: `ms` as the current speed plays it. */
export const scaled = (ms) => ms * scale;

/** Class B: `ms` as the current speed plays it, but never below `floor` (nor above `ms`: Slow is exactly `ms`). */
export const scaledMin = (ms, floor) => Math.min(ms, Math.max(ms * scale, floor));

/** Classes C and D: `ms` as it is. `why` says in a few words why it must not follow the speed. */
export const fixed = (ms, _why) => ms;   // the reason is for the reader; tests/ui/motion.test.js requires one

/** Takes the backend's value; anything that is not a number in (0, 1] is ignored. */
export function setMotionScale(next) {
  if (!(next > 0 && next <= 1) || next === scale) return;
  scale = next;
  document.documentElement.style.setProperty("--motion-scale", String(next));
}

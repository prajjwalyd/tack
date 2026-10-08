// Spring easings: a damped spring's step response sampled into a CSS linear()
// easing drives the reveal swing, the row's slides and the hover settle.
// Importing this module publishes them as CSS custom properties (tokens.css
// has cubic-bezier fallbacks for webviews without linear()).

/**
 * `response`: undamped period, s. `damping`: 0..1, lower overshoots more.
 * `settle`: fraction of travel left when the animation may end. `v0`: initial
 * speed in travels per second.
 */
export function spring(response, damping, settle = 0.003, v0 = 0) {
  const w0 = (2 * Math.PI) / response;
  const z = Math.min(damping, 0.999);
  const wd = w0 * Math.sqrt(1 - z * z);
  const x = (t) => 1 - Math.exp(-z * w0 * t) * (Math.cos(wd * t) + ((z * w0 - v0) / wd) * Math.sin(wd * t));
  const dur = Math.log(1 / settle) / (z * w0);
  const n = 40;
  const pts = [];
  for (let i = 0; i <= n; i++) pts.push(i === n ? 1 : +x((i / n) * dur).toFixed(4));
  return { easing: `linear(${pts.join(", ")})`, ms: Math.round(dur * 1000) };
}

const hasLinear = typeof CSS !== "undefined" && CSS.supports("transition-timing-function", "linear(0, 1)");
// Reveal: the board's swing down on its hinge (board.css), overshooting flat
// by a few degrees; the faint second swing is cut off.
const REVEAL = spring(0.42, 0.62, 0.012, 1.5);
// Row slides (gaps closing, reorders): no visible overshoot.
const LAYOUT = spring(0.44, 0.9);
// A print settling after hover: a whisper of bounce.
const SETTLE = spring(0.3, 0.7, 0.01);

if (hasLinear) {
  const root = document.documentElement.style;
  root.setProperty("--ease-reveal", REVEAL.easing);
  root.setProperty("--dur-reveal", `${REVEAL.ms}ms`);
  root.setProperty("--ease-layout", LAYOUT.easing);
  root.setProperty("--dur-layout", `${LAYOUT.ms}ms`);
  root.setProperty("--ease-settle", SETTLE.easing);
  root.setProperty("--dur-hover-out", `${SETTLE.ms}ms`);
}

/** How long a slide along the row takes, ms. */
export const LAYOUT_MS = hasLinear ? LAYOUT.ms : 480;
/** The row's slide easing, for WAAPI animations matching CSS ones. */
export const LAYOUT_EASING = hasLinear ? LAYOUT.easing : "cubic-bezier(.22, 1, .36, 1)";
/**
 * Ms after a reveal until the board is at rest (swing plus a couple of
 * frames). Prints pin on only then: pinning mid-swing would make the
 * compositor build the print's new layers inside a 3D-turned board.
 */
export const REVEAL_MS = (hasLinear ? REVEAL.ms : 520) + 40;

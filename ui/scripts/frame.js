// The board's frame: SettingsView.frame (a finish's id) turned into the custom
// properties the board draws its metal rails and corner caps with. A finish is
// a hand-tuned set of tones for each theme, defined here and nowhere else;
// the Rust side only knows the ids.
//
// The frame is lit from above: the bottom rail's top face is a bright
// hairline and each side rail is brightest at its inner edge (a tone list
// runs from the lit edge to the shaded one). Each tone is a light and a
// dark-theme colour, dimmer in the dark so it never glares, and goes into the
// custom properties as light-dark(), as tokens.css does for everything else.
// The corner caps are pictures, which cannot follow light-dark(): there is a
// light and a dark one of each, and board.css picks by theme.
//
// frameProperties is pure (and imports nothing), so the settings page uses it
// for its swatches too: they show the rails the board will have.

/** The finish the board has without a choice: what it has always been. */
export const DEFAULT_FRAME = "aluminium";

/*
 * The finishes (exported for the tests). A theme's tones, in hex:
 *   bottom  the bottom rail from its top face down: the hairline (the lit
 *           edge), two bevel steps, the face, and two to the shaded edge
 *   side    a side rail from its outer edge to the inner one: the shaded
 *           outer edge, a bevel, the face, a bevel, the lit inner edge
 *   cap     a corner cap: the gradient from top to bottom, the lit rim
 *           and the shaded rim
 */
export const FINISHES = {
  // Brushed aluminium, cool grey.
  aluminium: {
    light: {
      bottom: ["#ffffff", "#e4e7eb", "#d2d6db", "#bcc1c7", "#a6acb3", "#7b818a"],
      side: ["#7f858e", "#aab0b7", "#c3c8ce", "#d8dce1", "#ffffff"],
      cap: { top: "#eef0f3", bottom: "#b3b9c0", lit: "#ffffff", shaded: "#767c85" },
    },
    dark: {
      bottom: ["#e8ebee", "#b9bec4", "#a8aeb5", "#92989f", "#7c828a", "#50555c"],
      side: ["#565b62", "#80868d", "#969ca3", "#aab0b6", "#e0e3e6"],
      cap: { top: "#c6cbd0", bottom: "#858b93", lit: "#eef0f2", shaded: "#4d5259" },
    },
  },
  // Dark anodised steel.
  graphite: {
    light: {
      bottom: ["#777f89", "#696f79", "#5e646d", "#52575e", "#45494f", "#2b2e31"],
      side: ["#2d3034", "#474c52", "#565b63", "#626871", "#777f89"],
      cap: { top: "#6e757f", bottom: "#4d5158", lit: "#777f89", shaded: "#282a2e" },
    },
    dark: {
      bottom: ["#7a808a", "#5b6068", "#535860", "#4a4e55", "#3d4147", "#26282b"],
      side: ["#2a2c30", "#40444a", "#4b5058", "#585d65", "#7a808a"],
      cap: { top: "#5f656d", bottom: "#40444a", lit: "#7a808a", shaded: "#202225" },
    },
  },
  // Brushed brass: a quieter, greener gold than the kept pin's polished one.
  brass: {
    light: {
      bottom: ["#fff3cf", "#ecdca6", "#dcc78a", "#c9ae66", "#b3944a", "#7e6528"],
      side: ["#836a2c", "#b9a05a", "#cfb978", "#e3d3a0", "#fff3cf"],
      cap: { top: "#f0e2b4", bottom: "#c3a560", lit: "#fff3cf", shaded: "#8a6f30" },
    },
    dark: {
      bottom: ["#eadfbf", "#caba8d", "#c3af77", "#b89f5a", "#a58c47", "#6c5c2e"],
      side: ["#746232", "#a99049", "#bba25f", "#c4b079", "#dbd3bd"],
      cap: { top: "#cfc29e", bottom: "#b0964c", lit: "#e3ddcd", shaded: "#69592d" },
    },
  },
  // Polished steel: lighter and whiter than the aluminium, with no blue in it.
  silver: {
    light: {
      bottom: ["#ffffff", "#f2f3f4", "#e6e7e9", "#d6d8da", "#c2c4c7", "#9a9da1"],
      side: ["#9da0a4", "#c7c9cb", "#dcdddf", "#eceded", "#ffffff"],
      cap: { top: "#f8f8f9", bottom: "#cfd0d2", lit: "#ffffff", shaded: "#9a9da1" },
    },
    dark: {
      bottom: ["#eceded", "#d0d2d4", "#bfc1c4", "#a8aaae", "#94979b", "#66696d"],
      side: ["#6e7175", "#999ca0", "#adb0b3", "#c0c2c5", "#e8e9ea"],
      cap: { top: "#d8d9db", bottom: "#9fa1a5", lit: "#f2f2f3", shaded: "#626569" },
    },
  },
};

// What is the same for every finish: the brushed sheen laid over the bottom
// rail (light across it, as alphas: the shaded ends, then three bands), and
// what the cap casts on the felt (a colour and its opacity) and the hairline
// along its two flat edges.
const SHEEN = {
  light: { ends: 0.08, bands: [0.18, 0.3, 0.12] },
  dark: { ends: 0.14, bands: [0.14, 0.18, 0.06] },
};
const CAP_SHADE = {
  light: { colour: "#001a0c", opacity: 0.45, edge: 0.2 },
  dark: { colour: "#000", opacity: 0.55, edge: 0.28 },
};

const THEMES = ["light", "dark"];
const alpha = (n) => String(n).replace(/^0\./, ".");
const black = (n) => `rgba(0, 0, 0, ${alpha(n)})`;
const white = (n) => `rgba(255, 255, 255, ${alpha(n)})`;

/** One colour that is `light` in the light theme and `dark` in the dark one. */
const pair = (light, dark) => `light-dark(${light}, ${dark})`;

const bottomRail = (tones) => {
  const [hairline, a, b, face, c, edge] = tones.map(([light, dark]) => pair(light, dark));
  return `linear-gradient(180deg, ${hairline} 0, ${hairline} .5px, ${a} .5px, ${b} 1.5px, ${face} 55%, ${c} calc(100% - 1px), ${edge} calc(100% - .5px))`;
};

const sideRail = (angle, tones) => {
  const [outer, a, face, b, inner] = tones.map(([light, dark]) => pair(light, dark));
  return `linear-gradient(${angle}deg, ${outer} 0, ${outer} .5px, ${a} 1px, ${face} 45%, ${b} calc(100% - 1px), ${inner} calc(100% - .5px))`;
};

const sheen = () => {
  const ends = pair(black(SHEEN.light.ends), black(SHEEN.dark.ends));
  const band = (i) => pair(white(SHEEN.light.bands[i]), white(SHEEN.dark.bands[i]));
  return `linear-gradient(90deg, ${ends}, ${band(0)} 18%, rgba(255, 255, 255, 0) 34%, ${band(1)} 52%, rgba(255, 255, 255, 0) 70%, ${band(2)} 86%, ${ends})`;
};

/** A corner cap as a CSS image: the left one, or (`mirrored`) the right one. */
function capImage(cap, shade, mirrored) {
  const c = (colour) => `%23${colour.slice(1)}`;
  const body =
    `<path d='M7 0V2A5 5 0 0 0 12 7H14' fill='none' stroke='${c(shade.colour)}' stroke-opacity='${alpha(shade.opacity)}' stroke-width='1.1' transform='translate(0 .55)' filter='url(%23b)'/>` +
    `<path d='M0 0V2A12 12 0 0 0 12 14H14V7H12A5 5 0 0 1 7 2V0Z' fill='url(%23m)'/>` +
    `<path d='M6.75 0V2A5.25 5.25 0 0 0 12 7.25H14' fill='none' stroke='${c(cap.lit)}' stroke-width='.5'/>` +
    `<path d='M.25 0V2A11.75 11.75 0 0 0 12 13.75H14' fill='none' stroke='${c(cap.shaded)}' stroke-width='.5'/>` +
    `<path d='M0 .25H7M13.75 7V14' stroke='%23000' stroke-opacity='${alpha(shade.edge)}' stroke-width='.5'/>`;
  const drawn = mirrored ? `<g transform='translate(14 0) scale(-1 1)'>${body}</g>` : body;
  return (
    `url("data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' width='14' height='14' viewBox='0 0 14 14'>` +
    `<defs><linearGradient id='m' x1='0' y1='0' x2='.45' y2='1'><stop offset='0' stop-color='${c(cap.top)}'/><stop offset='1' stop-color='${c(cap.bottom)}'/></linearGradient>` +
    `<filter id='b' x='-50%25' y='-50%25' width='200%25' height='200%25'><feGaussianBlur stdDeviation='.45'/></filter></defs>${drawn}</svg>")`
  );
}

/** The two themes' tones of one list, as [light, dark] pairs. */
const zip = (finish, key) => finish.light[key].map((tone, i) => [tone, finish.dark[key][i]]);

/** The names of the finishes, the default first. */
export const FINISH_IDS = Object.keys(FINISHES);

/**
 * The board's custom properties for the finish `id`, or null for one that is
 * not a finish.
 * @returns {Record<string, string> | null}
 */
export function frameProperties(id) {
  if (!Object.hasOwn(FINISHES, id)) return null;
  const finish = FINISHES[id];
  const props = {
    "--rail-bottom": bottomRail(zip(finish, "bottom")),
    "--rail-side-l": sideRail(90, zip(finish, "side")),
    "--rail-side-r": sideRail(270, zip(finish, "side")),
    "--rail-sheen": sheen(),
  };
  for (const theme of THEMES) {
    props[`--cap-l-${theme}`] = capImage(finish[theme].cap, CAP_SHADE[theme], false);
    props[`--cap-r-${theme}`] = capImage(finish[theme].cap, CAP_SHADE[theme], true);
  }
  return props;
}

const NAMES = Object.keys(frameProperties(DEFAULT_FRAME));

/** Sets the frame's properties on `root` (the #board element); a finish that is not one gets the default. */
export function applyFrame(root, id) {
  const props = frameProperties(id) || frameProperties(DEFAULT_FRAME);
  for (const name of NAMES) root.style.setProperty(name, props[name]);
}

// The Speed setting must move the whole board, uniformly. This scans the
// board's stylesheets and scripts and fails on a motion duration, delay or
// timer that does not go through the speed (class A/B: --motion-scale in CSS,
// scaled() / scaledMin() in scripts) unless it says why it must not (class
// C/D: fixed(ms, "why") in scripts, an ALLOWED_CSS entry below). The classes
// are the header of ui/scripts/motion/scale.js.
//
// What it recognises, so it stays robust rather than brittle:
//   scripts  the delay of later() / setTimeout() / setInterval(), the duration
//            and delay of an .animate() call, every use of a *_MS / *_TAU /
//            *_DELAY constant, and what is written to a --dur-* property;
//   styles   the time values of transition, animation and their longhands, and
//            of every --dur-* / --delay-* property.
// A number below 5 is a ratio or a 1 ms "instant", not a duration.

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import { read } from "./support.js";

const root = fileURLToPath(new URL("../../", import.meta.url));

// ---- what is not the board's motion ---------------------------------------

/** Files that are not the board: the dialog windows keep their own fixed, native-feeling control timings and never load the board's speed (scripts/motion/scale.js). */
const NOT_THE_BOARD = [
  /^ui\/styles\/(dialog|settings|shortcuts|phone-link|switch)\.css$/,
  /^ui\/scripts\/(dialog|shortcuts|phone-link|phone-icon)\.js$/,
  /^ui\/scripts\/settings\//,
];

/** Time values in the board's styles that must NOT follow the speed (class C/D), each with its reason. */
const ALLOWED_CSS = [
  { file: "ui/styles/tokens.css", property: "--dur-press", why: "mirrors HOLD_MS, the press-and-hold gesture time: the print sinks as the hold builds, and the hold is physical" },
];

// ---- the project's files ---------------------------------------------------

function walk(dir, ext, out = []) {
  for (const entry of readdirSync(root + dir, { withFileTypes: true })) {
    const path = `${dir}/${entry.name}`;
    if (entry.isDirectory()) walk(path, ext, out);
    else if (entry.name.endsWith(ext)) out.push(path);
  }
  return out;
}

const board = (files) => files.filter((f) => !NOT_THE_BOARD.some((re) => re.test(f)));
const styles = board(walk("ui/styles", ".css"));
const scripts = board(walk("ui/scripts", ".js"));

test("the scan finds the board's files", () => {
  for (const f of ["ui/styles/tokens.css", "ui/styles/print.css", "ui/styles/pin.css", "ui/styles/motion.css"]) assert.ok(styles.includes(f), f);
  for (const f of ["ui/scripts/flight.js", "ui/scripts/print.js", "ui/scripts/reveal.js", "ui/scripts/motion/swing.js"]) assert.ok(scripts.includes(f), f);
  assert.ok(!styles.some((f) => f.includes("settings")) && !scripts.some((f) => f.includes("settings/")));
});

// ---- CSS -------------------------------------------------------------------

/** CSS with its comments blanked (same length, so offsets and line numbers hold). */
const blank = (text, re) => text.replace(re, (m) => m.replace(/[^\n]/g, " "));

/** The declarations of a stylesheet: { property, value, at } for each `property: value` outside the at-rule preludes, string and url() aware. */
function declarations(css) {
  const code = blank(css, /\/\*[\s\S]*?\*\//g);
  const out = [];
  let depth = 0, quote = "", start = 0;
  const flush = (end) => {
    const text = code.slice(start, end);
    const m = /^\s*([-\w]+)\s*:([\s\S]*)$/.exec(text);
    if (m && !/^\s*(from|to|\d+%)\s*$/.test(m[1])) out.push({ property: m[1], value: m[2].trim(), at: start + text.length - text.trimStart().length });
  };
  for (let i = 0; i < code.length; i++) {
    const c = code[i];
    if (quote) { if (c === "\\") i++; else if (c === quote) quote = ""; continue; }
    if (c === '"' || c === "'") quote = c;
    else if (c === "(") depth++;
    else if (c === ")") depth--;
    else if (depth === 0 && (c === ";" || c === "{" || c === "}")) {
      if (c === ";") flush(i);
      start = i + 1;
    }
  }
  return out;
}

const TIMED = /^(--(dur|delay)[-\w]*|transition|transition-duration|transition-delay|animation|animation-duration|animation-delay)$/;

/** The spans of `calc(...)` holding var(--motion-scale): a time inside one follows the speed. */
function scaledSpans(value) {
  const spans = [];
  for (let i = value.indexOf("calc("); i >= 0; i = value.indexOf("calc(", i + 1)) {
    let depth = 0, j = i + 4;
    for (; j < value.length; j++) {
      if (value[j] === "(") depth++;
      else if (value[j] === ")" && --depth === 0) break;
    }
    if (value.slice(i, j).includes("var(--motion-scale)")) spans.push([i, j]);
  }
  return spans;
}

/** The time values in a declaration's value that no speed reaches. */
function rawTimes(value) {
  const spans = scaledSpans(value);
  const raw = [];
  for (const m of value.matchAll(/(?<![-\w.])(\d*\.?\d+)(ms|s)(?![-\w])/g)) {
    const ms = parseFloat(m[1]) * (m[2] === "s" ? 1000 : 1);
    if (ms < 5) continue;                       // 0s, 1ms: no time to scale
    if (spans.some(([a, b]) => m.index > a && m.index < b)) continue;
    raw.push(m[0]);
  }
  return raw;
}

test("every duration and delay in the board's styles follows the speed, or says why not", () => {
  const found = [];
  for (const file of styles) {
    const css = read(file);
    for (const d of declarations(css)) {
      if (!TIMED.test(d.property)) continue;
      const raw = rawTimes(d.value);
      if (!raw.length) continue;
      if (ALLOWED_CSS.some((a) => a.file === file && a.property === d.property)) continue;
      const line = css.slice(0, d.at).split("\n").length;
      found.push(`${file}:${line}  ${d.property}: ${d.value}   (raw ${raw.join(", ")})`);
    }
  }
  assert.deepEqual(found, [], `these must be calc(<ms> * var(--motion-scale)) or a --dur-* token, or go in ALLOWED_CSS with a reason:\n  ${found.join("\n  ")}`);
});

test("the allowed CSS entries are all still there", () => {
  for (const a of ALLOWED_CSS) {
    assert.ok(a.why.length > 20, `${a.file} ${a.property} needs a reason`);
    const hit = declarations(read(a.file)).some((d) => d.property === a.property && rawTimes(d.value).length);
    assert.ok(hit, `${a.file} no longer has a raw ${a.property}: remove it from ALLOWED_CSS`);
  }
});

test("the CSS check sees what it should", () => {
  assert.deepEqual(rawTimes("opacity 160ms ease, transform calc(200ms * var(--motion-scale)) ease"), ["160ms"]);
  assert.deepEqual(rawTimes("opacity calc(1.2s * var(--motion-scale)) ease 0s"), []);
  assert.deepEqual(rawTimes("transform 1ms, opacity .3s"), [".3s"]);
  assert.deepEqual(rawTimes("scale var(--dur-soft) ease"), []);
  assert.deepEqual(rawTimes("visibility 0s linear 160ms"), ["160ms"]);
  const css = `/* a { transition: 90ms } */ .a { transition: opacity 5ms; --dur-x: 300ms; background: url("data:image/png;base64,AAAA") }`;
  assert.deepEqual(declarations(css).filter((d) => TIMED.test(d.property)).map((d) => d.property), ["transition", "--dur-x"]);
});

// ---- scripts ---------------------------------------------------------------

/**
 * The source with comments blanked and the insides of strings, templates and
 * regular expressions replaced by "_" (same length), so brackets and commas
 * in them never count. Quotes stay.
 */
function mask(src) {
  let out = "";
  let prev = "";                                 // the last significant character
  for (let i = 0; i < src.length;) {
    const c = src[i], n = src[i + 1];
    if (c === "/" && n === "/") {
      const end = src.indexOf("\n", i);
      const stop = end < 0 ? src.length : end;
      out += " ".repeat(stop - i); i = stop; continue;
    }
    if (c === "/" && n === "*") {
      const end = src.indexOf("*/", i + 2);
      const stop = end < 0 ? src.length : end + 2;
      out += src.slice(i, stop).replace(/[^\n]/g, " "); i = stop; continue;
    }
    if (c === '"' || c === "'" || c === "`") {
      let j = i + 1;
      while (j < src.length && src[j] !== c) j += src[j] === "\\" ? 2 : 1;
      out += c + "_".repeat(Math.max(0, j - i - 1)) + c;
      i = j + 1; prev = c; continue;
    }
    if (c === "/" && /[(,=:[!&|?{};]|^$/.test(prev)) {   // a regular expression
      let j = i + 1, cls = false;
      while (j < src.length && (src[j] !== "/" || cls) && src[j] !== "\n") {
        if (src[j] === "\\") j++;
        else if (src[j] === "[") cls = true;
        else if (src[j] === "]") cls = false;
        j++;
      }
      out += "/" + "_".repeat(Math.max(0, j - i - 1)) + "/";
      i = j + 1; prev = "/"; continue;
    }
    out += c;
    if (!/\s/.test(c)) prev = c;
    i++;
  }
  return out;
}

/** The index of the bracket closing the one at `open`. */
function close(code, open) {
  const pairs = { "(": ")", "[": "]", "{": "}" };
  let depth = 0;
  for (let i = open; i < code.length; i++) {
    if ("([{".includes(code[i])) depth++;
    else if (")]}".includes(code[i]) && --depth === 0) return i;
  }
  return code.length;
}

/** `text` split at its top-level commas: [{ text, at }]. */
function split(code, from, to) {
  const parts = [];
  let depth = 0, start = from;
  for (let i = from; i < to; i++) {
    if ("([{".includes(code[i])) depth++;
    else if (")]}".includes(code[i])) depth--;
    else if (code[i] === "," && depth === 0) { parts.push({ text: code.slice(start, i), at: start }); start = i + 1; }
  }
  parts.push({ text: code.slice(start, to), at: start });
  return parts;
}

const SPEED_CALLS = ["scaled", "scaledMin", "fixed"];

/** Every call of `name(` in `code`: { open, end } with the bracket positions. */
function calls(code, names, { method = false } = {}) {
  const out = [];
  const re = new RegExp(`${method ? "\\.\\s*" : "(?<![\\w$])(?<!\\.\\s*)"}(${names.join("|")})\\s*\\(`, "g");
  for (const m of code.matchAll(re)) {
    const before = code.slice(Math.max(0, m.index - 20), m.index);
    if (/function\s*\*?\s*$/.test(before)) continue;                  // a definition
    const open = m.index + m[0].length - 1;
    out.push({ name: m[1], at: m.index, open, end: close(code, open) });
  }
  return out;
}

const lineOf = (src, at) => src.slice(0, at).split("\n").length;

/** `code[from, to)` with the speed calls (scaled, scaledMin, fixed) cut out. */
function withoutSpeedCalls(code, from, to) {
  const cut = calls(code, SPEED_CALLS).filter((c) => c.at >= from && c.end < to).sort((a, b) => a.at - b.at);
  let out = "", pos = from;
  for (const c of cut) {
    if (c.at < pos) continue;                                          // nested in one already cut
    out += code.slice(pos, c.at) + " ";
    pos = c.end + 1;
  }
  return out + code.slice(pos, to);
}

/** What in `code[from, to)` looks like a raw duration: a number of 5 or more, or a SCREAMING_CASE constant. */
function rawTokens(code, from, to) {
  const rest = withoutSpeedCalls(code, from, to);
  const raw = [];
  for (const m of rest.matchAll(/(?<![\w.$])(\d+\.?\d*(?:e\d+)?|\.\d+)(?![\w.])/gi)) if (parseFloat(m[1]) >= 5) raw.push(m[0]);
  for (const m of rest.matchAll(/(?<![\w$.])[A-Z][A-Z0-9]*_[A-Z0-9_]+(?![\w$])/g)) raw.push(m[0]);
  return raw;
}

/** The problems with one script's timing, as "file:line  what". */
function scriptProblems(file, src) {
  const code = mask(src);
  const found = [];
  const flag = (at, what) => found.push(`${file}:${lineOf(src, at)}  ${what}`);

  // later(fn, ms), setTimeout(fn, ms), setInterval(fn, ms): the last argument.
  for (const c of calls(code, ["later", "setTimeout", "setInterval"])) {
    const args = split(code, c.open + 1, c.end);
    if (args.length < 2) continue;
    const last = args[args.length - 1];
    const raw = rawTokens(code, last.at, last.at + last.text.length);
    if (raw.length) flag(last.at, `${c.name}(…, ${src.slice(last.at, last.at + last.text.length).trim()})  raw ${raw.join(", ")}`);
  }

  // el.animate(keyframes, { duration, delay, endDelay }).
  for (const c of calls(code, ["animate"], { method: true })) {
    const args = split(code, c.open + 1, c.end);
    const options = args[1];
    if (!options || !options.text.trim().startsWith("{")) continue;
    const from = options.at + options.text.indexOf("{") + 1;
    const to = close(code, options.at + options.text.indexOf("{"));
    for (const prop of split(code, from, to)) {
      const m = /^\s*(duration|delay|endDelay)\s*:/.exec(prop.text);
      if (!m) continue;
      const expr = prop.at + m[0].length;
      const raw = rawTokens(code, expr, prop.at + prop.text.length);
      if (raw.length) flag(prop.at, `animate({ ${m[1]}: ${src.slice(expr, prop.at + prop.text.length).trim()} })  raw ${raw.join(", ")}`);
    }
  }

  // A *_MS / *_TAU / *_DELAY constant is the normal-speed value: only ever read inside scaled(), scaledMin() or fixed().
  const speed = calls(code, SPEED_CALLS);
  for (const m of code.matchAll(/^(?:export\s+)?const\s+([A-Z][A-Z0-9_]*_(?:MS|TAU|DELAY|WAIT|DUR))\s*=/gm)) {
    const name = m[1];
    for (const use of code.matchAll(new RegExp(`(?<![\\w$.])${name}(?![\\w$])`, "g"))) {
      if (use.index === m.index + m[0].indexOf(name)) continue;       // its definition
      if (speed.some((c) => use.index > c.open && use.index < c.end)) continue;
      flag(use.index, `${name} is a normal-speed value: read it as scaled(${name}), scaledMin(${name}, floor) or fixed(${name}, "why")`);
    }
  }

  // What is written to a --dur-* / --delay-* property.
  for (const c of calls(code, ["setProperty"], { method: true })) {
    const args = split(code, c.open + 1, c.end);
    const name = src.slice(args[0].at, args[0].at + args[0].text.length);
    if (/--(dur|delay)/.test(name) && !src.slice(c.open, c.end).includes("--motion-scale")) flag(c.at, `setProperty(${name.trim()}, …) must be calc(<ms> * var(--motion-scale))`);
  }

  // fixed(ms, "why"): the reason is a real sentence, not a placeholder.
  for (const c of calls(code, ["fixed"])) {
    const args = split(code, c.open + 1, c.end);
    const why = args[1] ? src.slice(args[1].at, args[1].at + args[1].text.length).trim() : "";
    if (!/^(["'`]).{12,}\1$/s.test(why)) flag(c.at, `fixed(…) needs a reason (a string of a few words) as its second argument`);
  }
  return found;
}

test("every timer and animation in the board's scripts follows the speed, or says why not", () => {
  const found = scripts.flatMap((file) => scriptProblems(file, read(file)));
  assert.deepEqual(found, [], `these must go through scaled() / scaledMin(), or fixed(ms, "why"):\n  ${found.join("\n  ")}`);
});

test("the script check sees what it should", () => {
  const bad = (src) => scriptProblems("x.js", src);
  assert.equal(bad(`later(fn, 300);`).length, 1);
  assert.equal(bad(`later(fn, scaled(300));`).length, 0);
  assert.equal(bad(`later(fn, pinAt + scaled(95));`).length, 0);
  assert.equal(bad(`later(fn, pinAt + 95);`).length, 1);
  assert.equal(bad(`setTimeout(() => { go("a,b"); }, 60);`).length, 1);
  assert.equal(bad(`setTimeout(() => { go(); }, fixed(60, "debounces the resize events"));`).length, 0);
  assert.equal(bad(`setTimeout(() => { go(); }, fixed(60, ""));`).length, 1);
  assert.equal(bad(`setTimeout(() => { go(); }, fixed(60));`).length, 1);
  assert.equal(bad(`el.animate(f, { duration: 230, easing: "ease" });`).length, 1);
  assert.equal(bad(`el.animate(f, { duration: state.reduced ? 1 : scaled(230), easing: "ease" });`).length, 0);
  assert.equal(bad(`el.animate(f, { duration: scaled(170), delay: 120 });`).length, 1);
  assert.equal(bad(`el.animate(f, { duration: t.total, delay: pinAt });`).length, 0);
  assert.equal(bad(`const HOLD_MS = 520;\nsetTimeout(go, HOLD_MS);`).length, 2);
  assert.equal(bad(`const HOLD_MS = 520;\nsetTimeout(go, scaled(HOLD_MS));`).length, 0);
  assert.equal(bad(`const X_MS = 520;\nconst y = x * 0.4 + 3;`).length, 0);
  assert.equal(bad(`const re = /["']\\d+/; later(fn, scaled(9)); // later(x, 900)`).length, 0);
  assert.equal(bad(`root.setProperty("--dur-layout", \`\${n}ms\`);`).length, 1);
  assert.equal(bad(`root.setProperty("--dur-layout", \`calc(\${n}ms * var(--motion-scale))\`);`).length, 0);
  assert.equal(bad(`function later(fn, ms) { setTimeout(fn, ms); }`).length, 0);
});

// ---- the speed itself ------------------------------------------------------

/** What the page's scale does, with a stand-in document. */
async function loadScale() {
  const props = {};
  globalThis.document = { documentElement: { style: { setProperty: (k, v) => { props[k] = v; } } } };
  const scale = await import(`../../ui/scripts/motion/scale.js?${Math.random()}`);
  return { ...scale, props };
}

test("Normal plays everything as written, and the others scale it", async () => {
  const s = await loadScale();
  assert.equal(s.scaled(330), 330);
  assert.equal(s.scaledMin(1000, 600), 1000);
  s.setMotionScale(0.5);
  assert.equal(s.props["--motion-scale"], "0.5");
  assert.equal(s.scaled(330), 165);
  assert.equal(s.scaledMin(1000, 600), 600, "a hold keeps its floor");
  assert.equal(s.scaledMin(1000, 300), 500);
  assert.equal(s.scaledMin(100, 600), 100, "a floor never makes it longer than Normal");
  assert.equal(s.fixed(520, "a physical gesture time"), 520);
});

test("a bad scale is ignored", async () => {
  const s = await loadScale();
  s.setMotionScale(0.65);
  for (const bad of [0, -1, 1.5, NaN, "fast", null, undefined]) s.setMotionScale(bad);
  assert.equal(s.motionScale(), 0.65);
});

/** The three scales, from the Rust source (the single source of them). */
function rustScales() {
  const source = read("crates/tack-core/src/reveal_speed.rs");
  const body = source.slice(source.indexOf("pub fn motion_scale"));
  const end = body.indexOf("\n    }\n");
  const scales = Object.fromEntries([...body.slice(0, end).matchAll(/RevealSpeed::(\w+)\s*=>\s*([\d.]+)/g)].map(([, name, n]) => [name.toLowerCase(), parseFloat(n)]));
  return scales;
}

test("the preview's speeds are the Rust ones, clearly apart, Normal exactly 1", async () => {
  const { SCALES } = await import("../../ui/dev/appearance-presets.js");
  const rust = rustScales();
  assert.deepEqual(SCALES, rust);
  assert.equal(rust.normal, 1);
  assert.ok(rust.fast / rust.instant >= 1.5 && rust.normal / rust.fast >= 1.4, "each step must be plainly felt");
});

test("the tuck is one length in the styles, the page and the backend", () => {
  const css = /--dur-tuck:\s*calc\((\d+)ms \* var\(--motion-scale\)\)/.exec(read("ui/styles/tokens.css"));
  const page = /const TUCK_MS = (\d+);/.exec(read("ui/scripts/reveal.js"));
  const rust = /pub const TUCK_MS: u64 = (\d+);/.exec(read("crates/tack-core/src/reveal_speed.rs"));
  assert.ok(css && page && rust, "all three say it");
  assert.equal(css[1], page[1]);
  assert.equal(css[1], rust[1]);
});

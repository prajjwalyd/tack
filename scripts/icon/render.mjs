// Rasterises SVGs to PNGs at exact pixel sizes in headless Chrome (or Edge),
// the way the app's WebView and Windows will see them: each SVG is drawn onto a
// canvas of its own size, so the hand-pixelled sizes come out pixel for pixel.
// Node 22+ (global WebSocket and fetch), no packages.
//
//   node render.mjs <in.svg> <size> <out.png> [<in.svg> <size> <out.png> ...]
//
// The browser: $CHROME, else Chrome or Edge in their usual Windows places.
import { spawn } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const candidates = [
  process.env.CHROME,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "C:/Program Files/Microsoft/Edge/Application/msedge.exe",
].filter(Boolean);
const exe = candidates.find((p) => existsSync(p));
if (!exe) throw new Error("no Chrome or Edge found; set CHROME to its path");

const args = process.argv.slice(2);
if (!args.length || args.length % 3) throw new Error("usage: node render.mjs <in.svg> <size> <out.png> ...");

const port = 9400 + Math.floor(Math.random() * 400);
const profile = mkdtempSync(join(tmpdir(), "tack-icon-"));
const proc = spawn(exe, [
  "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
  "--no-first-run", "--no-default-browser-check", "--force-color-profile=srgb", "about:blank",
], { stdio: "ignore" });

try {
  let tabs;
  for (let i = 0; i < 100 && !tabs; i++) {
    try { tabs = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json(); } catch { await new Promise((r) => setTimeout(r, 100)); }
  }
  const ws = new WebSocket(tabs.find((t) => t.type === "page").webSocketDebuggerUrl);
  await new Promise((r) => ws.addEventListener("open", r, { once: true }));
  let id = 0;
  const pending = new Map();
  ws.addEventListener("message", (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { const { res, rej } = pending.get(m.id); pending.delete(m.id); m.error ? rej(new Error(JSON.stringify(m.error))) : res(m.result); }
  });
  const send = (method, params = {}) => new Promise((res, rej) => { const i = ++id; pending.set(i, { res, rej }); ws.send(JSON.stringify({ id: i, method, params })); });
  for (let i = 0; i < args.length; i += 3) {
    const [src, size, out] = [args[i], +args[i + 1], args[i + 2]];
    const url = "data:image/svg+xml;base64," + readFileSync(src).toString("base64");
    const expr = `new Promise((res, rej) => { const im = new Image(); im.onload = () => { const c = document.createElement('canvas'); c.width = c.height = ${size}; c.getContext('2d').drawImage(im, 0, 0, ${size}, ${size}); res(c.toDataURL('image/png')); }; im.onerror = () => rej('cannot load ${src.replace(/'/g, "")}'); im.src = ${JSON.stringify(url)}; })`;
    const r = await send("Runtime.evaluate", { expression: expr, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails));
    writeFileSync(out, Buffer.from(r.result.value.split(",")[1], "base64"));
    console.log(`${out} (${size} px)`);
  }
  ws.close();
} finally {
  proc.kill();
  await new Promise((r) => setTimeout(r, 300));
  try { rmSync(profile, { recursive: true, force: true }); } catch {}
}

// The two sounds, synthesised with Web Audio (no sound files), quiet and
// short: a soft "tock" as a pin goes into the cork, and a small dry tick as
// one comes out. Silent when sound is off in the tray. The audio context is
// suspended while the board is tucked, so no audio thread runs in the
// background.

import { state } from "./state.js";

const LEVEL = 0.2;   // master level: present, never loud

let audio = null;
let noiseBuf = null;

function ctx() {
  if (!state.sound) return null;
  try {
    if (!audio) {
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return null;
      audio = new AC({ latencyHint: "interactive" });
    }
    if (audio.state !== "running") {
      audio.resume().catch(() => {});
      return null;
    }
    return audio;
  } catch {
    return null;
  }
}

/** Board revealed: get the audio context running before the first sound. */
export function wakeSound() {
  if (audio && audio.state === "suspended" && state.sound) audio.resume().catch(() => {});
}

/** Board tucked: suspend the audio context. */
export function sleepSound() {
  if (audio && audio.state === "running") audio.suspend().catch(() => {});
}

function noise(a) {
  if (!noiseBuf || noiseBuf.sampleRate !== a.sampleRate) {
    noiseBuf = a.createBuffer(1, Math.ceil(a.sampleRate * 0.08), a.sampleRate);
    const d = noiseBuf.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
  }
  const s = a.createBufferSource();
  s.buffer = noiseBuf;
  return s;
}

function env(g, t, peak, attack, decay) {
  g.gain.setValueAtTime(0.0001, t);
  g.gain.exponentialRampToValueAtTime(peak, t + attack);
  g.gain.exponentialRampToValueAtTime(0.0001, t + attack + decay);
}

function out(a, level) {
  const g = a.createGain();
  g.gain.value = LEVEL * level;
  g.connect(a.destination);
  return g;
}

/** Pin into cork: a muffled, woody tock. */
export function playTock() {
  const a = ctx();
  if (!a) return;
  const t = a.currentTime + 0.004;
  const o = out(a, 1);

  // The body: a short low sine, falling a little.
  const s = a.createOscillator();
  s.type = "sine";
  s.frequency.setValueAtTime(230, t);
  s.frequency.exponentialRampToValueAtTime(135, t + 0.05);
  const sg = a.createGain();
  env(sg, t, 0.9, 0.002, 0.055);
  s.connect(sg).connect(o);
  s.start(t); s.stop(t + 0.08);

  // The contact: a soft band of noise, well below anything bright.
  const n = noise(a);
  const bp = a.createBiquadFilter();
  bp.type = "bandpass"; bp.frequency.value = 900; bp.Q.value = 1.1;
  const lp = a.createBiquadFilter();
  lp.type = "lowpass"; lp.frequency.value = 2000;
  const ng = a.createGain();
  env(ng, t, 0.5, 0.001, 0.022);
  n.connect(bp).connect(lp).connect(ng).connect(o);
  n.start(t); n.stop(t + 0.04);
}

/** Pin out of cork: a small, dry upward tick. */
export function playPop() {
  const a = ctx();
  if (!a) return;
  const t = a.currentTime + 0.004;
  const o = out(a, 0.7);

  const s = a.createOscillator();
  s.type = "sine";
  s.frequency.setValueAtTime(520, t);
  s.frequency.exponentialRampToValueAtTime(880, t + 0.035);
  const sg = a.createGain();
  env(sg, t, 0.6, 0.002, 0.04);
  s.connect(sg).connect(o);
  s.start(t); s.stop(t + 0.06);

  const n = noise(a);
  const bp = a.createBiquadFilter();
  bp.type = "bandpass"; bp.frequency.value = 1800; bp.Q.value = 1.4;
  const ng = a.createGain();
  env(ng, t, 0.25, 0.001, 0.012);
  n.connect(bp).connect(ng).connect(o);
  n.start(t); n.stop(t + 0.025);
}

// The two sounds, synthesised with Web Audio (no sound files), quiet and
// short: a soft, cork-damped "tock" as a pin goes in, and a small dry tick as
// one comes out. Silent when sound is off in the tray. The audio context is
// suspended while the board is tucked, so no audio thread runs in the
// background.

import { state } from "./state.js";

const LEVEL = 0.18;  // master level: present, never loud

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

/**
 * Pin into cork: a soft, muffled tock. Three quiet layers: the press (a
 * short low thump that drops in pitch as the pin seats), the cork's body (a
 * damped resonance in the low mids, what makes it sound like cork rather
 * than wood or plastic), and the faintest touch of contact noise, filtered
 * well below anything bright.
 */
export function playTock() {
  const a = ctx();
  if (!a) return;
  const t = a.currentTime + 0.004;
  const o = out(a, 0.85);
  const soft = a.createBiquadFilter();   // the whole tock, rounded off
  soft.type = "lowpass"; soft.frequency.value = 1600; soft.Q.value = 0.5;
  soft.connect(o);

  // The press.
  const s = a.createOscillator();
  s.type = "sine";
  s.frequency.setValueAtTime(190, t);
  s.frequency.exponentialRampToValueAtTime(112, t + 0.045);
  const sg = a.createGain();
  env(sg, t, 0.85, 0.003, 0.06);
  s.connect(sg).connect(soft);
  s.start(t); s.stop(t + 0.09);

  // The cork: noise rung through a narrow resonance that dies fast.
  const n = noise(a);
  const body = a.createBiquadFilter();
  body.type = "bandpass"; body.frequency.value = 420; body.Q.value = 7;
  const bg = a.createGain();
  env(bg, t, 1.4, 0.002, 0.05);
  n.connect(body).connect(bg).connect(soft);
  n.start(t); n.stop(t + 0.07);

  // The contact.
  const c = noise(a);
  const bp = a.createBiquadFilter();
  bp.type = "bandpass"; bp.frequency.value = 1100; bp.Q.value = 0.9;
  const cg = a.createGain();
  env(cg, t, 0.18, 0.001, 0.014);
  c.connect(bp).connect(cg).connect(soft);
  c.start(t); c.stop(t + 0.03);
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

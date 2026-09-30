// 8-bit background music for the promo: NES-style square/triangle/noise
// voices, synthesised sample by sample. renderSoundtrack(seconds) returns a
// 16-bit stereo WAV buffer. Music only: the real app makes no sound while
// the text animates, so neither does the video.

const RATE = 44100;
const VOLUME = 1.0;

const midi = n => 440 * Math.pow(2, (n - 69) / 12);

// ---- Voices ------------------------------------------------------------------

function square(phase, duty) { return (phase % 1) < duty ? 1 : -1; }
function triangle(phase) { const p = phase % 1; return 4 * Math.abs(p - 0.5) - 1; }

// NES-ish noise: a 15-bit LFSR clocked at `clock` Hz.
function makeNoise() {
  let reg = 1, acc = 0, out = 1;
  return clock => {
    acc += clock / RATE;
    while (acc >= 1) {
      acc -= 1;
      const bit = (reg ^ (reg >> 1)) & 1;
      reg = (reg >> 1) | (bit << 14);
      out = reg & 1 ? 1 : -1;
    }
    return out;
  };
}

function note(buf, t, dur, freq, { wave = "square", duty = 0.5, vol = 0.2, attack = 0.004, release = 0.03, vibrato = 0 } = {}) {
  const start = Math.floor(t * RATE), len = Math.floor(dur * RATE), rel = Math.floor(release * RATE);
  let phase = 0;
  for (let i = 0; i < len + rel && start + i < buf.length; i++) {
    let f = freq;
    if (vibrato) f *= 1 + vibrato * Math.sin(2 * Math.PI * 6 * i / RATE) * Math.min(1, i / (0.15 * RATE));
    phase += f / RATE;
    let env = Math.min(1, i / (attack * RATE));
    if (i >= len) env *= 1 - (i - len) / rel;
    buf[start + i] += (wave === "square" ? square(phase, duty) : triangle(phase)) * env * vol;
  }
}

// A decaying noise hit (hats, snare).
function hit(buf, t, dur, clock, vol, toClock = clock) {
  const start = Math.floor(t * RATE), len = Math.floor(dur * RATE), noise = makeNoise();
  for (let i = 0; i < len && start + i < buf.length; i++) {
    const k = i / len;
    buf[start + i] += noise(clock * Math.pow(toClock / clock, k)) * vol * Math.pow(1 - k, 2);
  }
}

// ---- The tune ----------------------------------------------------------------
// A minor, 128 BPM, Am - F - C - G. Triangle bass, thin square arpeggio and
// noise drums throughout; a square lead joins after the first pass so the
// loop doesn't just repeat for the whole video.

const STEP = 60 / 128 / 4; // sixteenth note
const CHORDS = [[57, 60, 64], [53, 57, 60], [48, 52, 55], [55, 59, 62]];
// Lead melody, one entry per eighth note across the 4-bar progression (null = rest).
const LEAD = [
  76, null, 76, 74, 72, null, 69, null,   // Am
  72, null, 72, 74, 76, null, 72, null,   // F
  79, null, 79, 76, 74, null, 72, null,   // C
  74, null, 74, 76, 71, null, null, null, // G
];

function music(buf, seconds) {
  const steps = Math.floor(seconds / STEP);
  for (let i = 0; i < steps; i++) {
    const t = i * STEP, bar = Math.floor(i / 16), chord = CHORDS[bar % 4], root = chord[0];
    if (i % 2 === 0) note(buf, t, STEP * 1.6, midi(root - 12 + (i % 4 === 2 ? 12 : 0)), { wave: "triangle", vol: 0.2 });
    note(buf, t, STEP * 0.7, midi(chord[[0, 1, 2, 1][i % 4]] + 12), { duty: 0.125, vol: 0.035, release: 0.02 });
    if (i % 4 === 2) hit(buf, t, 0.04, 22000, 0.04);
    if (i % 16 === 8) hit(buf, t, 0.12, 6000, 0.06, 1500);
    const pass = Math.floor(bar / 4);
    const lead = LEAD[(Math.floor(i / 2)) % LEAD.length];
    if (pass >= 1 && i % 2 === 0 && lead != null) {
      note(buf, t, STEP * 1.7, midi(lead), { duty: pass % 2 ? 0.5 : 0.25, vol: 0.06, vibrato: 0.006, release: 0.04 });
    }
  }
}

// ---- WAV -----------------------------------------------------------------------

export function renderSoundtrack(seconds) {
  const len = Math.ceil(seconds * RATE);
  const buf = new Float32Array(len);
  music(buf, seconds);

  const out = new Int16Array(len * 2);
  for (let i = 0; i < len; i++) {
    const t = i / RATE;
    const fade = Math.min(1, t / 0.4, (seconds - t) / 2.5);
    const v = Math.tanh(buf[i] * fade * VOLUME * 1.6) * 0.85;
    out[2 * i] = out[2 * i + 1] = Math.round(v * 32767);
  }

  const header = Buffer.alloc(44);
  header.write("RIFF", 0); header.writeUInt32LE(36 + out.byteLength, 4); header.write("WAVE", 8);
  header.write("fmt ", 12); header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(2, 22);
  header.writeUInt32LE(RATE, 24); header.writeUInt32LE(RATE * 4, 28); header.writeUInt16LE(4, 32); header.writeUInt16LE(16, 34);
  header.write("data", 36); header.writeUInt32LE(out.byteLength, 40);
  return Buffer.concat([header, Buffer.from(out.buffer)]);
}

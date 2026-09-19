#!/usr/bin/env python3
"""Generates all placeholder audio (16-bit PCM mono WAV) into content/sfx/.

Run from repo root:  python3 tools/make_audio.py
"""
import os, wave, math, struct, random

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SR = 22050
random.seed(7)

def write_wav(name, samples):
    path = os.path.join(ROOT, "content/sfx", name)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        frames = bytearray()
        for s in samples:
            s = max(-1.0, min(1.0, s))
            frames += struct.pack("<h", int(s * 32000))
        w.writeframes(bytes(frames))
    print(f"{name}: {len(samples)/SR:.2f}s")

def silence(dur): return [0.0] * int(SR * dur)

def env(i, n, a=0.01, r=0.3):
    t = i / n
    at = int(n * a)
    rt = int(n * r)
    g = 1.0
    if i < at: g = i / max(1, at)
    if i > n - rt: g = max(0.0, (n - i) / max(1, rt))
    return g

def tone(freq, dur, wave_fn="sine", vol=0.6, attack=0.01, release=0.3, slide=0.0):
    n = int(SR * dur)
    out = []
    ph = 0.0
    for i in range(n):
        f = freq * (1.0 + slide * (i / n))
        ph += 2 * math.pi * f / SR
        if wave_fn == "sine": v = math.sin(ph)
        elif wave_fn == "square": v = 1.0 if math.sin(ph) >= 0 else -1.0
        elif wave_fn == "saw": v = 2.0 * ((ph / (2 * math.pi)) % 1.0) - 1.0
        else: v = math.sin(ph)
        out.append(v * vol * env(i, n, attack, release))
    return out

def mix(*tracks):
    n = max(len(t) for t in tracks)
    out = [0.0] * n
    for t in tracks:
        for i, v in enumerate(t):
            out[i] += v
    return out

def cat(*tracks):
    out = []
    for t in tracks: out += t
    return out

def delay_track(smp, dt, gain):
    d = int(SR * dt)
    out = list(smp) + [0.0] * d
    for i, v in enumerate(smp):
        out[i + d] += v * gain
    return out

def noise(dur, vol=0.5, release=0.5, attack=0.005, lp=0.2):
    n = int(SR * dur)
    out, prev = [], 0.0
    for i in range(n):
        prev = prev * (1 - lp) + (random.uniform(-1, 1)) * lp
        out.append(prev * 8 * vol * env(i, n, attack, release))
    return out

def kick(dur=0.22, f0=150, f1=45, vol=0.9):
    n = int(SR * dur)
    out, ph = [], 0.0
    for i in range(n):
        f = f0 + (f1 - f0) * (i / n)
        ph += 2 * math.pi * f / SR
        out.append(math.sin(ph) * vol * math.exp(-6.0 * i / n))
    return out

# --- one-shots -------------------------------------------------------------
write_wav("blip.wav", tone(880, 0.07, "square", 0.35, 0.002, 0.05))
write_wav("move.wav", tone(520, 0.05, "square", 0.22, 0.002, 0.04))
write_wav("jump.wav", tone(300, 0.16, "sine", 0.5, 0.002, 0.1, slide=1.2))
write_wav("go.wav", mix(tone(660, 0.5, "square", 0.4, 0.002, 0.35), tone(990, 0.5, "square", 0.25, 0.002, 0.4)))
write_wav("cash.wav", cat(tone(988, 0.09, "square", 0.4, 0.002, 0.06), tone(1319, 0.35, "square", 0.4, 0.002, 0.3)))
write_wav("stinger_win.wav", delay_track(mix(
    cat(tone(523, 0.14, "saw", 0.30), tone(659, 0.14, "saw", 0.30), tone(784, 0.14, "saw", 0.30), tone(1047, 0.55, "saw", 0.34)),
    cat(silence(0.14 * 3), tone(1568, 0.5, "sine", 0.16))), 0.12, 0.35))
write_wav("stinger_elim.wav", mix(
    cat(tone(440, 0.22, "saw", 0.3), tone(349, 0.22, "saw", 0.3), tone(262, 0.6, "saw", 0.32)),
    cat(silence(0.66), kick(0.5, 120, 38, 0.8))))
write_wav("stinger_twist.wav", mix(tone(220, 0.7, "saw", 0.25, 0.02, 0.25, slide=1.6), noise(0.7, 0.2, 0.5, lp=0.5)))
write_wav("hammer_hit.wav", mix(kick(0.28, 130, 50, 0.85), tone(800, 0.16, "square", 0.16, 0.001, 0.14)))
write_wav("splash.wav", noise(0.42, 0.5, 0.6, lp=0.35))
write_wav("cheer.wav", noise(2.2, 0.4, 0.75, 0.3, lp=0.12))

# --- Priority 6: breakable glass path -------------------------------------
# Four cues that must stay distinguishable while several panes go at once:
#   glass_crack  short dry tick  — "this pane is about to go, MOVE"
#   glass_break  bright burst + shard tail — the pane actually dropping
#   glass_land   soft ring       — weight landing on a pane that HELD (the relief beat)
#   glass_reform crystalline rise — a pane fading back in
# Built from the same primitives as everything else so the set stays sonically of a piece.
#
# The whole block runs on a SAVED-AND-RESTORED RNG state with its own seed. Every noise-based
# generator here draws from the module-level `random`, so simply appending these calls would
# advance the shared stream and silently re-roll the noise in music_loop.wav (verified: it did).
# Bracketing the block keeps every pre-existing clip byte-identical no matter what is added here.
_rng_state = random.getstate()
random.seed(60601)

def normalize(smp, ceiling=0.92):
    """Scale a signal so its loudest sample sits exactly at `ceiling` (same trick the music
    loop uses at the bottom of this file). Peaks are transient-dependent here, so trusting
    hand-tuned scalars is what let a single sample clip; this makes headroom exact."""
    peak = max(abs(s) for s in smp) or 1.0
    return [s / peak * ceiling for s in smp]


def shards(dur=0.7, vol=0.5, density=34):
    """Sparse high partials decaying at different rates — the tinkle after a pane lets go."""
    n = int(SR * dur)
    out = [0.0] * n
    for _ in range(density):
        f = random.uniform(1400, 5200)
        start = int(random.uniform(0, 0.42) * SR)
        length = int(random.uniform(0.05, 0.3) * SR)
        ph = 0.0
        amp = vol * random.uniform(0.25, 1.0)
        for i in range(length):
            j = start + i
            if j >= n:
                break
            ph += 2 * math.pi * f / SR
            out[j] += math.sin(ph) * amp * math.exp(-7.5 * i / length)
    return out

# NOTE ON LEVELS: noise() carries an internal 8x gain, so its `vol` argument is far hotter than
# tone()'s. These were measured after generation and trimmed to sit under 0 dBFS — glass_crack
# fires on every pane a contestant touches, and a hard-clipped tick turns into an audible crunch
# when a whole row cracks at once. Peaks now land ~80-90% FS with zero clipped samples.
write_wav("glass_crack.wav", normalize(mix(
    noise(0.11, 0.055, 0.75, 0.001, lp=0.85),
    tone(2600, 0.06, "square", 0.10, 0.001, 0.05),
    tone(1730, 0.09, "sine", 0.08, 0.001, 0.07)), 0.62))

write_wav("glass_break.wav", normalize(mix(
    noise(0.30, 0.055, 0.7, 0.001, lp=0.7),
    shards(0.75, 0.27),
    cat(tone(1900, 0.05, "square", 0.16, 0.001, 0.04),
        tone(950, 0.20, "sine", 0.12, 0.001, 0.18))), 0.92))

write_wav("glass_land.wav", normalize(mix(
    tone(1320, 0.20, "sine", 0.20, 0.002, 0.18),
    tone(1980, 0.14, "sine", 0.09, 0.002, 0.12),
    noise(0.06, 0.10, 0.8, 0.001, lp=0.6)), 0.70))

write_wav("glass_reform.wav", normalize(mix(
    tone(660, 0.45, "sine", 0.17, 0.02, 0.35, slide=1.1),
    tone(990, 0.45, "sine", 0.10, 0.03, 0.35, slide=1.1),
    cat(silence(0.18), shards(0.34, 0.16, 12))), 0.55))

random.setstate(_rng_state)   # hand the shared stream back untouched (see note above)

# --- music loop: 124 BPM "show theme", 4 bars (~7.74s) ----------------------
BPM = 124.0
BEAT = 60.0 / BPM
BAR = BEAT * 4
bars = 4
total = int(SR * BAR * bars)
song = [0.0] * total

def add(sig, start_sec, gain=1.0):
    start = int(start_sec * SR)
    for i, v in enumerate(sig):
        j = start + i
        if 0 <= j < total: song[j] += v * gain

A2, C3, E3, G2, D3, F3 = 110.0, 130.81, 164.81, 98.0, 146.83, 174.61
A3, C4, E4, G4, A4, C5, E5, G5 = 220.0, 261.63, 329.63, 392.0, 440.0, 523.25, 659.26, 784.0
bass_line = [A2, A2, C3, E3, A2, A2, G2, G2]
lead_line = [A4, C5, E5, G5, E5, C5, A4, G4]

for bar in range(bars):
    t0 = bar * BAR
    for b in range(4):                                  # four-on-the-floor kick
        add(kick(0.2, 150, 45, 0.75), t0 + b * BEAT)
    for b in range(8):                                  # hats on 8ths
        add(noise(0.04, 0.16, 0.9, 0.001, lp=0.9), t0 + b * BEAT / 2)
    for b in (1, 3):                                    # snare on 2 & 4
        add(noise(0.12, 0.3, 0.7, 0.001, lp=0.55), t0 + b * BEAT)
    for b in range(8):                                  # bass
        f = bass_line[(bar * 2 + b // 4) % len(bass_line)]
        add(tone(f, BEAT / 2 * 0.9, "square", 0.20, 0.004, 0.12), t0 + b * BEAT / 2)
    for b in range(8):                                  # pluck lead w/ echo
        f = lead_line[(bar + b) % len(lead_line)]
        sig = delay_track(tone(f, 0.14, "square", 0.12, 0.002, 0.1), BEAT * 0.75, 0.4)
        add(sig, t0 + b * BEAT / 2)

# normalize
peak = max(abs(s) for s in song) or 1.0
song = [s / peak * 0.85 for s in song]
write_wav("music_loop.wav", song)

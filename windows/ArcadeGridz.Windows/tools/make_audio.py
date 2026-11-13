"""Generates the ag app's launcher audio (all synthesized, no samples):
  Assets/Audio/chillstep.wav   seamless ambient chillstep loop (70 BPM, 16 bars)
  Assets/Audio/jingle.wav      loading jingle (startup splash)
  Assets/Audio/move.wav select.wav back.wav open.wav launch.wav error.wav   UI sfx
Run: python3 make_audio.py <out_dir>   (needs numpy, scipy, ffmpeg on PATH)
"""
import os, subprocess, sys
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

SR = 44100
rng = np.random.default_rng(7)
OUT = sys.argv[1] if len(sys.argv) > 1 else "Assets/Audio"
os.makedirs(OUT, exist_ok=True)


def t_(dur):
    return np.arange(int(dur * SR)) / SR


def mtof(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def lp(x, f, order=2):
    return sosfilt(butter(order, f, "low", fs=SR, output="sos"), x)


def hp(x, f, order=2):
    return sosfilt(butter(order, f, "high", fs=SR, output="sos"), x)


def env(n, a, d, s, r, sus_len):
    """ADSR in seconds; sus_len = held time before release."""
    A, D, R = int(a * SR), int(d * SR), int(r * SR)
    S = max(0, int(sus_len * SR) - A - D)
    e = np.concatenate([np.linspace(0, 1, A, False), np.linspace(1, s, D, False), np.full(S, s), np.linspace(s, 0, R)])
    return np.pad(e, (0, max(0, n - len(e))))[:n]


def saw_stack(freq, dur, detune=(-0.12, 0, 0.11), phase=None):
    t = t_(dur)
    out = np.zeros_like(t)
    for d in detune:
        f = freq * 2 ** (d / 12)
        ph = rng.random() if phase is None else phase
        out += 2 * ((t * f + ph) % 1) - 1
    return out / len(detune)


def sine(freq, dur, ph=0.0):
    return np.sin(2 * np.pi * freq * t_(dur) + ph)


def mix_into(buf, sig, at):
    i = int(at * SR)
    if i >= len(buf):
        return
    n = min(len(sig), len(buf) - i)
    buf[i:i + n] += sig[:n]


def mix_wrap(buf, sig, at):
    """mix with wrap-around so loop tails continue into the start (seamless loop)."""
    i = int(at * SR) % len(buf)
    while len(sig):
        n = min(len(sig), len(buf) - i)
        buf[i:i + n] += sig[:n]
        sig = sig[n:]
        i = 0


def delay(x, time, fb, wet, loop=True):
    d = int(time * SR)
    y = x.copy()
    tap = x.copy()
    for _ in range(8):
        tap = np.roll(tap, d) if loop else np.concatenate([np.zeros(d), tap[:-d]])
        tap = lp(tap * fb, 5000)
        y += tap * wet
    return y


def reverb(x, wet=0.3, loop=True):
    """cheap diffuse reverb: a handful of filtered, decaying taps."""
    y = x.copy()
    for k, (dt, g) in enumerate([(0.031, .5), (0.047, .45), (0.071, .4), (0.113, .33), (0.173, .27), (0.251, .2), (0.367, .14), (0.53, .09)]):
        d = int(dt * SR)
        tap = np.roll(x, d) if loop else np.concatenate([np.zeros(d), x[:-d]])
        y += lp(tap, 3500 - k * 300) * g * wet
    return y


def norm(x, peak=0.89):
    m = np.max(np.abs(x)) or 1
    return x / m * peak


def write_wav(name, x):
    x = norm(x)
    wavfile.write(os.path.join(OUT, name), SR, (x * 32767).astype(np.int16))


# ---------------------------------------------------------------- music
BPM = 70
BEAT = 60 / BPM
BAR = BEAT * 4
BARS = 16
L = int(BARS * BAR * SR)
music_l = np.zeros(L)
music_r = np.zeros(L)

# Am9 - Fmaj7 - Cadd9 - Gsus/E-ish, voiced low and wide
chords = [
    [45, 57, 60, 64, 67, 71],   # A minor 9
    [41, 53, 57, 60, 64, 69],   # F maj7 (add 9)
    [48, 55, 60, 62, 64, 67],   # C add9
    [43, 55, 59, 62, 67, 69],   # G6/sus
]
roots = [33, 29, 36, 31]

for bar in range(BARS):
    ch = chords[bar % 4]
    at = bar * BAR
    # pad: detuned saws, slow swell, low-passed, stereo spread
    for i, m in enumerate(ch[1:]):
        p = saw_stack(mtof(m), BAR + 1.2)
        p = lp(p, 900 + 400 * np.sin(bar * 0.7)) * env(len(p), 0.9, 0.6, 0.75, 1.2, BAR)
        pan = (i / (len(ch) - 2)) * 2 - 1
        mix_wrap(music_l, p * 0.055 * (1 - pan * 0.6), at)
        mix_wrap(music_r, p * 0.055 * (1 + pan * 0.6), at)
    # sub bass: sine with gentle wobble (half-time "chillstep" feel)
    root = roots[bar % 4]
    sub = sine(mtof(root), BAR)
    wob = 0.55 + 0.45 * np.cos(2 * np.pi * (2 / BAR) * t_(BAR))  # 2 slow wobbles per bar
    sub *= env(len(sub), 0.03, 0.2, 0.9, 0.25, BAR - 0.1) * wob
    sub += 0.25 * lp(saw_stack(mtof(root + 12), BAR, detune=(0,)), 300) * env(len(sub), 0.02, 0.3, 0.5, 0.2, BAR - 0.1) * wob
    mix_wrap(music_l, sub * 0.33, at)
    mix_wrap(music_r, sub * 0.33, at)

# drums: half-time (kick on 1, snare/clap on 3), soft hats; drop out on bars 0-1 and 8 for breathing room
def kick():
    t = t_(0.5)
    f = 45 + 90 * np.exp(-t * 28)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 7)

def snare():
    t = t_(0.45)
    n = hp(rng.standard_normal(len(t)), 1200) * np.exp(-t * 11)
    body = np.sin(2 * np.pi * 185 * t) * np.exp(-t * 20)
    return (n * 0.6 + body * 0.5)

def hat(open_=False):
    t = t_(0.25 if open_ else 0.06)
    return hp(rng.standard_normal(len(t)), 7000) * np.exp(-t * (14 if open_ else 60))

drums = np.zeros(L)
for bar in range(BARS):
    if bar in (0, 1, 8):
        continue
    at = bar * BAR
    mix_wrap(drums, kick() * 0.9, at)
    mix_wrap(drums, kick() * 0.55, at + BEAT * 2.5)
    mix_wrap(drums, reverb(np.pad(snare(), (0, SR)), 0.6, loop=False) * 0.5, at + BEAT * 2)
    for k in range(8):
        mix_wrap(drums, hat(open_=(k == 7)) * (0.12 if k % 2 else 0.07), at + k * BEAT / 2 + (0.02 if k % 2 else 0))
music_l += drums * 0.8
music_r += drums * 0.8

# arp/pluck: chord tones up high, ping-pong delay, enters on bar 4
arp_l = np.zeros(L)
arp_r = np.zeros(L)
pattern = [0, 2, 4, 3, 5, 3, 4, 2]
for bar in range(4, BARS):
    ch = chords[bar % 4]
    tones = sorted(set(m + 12 for m in ch[1:]))
    for k, idx in enumerate(pattern):
        if (bar == 15 and k > 3):
            break
        m = tones[idx % len(tones)]
        t = t_(0.9)
        pl = (np.sin(2 * np.pi * mtof(m) * t) + 0.3 * np.sin(2 * np.pi * mtof(m) * 2 * t)) * np.exp(-t * 6)
        side = arp_l if k % 2 == 0 else arp_r
        mix_wrap(side, pl * 0.07, bar * BAR + k * BEAT / 2)
arp_l = delay(arp_l, BEAT * 0.75, 0.45, 0.6)
arp_r = delay(arp_r, BEAT * 0.75, 0.45, 0.6)
music_l += arp_l
music_r += np.roll(arp_r, int(0.012 * SR))

# vinyl / air texture
air = lp(hp(rng.standard_normal(L), 300), 2500) * 0.006
crackle = (rng.random(L) > 0.9993) * rng.standard_normal(L) * 0.08
music_l += air + lp(crackle, 4000)
music_r += air + lp(np.roll(crackle, 777), 4000)

music_l = reverb(music_l, 0.25)
music_r = reverb(music_r, 0.25)
# gentle master: low-pass top, soft clip
music_l = np.tanh(lp(music_l, 11000) * 1.4)
music_r = np.tanh(lp(music_r, 11000) * 1.4)
stereo = np.stack([music_l, music_r], 1)
stereo = stereo / np.max(np.abs(stereo)) * 0.8
# seamless loop: crossfade the last 20 ms into the first 20 ms, then start playback after them
N = int(0.02 * SR); w = np.linspace(0, 1, N)[:, None]
stereo = np.concatenate([stereo[N:-N], stereo[-N:] * (1 - w) + stereo[:N] * w])
tmp = os.path.join(OUT, "_chillstep.wav")
wavfile.write(tmp, SR, (stereo * 32767).astype(np.int16))
# WAV (not MP3/AAC): those add encoder priming silence that makes an audible gap at the loop point
subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", tmp, "-ar", "32000", os.path.join(OUT, "chillstep.wav")], check=True)
os.remove(tmp)

# ---------------------------------------------------------------- sfx
def blip(f0, f1, dur, shape="sine", decay=30):
    t = t_(dur)
    f = f0 * (f1 / f0) ** (t / dur)
    ph = 2 * np.pi * np.cumsum(f) / SR
    w = np.sin(ph) if shape == "sine" else (2 * ((ph / (2 * np.pi)) % 1) - 1)
    return w * np.exp(-t * decay)

def tail(x, wet=0.35):
    return reverb(np.pad(x, (0, int(0.5 * SR))), wet, loop=False)

write_wav("move.wav", tail(lp(blip(1400, 1700, 0.05, decay=70), 6000) * 0.5, 0.2))
write_wav("select.wav", tail(np.concatenate([blip(880, 880, 0.07, decay=35), blip(1320, 1320, 0.16, decay=22)]) * 0.7))
write_wav("back.wav", tail(np.concatenate([blip(990, 990, 0.06, decay=40), blip(660, 640, 0.14, decay=25)]) * 0.7))
w = lp(rng.standard_normal(int(0.35 * SR)), 2500) * np.sin(np.linspace(0, np.pi, int(0.35 * SR))) * 0.35
write_wav("open.wav", tail(w + blip(500, 1500, 0.35, decay=6) * 0.3, 0.4))
t = t_(1.1)
# filter sweep done in chunks (cutoff 600 Hz -> 5.6 kHz)
_n = rng.standard_normal(len(t)); _chunks = np.array_split(np.arange(len(t)), 24)
riser = np.concatenate([lp(_n[c], 600 + 5000 * (k / 23) ** 2) for k, c in enumerate(_chunks)]) * (t / t[-1]) ** 2 * 0.4
riser += blip(200, 1200, len(t) / SR, decay=0.5) * (t / t[-1]) ** 2 * 0.15
stab = np.zeros(len(t))
for m in (69, 76, 81, 85):
    stab += np.sin(2 * np.pi * mtof(m) * t_(1.1)) * np.exp(-t_(1.1) * 3)
launch = np.concatenate([riser[: int(0.6 * SR)], np.zeros(0)])
launch = np.pad(launch, (0, int(1.2 * SR)))
mix_into(launch, stab * 0.25, 0.6)
write_wav("launch.wav", tail(launch, 0.4))
write_wav("error.wav", tail(lp(blip(180, 150, 0.22, shape="saw", decay=12), 1500) * 0.6, 0.2))

# loading jingle: rising sparkly arpeggio into a warm chord (A major 9 lift), ~2.6 s
jl = np.zeros(int(3.4 * SR))
notes = [69, 73, 76, 80, 83, 88]
for i, m in enumerate(notes):
    tt = t_(1.2)
    pl = (np.sin(2 * np.pi * mtof(m) * tt) + 0.25 * np.sin(2 * np.pi * mtof(m) * 3 * tt)) * np.exp(-tt * 5)
    mix_into(jl, pl * 0.28, i * 0.11)
for m in (57, 64, 68, 71, 76):
    p = lp(saw_stack(mtof(m), 2.4), 2200) * env(int(2.4 * SR), 0.05, 0.4, 0.6, 1.4, 0.9)
    mix_into(jl, p * 0.09, 0.62)
sparkle = hp(rng.standard_normal(int(1.6 * SR)), 9000) * np.exp(-t_(1.6) * 3) * 0.05
mix_into(jl, sparkle, 0.62)
mix_into(jl, kick()[: int(0.4 * SR)] * 0.35, 0.62)
write_wav("jingle.wav", reverb(delay(jl, 0.18, 0.35, 0.4, loop=False), 0.4, loop=False))
print("ok", sorted(os.listdir(OUT)))

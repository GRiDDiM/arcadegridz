"""Precompute a 16-band spectrum per track for the ag app's visualizer.
Writes <track>.spec next to each mp3: raw uint8, frames x 16 bands, 25 fps, band 0 = lowest.
Each band is normalized per track (20th..99.5th percentile of its dB level -> 0..255) so every band moves,
not just the bass."""
import sys, glob, os, subprocess, numpy as np
SR, FPS, BANDS, N = 22050, 25, 16, 2048
HOP = SR // FPS
edges = np.geomspace(40, 11000, BANDS + 1)
freqs = np.fft.rfftfreq(N, 1 / SR)
idx = [(np.searchsorted(freqs, edges[i]), max(np.searchsorted(freqs, edges[i + 1]), np.searchsorted(freqs, edges[i]) + 1)) for i in range(BANDS)]
win = np.hanning(N).astype(np.float32)
for f in sorted(glob.glob(os.path.join(sys.argv[1], "*.mp3"))):
    raw = subprocess.run(["ffmpeg", "-v", "quiet", "-i", f, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"], capture_output=True).stdout
    x = np.frombuffer(raw, dtype=np.float32)
    n = len(x) // HOP
    x = np.concatenate([np.zeros(N // 2, np.float32), x, np.zeros(N, np.float32)])
    frames = np.lib.stride_tricks.sliding_window_view(x, N)[::HOP][:n] * win
    mag = np.abs(np.fft.rfft(frames, axis=1)) ** 2
    db = np.stack([10 * np.log10(mag[:, a:b].mean(1) + 1e-10) for a, b in idx], 1)
    lo = np.percentile(db, 20, axis=0); hi = np.percentile(db, 99.5, axis=0)
    v = np.clip((db - lo) / np.maximum(hi - lo, 6), 0, 1) ** 1.4
    out = (v * 255).round().astype(np.uint8)
    open(f[:-4] + ".spec", "wb").write(out.tobytes())
    print(os.path.basename(f), n, "frames", round(out.mean(), 1), "avg")

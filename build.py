import glob, json, base64, subprocess, re, os
import numpy as np
from scipy.io import wavfile
FPS = 25
tracks = []
for f in sorted(glob.glob("*.wav")):
    m = re.search(r" - (\d+) - (.+)\.wav$", f)
    num, title = int(m.group(1)), m.group(2)
    slug = f"{num:02d}-" + re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")
    out = f"out/{slug}.mp3"
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", f, "-codec:a", "libmp3lame", "-b:a", "192k",
                    "-metadata", f"title={title}", "-metadata", "album=GAME OVER - Insert Coin To Continue",
                    "-metadata", "artist=Benjamin Campbell", "-metadata", f"track={num}/11", out], check=True)
    sr, x = wavfile.read(f)
    x = x.astype(np.float32) / 32768.0
    hop = sr // FPS
    n = len(x) // hop
    peaks = np.abs(x[: n * hop]).reshape(n, hop, 2).max(1)          # per-frame peak, L/R
    db = 20 * np.log10(np.maximum(peaks, 1e-5))                       # dBFS
    lvl = np.clip((db + 48) / 48, 0, 1)                               # -48..0 dB -> 0..1
    b = (lvl * 255).round().astype(np.uint8)
    tracks.append({"n": num, "title": title, "file": os.path.basename(out), "duration": round(len(x) / sr, 2),
                   "fps": FPS, "peaksL": base64.b64encode(b[:, 0].tobytes()).decode(),
                   "peaksR": base64.b64encode(b[:, 1].tobytes()).decode()})
    print(num, title, os.path.getsize(out) // 1024, "KB")
album = {"id": "game-over", "title": "GAME OVER", "subtitle": "Insert Coin To Continue", "artist": "Benjamin Campbell",
         "base": "https://github.com/GRiDDiM/arcadegridz/releases/download/album-game-over-v1/", "tracks": tracks}
json.dump(album, open("out/album.json", "w"))
print("album.json", os.path.getsize("out/album.json") // 1024, "KB")

"""Original BSV "go home" whistle for dismissing a horse. No samples, recordings or game assets.
Companion of make_horse_whistle.py: the call whistle rises (low -> high note); this one falls
(high note, then a long downward glide), so players can tell the two apart by ear.
License: MIT; see ../../BetterStardewValley/THIRD_PARTY_LICENSES_HORSES.txt.
Run with Python 3, optionally pass the output WAV path.
"""
import math, random, struct, sys, wave
from pathlib import Path
out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[2] / 'BetterStardewValley/assets/horse-dismiss.wav'
out.parent.mkdir(parents=True, exist_ok=True)
rate, duration = 44100, 1.02
rng = random.Random(10222026)
phase, breath = 0.0, 0.0
samples = []
# (start, length, start frequency, end frequency)
notes = [(.02, .24, 1790, 1720), (.34, .62, 1640, 1080)]
for n in range(round(rate * duration)):
    t = n / rate
    value = 0.0
    for start, length, f0, f1 in notes:
        local = t - start
        if 0 <= local <= length:
            k = local / length
            # Soft attack, longer release on the falling note.
            env = math.sin(math.pi * min(1, k * 1.6) / 2) ** .8 * (1 - k) ** (.35 if f1 < f0 - 200 else .0) * (math.sin(math.pi * k) ** .3)
            freq = f0 + (f1 - f0) * (k ** 1.3) + 6 * math.sin(2 * math.pi * 5.1 * t)
            phase += 2 * math.pi * freq / rate
            breath = .86 * breath + .14 * rng.uniform(-1, 1)
            value = .32 * env * (math.sin(phase) + .035 * math.sin(phase * 2) + .05 * breath)
    samples.append(struct.pack('<h', round(max(-1, min(1, value)) * 32767)))
with wave.open(str(out), 'wb') as f:
    f.setparams((1, 2, rate, 0, 'NONE', 'not compressed'))
    f.writeframes(b''.join(samples))
print(out)

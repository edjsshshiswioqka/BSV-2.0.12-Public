"""Original BSV two-note whistle. No samples, recordings or game assets.
License: MIT; see ../../BetterStardewValley/THIRD_PARTY_LICENSES_HORSES.txt.
Run with Python 3, optionally pass the output WAV path.
"""
import math, random, struct, sys, wave
from pathlib import Path
out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[2] / 'BetterStardewValley/assets/horse-whistle.wav'
out.parent.mkdir(parents=True, exist_ok=True)
rate, duration = 44100, .92
rng = random.Random(10012026)
phase, breath = 0.0, 0.0
samples = []
for n in range(round(rate * duration)):
    t = n / rate
    # Rounded breath envelopes and a gentle downward bend keep the whistle soft.
    local = t - (.40 if t >= .40 else .02)
    length = .44 if t >= .40 else .29
    if 0 <= local <= length:
        env = math.sin(math.pi * local / length) ** .7
        freq = (1770 if t >= .40 else 1530) - 110 * (local / length) + 7 * math.sin(2 * math.pi * 5.3 * t)
        phase += 2 * math.pi * freq / rate
        breath = .86 * breath + .14 * rng.uniform(-1, 1)
        value = .32 * env * (math.sin(phase) + .035 * math.sin(phase * 2) + .045 * breath)
    else:
        value = 0.0
    samples.append(struct.pack('<h', round(max(-1, min(1, value)) * 32767)))
with wave.open(str(out), 'wb') as f:
    f.setparams((1, 2, rate, 0, 'NONE', 'not compressed'))
    f.writeframes(b''.join(samples))
print(out)

"""Integrate imagegen's four transparent sources into native 16x16 game assets.

No artwork is drawn here: crop, downsample, palette-quantize and enforce binary
alpha for nearest-neighbor game rendering. Source files remain unchanged.
"""
from pathlib import Path
from PIL import Image
import json, shutil

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'BetterStardewValley/assets'
GENERATED = ROOT.parent / 'generated_images'
SOURCES = {
    'items/a_cup_of_green_tea': 'exec-4a4d52e3-5e48-451d-a593-45adf7150e63.png',
    'items/tea_cake': 'exec-268befc9-af51-4dff-92c2-20d0ea8db544.png',
    'items/golden_apple': 'exec-ce3278b5-e43e-4835-b835-e7534f91ec8d.png',
    'buffs/tenacity': 'exec-634651f9-4762-4624-96b7-9a5d6cbedaeb.png',
}

results = []
preview = Image.new('RGBA', (4*224, 224), '#f4deb5')
for i, (name, file) in enumerate(SOURCES.items()):
    source = ASSETS / (name + '_source.png')
    source.parent.mkdir(parents=True, exist_ok=True)
    if not source.exists(): shutil.copy2(GENERATED / file, source)
    im = Image.open(source).convert('RGBA')
    assert im.getchannel('A').getextrema()[0] == 0, name
    bbox = im.getchannel('A').point(lambda x: 255 if x >= 128 else 0).getbbox()
    crop = im.crop(bbox)
    scale = 14 / max(crop.size)
    size = tuple(max(1, round(n*scale)) for n in crop.size)
    small = crop.resize(size, Image.Resampling.NEAREST)
    alpha = small.getchannel('A').point(lambda x: 255 if x >= 140 else 0)
    small = small.convert('RGB').quantize(colors=20, dither=Image.Dither.NONE).convert('RGBA')
    small.putalpha(alpha)
    final = Image.new('RGBA', (16,16))
    final.alpha_composite(small, ((16-size[0])//2, (16-size[1])//2))
    final.save(ASSETS / (name + '.png'))
    preview.alpha_composite(final.resize((192,192),Image.Resampling.NEAREST),(i*224+16,16))
    results.append({'name': name, 'source_size': im.size, 'final_size':final.size,
                    'visible_colors':len({p[:3] for p in final.getdata() if p[3]}),
                    'alpha_values': sorted(set(final.getchannel('A').getdata()))})
preview.save(ROOT/'verification/tea-sprites-preview.png')
(ROOT/'verification/tea-assets.json').write_text(json.dumps(results,indent=2)+'\n')
print(json.dumps(results))

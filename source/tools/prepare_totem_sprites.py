#!/usr/bin/env python3
"""Technical import of the author's four images, never generative redraw/recolor."""
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw
import argparse, json, hashlib, shutil
p=argparse.ArgumentParser();p.add_argument('--input',type=Path,required=True);p.add_argument('--preview',type=Path,required=True);a=p.parse_args()
root=Path(__file__).resolve().parents[2];dest=root/'BetterStardewValley/assets/totems';sources=root/'source/art/totems';dest.mkdir(parents=True,exist_ok=True);sources.mkdir(parents=True,exist_ok=True)
records=[];sheet=Image.new('RGBA',(640,360),(47,64,54,255));draw=ImageDraw.Draw(sheet)
for i,kind in enumerate(['harvest','growth','processing','thunderstorm']):
    filename=f'ChatGPT 图像 2026年10月4日 23_36_{55+i:02d}-{i+1}.png';path=a.input/filename
    image=Image.open(path).convert('RGBA');mask=image.getchannel('A').point(lambda x:255 if x>=200 else 0);box=mask.getbbox();image.putalpha(mask);crop=image.crop(box)
    shutil.copy2(path,sources/(kind+'-source.png'))
    def fit(size, bounds, bottom=False):
        factor=min(bounds[0]/crop.width,bounds[1]/crop.height);scaled=crop.resize((round(crop.width*factor),round(crop.height*factor)),Image.Resampling.NEAREST)
        out=Image.new('RGBA',size);out.alpha_composite(scaled,((size[0]-scaled.width)//2,size[1]-scaled.height-2 if bottom else (size[1]-scaled.height)//2));return out
    world=fit((64,128),(60,124),True);icon=fit((64,64),(56,56));native=world.resize((16,32),Image.Resampling.NEAREST)
    for suffix,img in [('world',world),('icon',icon),('native',native)]:img.save(dest/(kind+'-'+suffix+'.png'))
    dark=world.copy();rgb=dark.convert('RGB').point(lambda x:round(x*140/255));dark=rgb.convert('RGBA');dark.putalpha(world.getchannel('A'))
    sheet.alpha_composite(world,(i*160+12,30));sheet.alpha_composite(dark,(i*160+80,30));sheet.alpha_composite(icon,(i*160+48,185));draw.text((i*160+10,8),kind,fill='white');draw.text((i*160+8,270),'Ready / Cooldown',fill='white');draw.text((i*160+8,294),'64x128 / 64x64',fill='white')
    records.append({'kind':kind,'source_attachment':filename,'source_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'original_size':list(image.size),'alpha_threshold':200,'crop_box':box,'world_size':[64,128],'world_alpha_box':world.getbbox(),'icon_size':[64,64],'icon_alpha_box':icon.getbbox(),'native_size':[16,32],'recolor':False,'simplify':'alpha cleanup and nearest-neighbour reduction only','repack':False,'runtime_tint':[140,140,140,255]})
(root/'source/art/totems/asset-map.json').write_text(json.dumps(records,ensure_ascii=False,indent=2)+'\n');sheet.convert('RGB').save(a.preview)
print(json.dumps(records,ensure_ascii=False))

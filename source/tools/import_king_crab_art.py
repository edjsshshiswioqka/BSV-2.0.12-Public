"""Technical import of author-supplied assets. No drawing, generative edits, or color redesign.
JPEG: flood-fill only near-white background connected to image edges; protect enclosed whites.
PNG: preserve original alpha. Runtime SpiritArt.Import performs the existing nearest-pixel fit.
"""
import argparse, hashlib, json, shutil
from collections import deque
from pathlib import Path
from PIL import Image
p=argparse.ArgumentParser();p.add_argument('uploads',type=Path);p.add_argument('root',type=Path);a=p.parse_args()
files={'fish':'613B46EE-7511-4BC5-B18B-9DA664DB97C9(1).jpeg','bowl':'CB93C7A1-065E-40CB-A5EC-AC437589DD28(1).jpeg'}
originals=a.root/'source/KingCrab/art-source';assets=a.root/'BetterStardewValley/assets/king-crab'
originals.mkdir(parents=True,exist_ok=True);assets.mkdir(parents=True,exist_ok=True);report=[]
for kind,name in files.items():
 src=a.uploads/name;shutil.copy2(src,originals/name)
 im=Image.open(src).convert('RGBA');w,h=im.size;px=list(im.getdata());removed=0
 if src.suffix.lower() in ('.jpeg','.jpg'):
  seen=bytearray(w*h);queue=deque()
  def visit(i):
   if seen[i]:return
   seen[i]=1;r,g,b,_=px[i]
   if min(r,g,b)>=240 and max(r,g,b)-min(r,g,b)<=12:queue.append(i)
  for x in range(w):visit(x);visit((h-1)*w+x)
  for y in range(h):visit(y*w);visit(y*w+w-1)
  while queue:
   i=queue.popleft();r,g,b,_=px[i];px[i]=(r,g,b,0);removed+=1;x=i%w;y=i//w
   if x:visit(i-1)
   if x+1<w:visit(i+1)
   if y:visit(i-w)
   if y+1<h:visit(i+w)
  im.putdata(px)
 im.save(assets/(kind+'.png'))
 # Inspect source bounds without using file format as evidence of usable transparency.
 alpha=im.getchannel('A');mask=alpha.point(lambda v:255 if v>=200 else 0);box=mask.getbbox()
 report.append({'kind':kind,'source':name,'sha256':hashlib.sha256(src.read_bytes()).hexdigest(),'size':[w,h], 'visible_alpha_200_bounds':box,'edge_background_pixels_removed':removed,'retained_opaque_near_white':sum(1 for r,g,b,aa in px if aa>=200 and min(r,g,b)>=240)})
(originals/'IMPORT_EVIDENCE.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(report,ensure_ascii=False))

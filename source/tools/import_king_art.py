"""Technical import of author-supplied assets. No drawing, generative edits, or color redesign.
JPEG: flood-fill only near-white background connected to image edges; protect enclosed whites.
PNG: preserve original alpha. Runtime SpiritArt.Import performs the existing nearest-pixel fit.
"""
import argparse, hashlib, json, shutil
from collections import deque
from pathlib import Path
from PIL import Image
p=argparse.ArgumentParser();p.add_argument('uploads',type=Path);p.add_argument('root',type=Path);a=p.parse_args()
files={'fish':'3FE59CCA-9835-4E6B-9053-355AB0BA77DF(2).jpeg','sashimi':'E07011B4-8372-4FE6-A99B-265A8C484889.jpeg'}
originals=a.root/'source/LobsterKing/art-source';assets=a.root/'BetterStardewValley/assets/lobster-king'
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

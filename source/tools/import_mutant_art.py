"""Technical conversion only; author explicitly prohibits redrawing. Edge-connected white removal.
Original RGB and all retained RGB values are unchanged. Runtime uses SpiritArt.Import.
"""
from pathlib import Path
from collections import deque
import argparse, hashlib, json, shutil, struct
from PIL import Image
p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('root',type=Path);p.add_argument('evidence',type=Path);a=p.parse_args()
originals=a.root/'source/MutantLobsterKing/art-source';assets=a.root/'BetterStardewValley/assets/mutant-lobster-king'
for d in (originals,assets,a.evidence):d.mkdir(parents=True,exist_ok=True)
shutil.copy2(a.source,originals/a.source.name)
im=Image.open(a.source).convert('RGBA');w,h=im.size;px=list(im.getdata());seen=bytearray(w*h);queue=deque();removed=0
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
im.putdata(px);im.save(assets/'collectible.png')
(a.evidence/'collectible.rgba').write_bytes(struct.pack('<ii',w,h)+im.tobytes())
report={'source':a.source.name,'sha256':hashlib.sha256(a.source.read_bytes()).hexdigest(),'original_size':[w,h],
        'visible_bounds':im.getchannel('A').getbbox(),'removed_edge_connected_pixels':removed,
        'retained_near_white':sum(1 for r,g,b,aa in px if aa>=200 and min(r,g,b)>=240),'rgb_changed':False,'redrawn':False}
(originals/'IMPORT_EVIDENCE.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report))

from PIL import Image
import numpy as np,re,json
from pathlib import Path
root=Path(__file__).resolve().parents[2];(root/'verification/art').mkdir(parents=True,exist_ok=True);dst=root/'BetterStardewValley/assets/crab-catcher'
src=np.array(Image.open(root/'source/art/crab-redrawn-reference.png').convert('RGBA'))
# Opaque original art clusters only; discard generated soft halos entirely.
a=src[:,:,3]>220;yy,xx=np.where(a);crop=src[yy.min():yy.max()+1,xx.min():xx.max()+1].copy();crop[crop[:,:,3]<=220]=0
native=np.array(Image.fromarray(crop).resize((72,40),Image.Resampling.BOX))
s=(root/'source/FishingMachine/Eddie.FishingMachine.Core/Visuals.cs').read_text();s=s.split('public static readonly uint[] Palette=new[]{')[1].split('}.Select')[0]
palette=np.array([[(int(h,16)>>16)&255,(int(h,16)>>8)&255,int(h,16)&255] for h in re.findall(r'0x([0-9a-f]{6})',s)],dtype=int)
rgba=np.zeros((48,72,4),dtype=np.uint8)
for y in range(40):
 for x in range(72):
  r,g,b,a=native[y,x]
  if a<145:continue
  ds=((palette-[r,g,b])**2*np.array([2,4,3])).sum(axis=1);rgba[y+3,x]=[*palette[ds.argmin()],255]
Image.fromarray(rgba).save(dst/'idle-lv1.png')
Image.fromarray(rgba).resize((720,480),Image.Resampling.NEAREST).save(root/'verification/art/native-preview.png')
print('Source crop',xx.min(),yy.min(),xx.max()+1,yy.max()+1)
# Isolate components on a single immutable 72x48 grid. Frame changes never change geometry.
legs=np.zeros_like(rgba);legmask=np.zeros((48,72),bool)
for x,y,w,h in [(18,31,11,12),(44,31,11,12),(26,35,6,6),(40,35,6,6)]:legmask[y:y+h,x:x+w]=True
legs[legmask]=rgba[legmask];body=rgba.copy();body[legmask]=0
pots=[]
for x,y,w,h in [(0,14,10,11),(7,25,10,11),(55,25,10,11),(62,14,10,11)]:
 pots.append((x,y,body[y:y+h,x:x+w].copy()));body[y:y+h,x:x+w]=0
for x,y,w,h in [(4,10,2,4),(11,11,2,14),(59,11,2,14),(66,10,2,4)]:body[y:y+h,x:x+w]=0
# Copper-only remapping preserves blue lenses and structural metal; fixed coordinates at both tiers.
goldmap={0x773d2c:0x745038,0xa65532:0xb58c34,0xcb7843:0xe3be59,0xe59e60:0xf1c583,0xf1c583:0xfff2d5,0x76403a:0x99613b,0xb64339:0xb58c34,0xe46a4c:0xe3be59}
def gold(a):
 out=a.copy();rgb=(a[:,:,0].astype(int)<<16)|(a[:,:,1].astype(int)<<8)|a[:,:,2]
 for old,new in goldmap.items():out[rgb==old,:3]=[(new>>16)&255,(new>>8)&255,new&255]
 return out
frames=[]
for stage in range(6):
 f=body.copy()
 for n,(x,y,pot) in enumerate(pots):
  bottom=y+pot.shape[0];delta=44-bottom
  drop=round(delta*min(stage,4)/4); cablex=[4,11,59,66][n]; top=[10,11,11,10][n];end=y+drop+2
  if stage==5:end=44
  for cy in range(top,min(44,end)):
   f[cy,cablex]=[41,78,104,255];f[cy,cablex+1]=[188,174,148,255]
  if stage<5:
   for py in range(pot.shape[0]):
    yy=y+drop+py
    if yy>=44:continue
    for px in range(pot.shape[1]):
     if pot[py,px,3]:f[yy,x+px]=pot[py,px]
  if stage>=4:
   # Pixel-stepped submergence contained in cage pixels, never baked map-colored puddles.
   for cy,alpha in [(42,170),(43,80)]:
    sl=f[cy,x:x+pot.shape[1]];sl[sl[:,3]>0,3]=alpha
 if stage==5:
  for cx,top in [(4,10),(11,11),(59,11),(66,10)]:f[top:44,cx:cx+2]=0
 frames.append(f)
# World-only two-row transparency at each foot; item art remains dry.
worldlegs=legs.copy()
for x in range(72):
 ys=np.where(worldlegs[:,x,3]>0)[0]
 if len(ys):
  bottom=ys[-1];worldlegs[bottom,x,3]=85
  if bottom>0 and worldlegs[bottom-1,x,3]:worldlegs[bottom-1,x,3]=175
for level,transform in [(1,lambda a:a),(2,gold)]:
 Image.fromarray(transform(rgba)).crop((0,3,72,43)).save(dst/f'held-lv{level}.png')
 Image.fromarray(transform(worldlegs)).save(dst/f'legs-lv{level}.png')
 sheet=np.concatenate([transform(f) for f in frames],axis=1);Image.fromarray(sheet).save(dst/f'world-lv{level}.png')
 # Distinct menu asset with cleaned hard alpha, not a world sheet source rectangle.
 icon=np.array(Image.fromarray(transform(rgba)).crop((0,3,72,43)).resize((28,16),Image.Resampling.BOX))
 icon[icon[:,:,3]<140]=0;icon[icon[:,:,3]>=140,3]=255
 # Quantize icon after area reduction to same palette.
 for y in range(16):
  for x in range(28):
   if icon[y,x,3]:
    ds=((palette-icon[y,x,:3])**2*np.array([2,4,3])).sum(axis=1);icon[y,x,:3]=palette[ds.argmin()]
 canvas=Image.new('RGBA',(32,16));canvas.paste(Image.fromarray(icon),(2,0));canvas.save(dst/f'icon-lv{level}.png')
 Image.fromarray(transform(rgba)).save(dst/f'idle-lv{level}.png')
layout={'Width':72,'Height':48,'OriginX':36,'OriginY':42,'WorldScale':4,'HeldScale':1,'HeldHeight':40,'IconWidth':32,'IconHeight':16,'IconScale':2,'Frames':6,'FrameOrder':['Idle','Drop25','Drop50','Drop75','Waterline','Submerged'],'Footprint':[2,2],'CableColumns':[4,11,59,66],'WaterlineRow':44,'BodyBobMaxPixels':1,'CableSwayMaxPixels':1}
(dst/'layout.json').write_text(json.dumps(layout,indent=2)+'\n')
# Exact nearest-neighbor asset contact sheet, not an in-game screenshot.
fm=Image.open(root/'BetterStardewValley/assets/machine.png').convert('RGBA');contact=Image.new('RGBA',(216,64),(40,54,53,255));contact.alpha_composite(fm,(0,18));contact.alpha_composite(Image.fromarray(rgba),(72,8));contact.alpha_composite(Image.fromarray(gold(rgba)),(144,8));contact.resize((1296,384),Image.Resampling.NEAREST).save(root/'verification/art/side-by-side.png')

# Fallback for third-party code reading native BigCraftable ItemData directly.
i=Image.open(dst/'icon-lv1.png').resize((16,8),Image.Resampling.NEAREST)
f=Image.new('RGBA',(16,32));f.paste(i,(0,12));f.save(dst/'native-icon.png')

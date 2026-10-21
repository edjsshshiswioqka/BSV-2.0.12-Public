"""1.1.47 source-resolution regression evidence and reproducible release packaging.

Pillow only decodes retained author assets for tests. Actual padding is executed
by HdChecks through the compiled production HdSprites.PadSource method.
"""
import argparse,hashlib,json,shutil,struct,zipfile
from pathlib import Path
from PIL import Image

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('action',choices=['prepare','package'])
p.add_argument('--work',required=True,type=Path)
p.add_argument('--build',type=Path)
a=p.parse_args();root=Path(__file__).resolve().parents[2];mod=root/'BetterStardewValley'
work=a.work.resolve();raw=work/'raw';report=root/'reports/original-art-1.1.47'
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
groups=['giant-lobster','king-crab','lobster-king','mutant-lobster-king']
assets=sorted(f for group in groups for f in (mod/'assets'/group).glob('*.png'))
if a.action=='prepare':
    raw.mkdir(parents=True,exist_ok=True);rows=[]
    for f in assets:
        im=Image.open(f).convert('RGBA');name=f.parent.name+'-'+f.stem+'.rgba'
        (raw/name).write_bytes(struct.pack('<ii',*im.size)+im.tobytes())
        rows.append({'asset':f.relative_to(root).as_posix(),'sha256':sha(f),'width':im.width,'height':im.height,'test_raw':name})
    (work/'asset-inputs.json').write_text(json.dumps(rows,indent=2)+'\n')
    print(json.dumps(rows,indent=2));raise SystemExit

assert a.build is not None
build=a.build.resolve();gates=json.loads((build/'release-gates.json').read_text())
assert gates['version']=='1.1.47' and gates['full_suite']
assert all(s['passed'] or s['suite']=='AutomationNativeChecks' and s.get('status')=='BLOCKED' for s in gates['suites'])
identity=json.loads((work/'identities.txt').read_text())
for name in ['BetterStardewValley.dll','BetterStardewValley.Automate.dll']:
    assert identity[name]['assembly_version']=='1.1.47.0'
    assert identity[name]['sha256']==sha(build/name)
art=json.loads((work/'original-pixels.txt').read_text());assert art['passed']
rows=json.loads((work/'asset-inputs.json').read_text());assert len(rows)==8
for row in rows:
    f=root/row['asset'];assert row['sha256']==sha(f)
    data=(raw/(row['test_raw']+'.padded')).read_bytes();w,h=struct.unpack('<ii',data[:8])
    image=Image.frombytes('RGBA',(w,h),data[8:]);left=(w-row['width'])//2;top=(h-row['height'])//2
    assert image.crop((left,top,left+row['width'],top+row['height'])).tobytes()==Image.open(f).convert('RGBA').tobytes()
    row['runtime_texture_side']=w;row['pixel_exact']=True
    # A lossless PNG of the ACTUAL production padding output, not a rendered screenshot.
    image.save(raw/(row['test_raw']+'.padded.png'))
baseline=work/'BSV-1.1.46-Pre-Original-Art.zip'
with zipfile.ZipFile(baseline) as z:
    old={n:hashlib.sha256(z.read(n)).hexdigest() for n in z.namelist() if not n.endswith('/')}
changed=[n for n,s in old.items() if n.startswith(('source/','BetterStardewValley/')) and (not (root/n).exists() or sha(root/n)!=s)]
allowed={'source/HdSprites.cs','source/GiantLobster/LobsterService.cs','source/KingCrab/KingCrabService.cs','source/LobsterKing/KingService.cs','source/MutantLobsterKing/MutantService.cs','source/BetterStardewValley.csproj','source/AutomateBridge/BetterStardewValley.Automate.csproj','BetterStardewValley/manifest.json','source/tests-hd/Program.cs','source/tests-hd/Hd.Tests.csproj','source/tools/run_release_checks.py'}
assert set(changed)==allowed,(set(changed)-allowed,allowed-set(changed))
assert all(sha(root/n)==s for n,s in old.items() if n.startswith('BetterStardewValley/assets/') or '/art-source/' in n)
scope={'baseline_sha256':sha(baseline),'changed_files':changed,'new_tool':'source/tools/original_art_release.py','all_existing_assets_byte_identical':True,'gameplay_configs_ids_save_schemas_unchanged':True}
report.mkdir(parents=True,exist_ok=True)
counts={state:sum(('PASS' if s['passed'] else s.get('status','FAIL'))==state for s in gates['suites']) for state in ['PASS','FAIL','BLOCKED']}
lines=['# BSV 1.1.47 原分辨率素材显示补丁','','## 结果与范围','',
'取消大龙虾、专属钓饵、大餐、帝王蟹、盖浇饭、龙虾王、刺身、变异龙虾王八张素材运行时16/64像素重采样。原有PNG及作者原文件均未修改。仅增加透明方形边框，源像素RGBA逐一复制；不裁切主体、不减色、不重绘、不做Alpha阈值剔除。',
'','游戏格子的16像素逻辑句柄仍然存在，但它是透明索引，不含缩小图案。实际SpriteBatch绘制映射到原分辨率纹理；画面中仍等比缩放到正常背包/手持大小。原分辨率不意味着屏幕能在一个64像素物品格中显示上千像素细节；本补丁取消的是预先降采样，不是屏幕的必要缩放。',
'','不扩大到图腾、机器、旧酒品或精灵鱼等未在本次确认范围内的资源。价格、概率、配方、库存、存档格式和生产流程保持1.1.46原字节。',
'','## 素材核验','','|资源|源像素|实际绘制纹理（含透明边框）|逐像素保留|','|---|---:|---:|---|']
for r in rows:lines.append(f"|{r['asset']}|{r['width']}×{r['height']}|{r['runtime_texture_side']}×{r['runtime_texture_side']}|PASS|")
lines+=['','## 自动验证与边界','',f"主程序、桥接、manifest版本1.1.47；无头完整回归{counts['PASS']} PASS、{counts['FAIL']} FAIL、{counts['BLOCKED']} BLOCKED。总门禁passed={gates['passed']}，不将受阻项伪装通过。",
f"真实八张资产调用编译后的生产PadSource；另有6种尺寸/随机RGBA夹具，共{art['assertions']}条断言。验证每个像素、半透明边缘、长宽比、各缩放比例、中心点、嵌套Draw不重复转换、注册清理。程序集检查四个服务不再调用缩图导入器。",
'原生自动化完整套件缺少System.Data.HashFunction相关测试依赖，仍为BLOCKED。target-01中测试代码拼写导致的编译失败日志保留，修正后重新执行。',
'','NOT TESTED IN REAL GAME：没有实际GPU游戏画面、不同UI缩放、掉落、桌面显示、联机/分屏截图。无头数学与字节检查不等于实机视觉验收。此前下水道地图闭环/实际跨日验收限制仍存在，本显示补丁不声称修复它们。',
'','## 实现与兼容','',
'HdSprites.CreateSourceResolution/PadSource创建原分辨率缓存，复用现有七种Draw重映射。四个物品服务仅改Texture与缓存清理；GiantLobster失效清理补齐自身生成纹理的Dispose。ModContent拥有的原图不由此处释放。',
'HD适配初始化失败时明确报错，不静默改回压缩图片；主启动已有必要模块失败门禁保留。第三方若直接读取/复制16像素逻辑纹理像素而绕过标准SpriteBatch绘制，可能看到透明句柄，需要专门适配，不能声明兼容所有显示Mod。',
f"八张全尺寸方形替代纹理约{sum(r['runtime_texture_side']**2*4 for r in rows)/1024**2:.2f} MiB（不含原资源缓存）；按需加载，不每帧生成。显存占用高于64×64旧版。",
'','本补丁未修改/覆盖采样器状态，沿用游戏既有像素绘制流程。保留先前授权的背景透明处理，本轮不再处理背景。',
'','## 安装','','备份存档；用Test-Build ZIP中的BetterStardewValley文件夹替换旧程序和资源，保留自己的config.json和机器配置。完整重启游戏以载入新绘制逻辑。Source ZIP用于开发，不是安装包。',
'','## 测试复现','','执行tools/run_release_checks.py完整回归；用本工具prepare导出RGBA测试输入，然后运行HdChecks.dll <主DLL> <raw目录>。后者调用生产PadSource并输出.padded字节，再由本工具package验证每个RGBA字节并输出证据PNG。']
(report/'VISUAL_CHANGE_REPORT.md').write_text('\n'.join(lines)+'\n')
(report/'CHANGELOG.md').write_text('# 1.1.47\n\n八项新增海产/料理取消低分辨率重采样，保留原图像素，正常大小等比绘制。修正对应缓存释放。无玩法修改。实机视觉未验证。\n')
verification=root/'verification/original-art-1.1.47';verification.mkdir(parents=True,exist_ok=True)
for directory in sorted(work.iterdir()):
    if directory.is_dir() and (directory/'release-checkpoint.json').exists():
        target=verification/directory.name;target.mkdir(exist_ok=True)
        for f in directory.iterdir():
            if f.is_file() and f.suffix in {'.json','.jsonl','.txt','.err','.rsp'}:shutil.copy2(f,target/f.name)
for filename in ['identities.txt','original-pixels.txt','original-pixels.err','asset-inputs.json']:shutil.copy2(work/filename,verification/filename)
(verification/'scope-check.json').write_text(json.dumps(scope,indent=2)+'\n')
(verification/'asset-pixel-checks.json').write_text(json.dumps(rows,indent=2)+'\n')
out=work/'delivery';out.mkdir(exist_ok=False);stage=out/'install'/'BetterStardewValley';stage.mkdir(parents=True)
for name in ['BetterStardewValley.dll','BetterStardewValley.Automate.dll']:shutil.copy2(build/name,stage/name)
for name in ['assets','i18n','THIRD_PARTY_LICENSES']:shutil.copytree(mod/name,stage/name)
for name in ['manifest.json','fishing-machine.json','crab-catcher.json','THIRD_PARTY_LICENSES.md','THIRD_PARTY_LICENSES_HORSES.txt']:shutil.copy2(mod/name,stage/name)
for name in ['CODE_PROVENANCE.md','THIRD_PARTY_PROVENANCE.md','LICENSE_REPORT.md']:shutil.copy2(root/name,stage/name)
shutil.copy2(report/'VISUAL_CHANGE_REPORT.md',stage/'README-1.1.47.md')
assert sorted(p.name for p in stage.rglob('*.dll'))==['BetterStardewValley.Automate.dll','BetterStardewValley.dll']
def archive(name,pairs):
    pairs=sorted(pairs,key=lambda pair:pair[1]);assert len(pairs)==len(set(n for _,n in pairs))
    with zipfile.ZipFile(out/name,'w',zipfile.ZIP_DEFLATED) as z:
        for f,n in pairs:z.write(f,n)
    with zipfile.ZipFile(out/name) as z:assert z.testzip() is None
archive('BSV-1.1.47-Original-Art-Test-Build.zip',[(f,f.relative_to(stage.parent).as_posix()) for f in stage.rglob('*') if f.is_file()])
source=[]
for directory in [root/'source',mod,report]:
    for f in directory.rglob('*'):
        if f.is_file() and not any(x in {'bin','obj','__pycache__','Documentation'} for x in f.relative_to(root).parts) and f.suffix.lower() not in {'.dll','.pdb','.pyc'}:source.append(f)
for name in ['README_FIRST.txt','README_BUILD_1.1.47.md','CODE_PROVENANCE.md','THIRD_PARTY_PROVENANCE.md','LICENSE_REPORT.md','THIRD_PARTY_LICENSES.md']:source.append(root/name)
source+=list(f for f in root.glob('LICENSE*') if f.is_file());source=sorted(set(source))
manifest=out/'SOURCE_SHA256SUMS.txt';manifest.write_text(''.join(f'{sha(f)}  {f.relative_to(root).as_posix()}\n' for f in source))
archive('BSV-1.1.47-Complete-Source.zip',[(f,f.relative_to(root).as_posix()) for f in source]+[(manifest,manifest.name)])
evidence=[(f,f.relative_to(root).as_posix()) for directory in [report,verification] for f in directory.rglob('*') if f.is_file()]
evidence += [(f,'production-padded-originals/'+f.name) for f in raw.glob('*.padded.png')]
archive('BSV-1.1.47-Reports-And-Evidence.zip',evidence)
shutil.copy2(report/'VISUAL_CHANGE_REPORT.md',out/'BSV-1.1.47-Visual-Report.md')
(out/'INSTALL_FILE_HASHES.json').write_text(json.dumps({f.relative_to(stage).as_posix():sha(f) for f in stage.rglob('*') if f.is_file()},indent=2)+'\n')
(out/'BSV-1.1.47-SHA256SUMS.txt').write_text(''.join(f'{sha(f)}  {f.name}\n' for f in sorted(out.iterdir()) if f.is_file()))
print(json.dumps({'version':'1.1.47','suites':counts,'art_assertions':art['assertions'],'assets':rows,'live_game':False},indent=2))

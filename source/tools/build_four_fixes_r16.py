#!/usr/bin/env python3
"""Offline .NET 6 build using the SDK's C# compiler and user-owned game references.
Compiles the real project Compile items; never downloads or distributes dependencies.
Uses csc directly so it also works in containers where dotnet CLI process inspection fails.
"""
import argparse, glob, hashlib, json, shutil, subprocess, xml.etree.ElementTree as ET
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
for name in ['sdk','game','smapi','out']: p.add_argument('--'+name,type=Path,required=True)
a=p.parse_args(); a.out=a.out.resolve();a.out.mkdir(parents=True,exist_ok=True)
source=Path(__file__).resolve().parents[1]
dotnet=a.sdk.resolve()/'dotnet'
csc=next(a.sdk.resolve().glob('sdk/6.*/Roslyn/bincore/csc.dll'))
framework=sorted(next(a.sdk.resolve().glob('packs/Microsoft.NETCore.App.Ref/6.*/ref/net6.0')).glob('*.dll'))
refs=[a.game/name for name in ['Stardew Valley.dll','StardewValley.GameData.dll','MonoGame.Framework.dll','xTile.dll']]
refs += [a.smapi/'StardewModdingAPI.dll']
refs += [a.smapi/'smapi-internal'/name for name in ['0Harmony.dll','Newtonsoft.Json.dll','SMAPI.Toolkit.CoreInterfaces.dll']]
# Game releases may put Netcode in a separate assembly.
if (a.game/'Netcode.dll').exists():refs.append(a.game/'Netcode.dll')
refs=[r.resolve() for r in refs]
for r in refs:
 if not r.is_file():raise SystemExit('Missing reference: '+str(r))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def files(project):
 tree=ET.parse(project);result=set()
 if tree.findtext('.//EnableDefaultCompileItems')!='false':result.update(project.parent.glob('*.cs'))
 for c in tree.findall('.//Compile'):
  if c.get('Include'):result.update(project.parent.glob(c.get('Include')))
 return sorted(p.resolve() for p in result)
version=ET.parse(source/'BetterStardewValley.csproj').findtext('.//Version')
label='four-fixes-r16-partial'
identity=a.out/'BuildIdentity.cs'
identity.write_text('using System.Reflection;\n[assembly: AssemblyVersion("'+version+'.0")]\n[assembly: AssemblyFileVersion("'+version+'.0")]\n[assembly: AssemblyInformationalVersion("'+version+'-'+label+'")]\n[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v6.0",FrameworkDisplayName=".NET 6.0")]\n')
implicit=a.out/'TestGlobalUsings.cs'
implicit.write_text('global using System;\nglobal using System.Collections.Generic;\nglobal using System.IO;\nglobal using System.Linq;\nglobal using System.Threading;\nglobal using System.Threading.Tasks;\n')
evidence={'version':version,'policy':label,'fishing_balance_version':1,'compiler':str(csc.relative_to(a.sdk.resolve())), 'inputs':{r.name:sha(r) for r in refs},'builds':{}}
def build(name,inputs,references,exe=False):
 dest=a.out/(name+'.dll');rsp=a.out/(name+'.rsp')
 argv=['-nologo','-nostdlib+','-langversion:10','-nullable:enable','-optimize+','-deterministic+','-define:TRACE;RELEASE;NET;NET6_0;NETCOREAPP;NET6_0_OR_GREATER;NETCOREAPP3_1_OR_GREATER','-target:'+('exe' if exe else 'library'),'-out:"'+str(dest)+'"']
 argv+=['-reference:"'+str(r)+'"' for r in framework+references]
 argv+=['"'+str(r)+'"' for r in inputs]
 rsp.write_text('\n'.join(argv)+'\n')
 proc=subprocess.run([str(dotnet),str(csc),'-noconfig','@'+str(rsp)],capture_output=True,text=True,timeout=120)
 (a.out/(name+'.compile.log')).write_text(proc.stdout+proc.stderr)
 print(name,proc.returncode,flush=True)
 if proc.returncode:print(proc.stdout+proc.stderr);raise SystemExit(proc.returncode)
 evidence['builds'][name]={'source_count':len(inputs),'source_sha256':{str(r.relative_to(source)) if r.is_relative_to(source) else r.name:sha(r) for r in inputs},'assembly_sha256':sha(dest)}
 if exe:(a.out/(name+'.runtimeconfig.json')).write_text(json.dumps({'runtimeOptions':{'tfm':'net6.0','framework':{'name':'Microsoft.NETCore.App','version':'6.0.0'}}}))
 return dest
main=build('BetterStardewValley',files(source/'BetterStardewValley.csproj')+[identity],refs)
for name,folder,native in [('FishingRareR15','tests-fishing-rare-r15',False),('Orders.Tests','tests-orders',False),('OrdersUi.Tests','tests-orders-ui',False),('OrdersMarketNativeChecks','tests-orders-market-native',True),('LocalizationR5.Tests','tests-localization-r5',False),('Hotfix.Tests','tests-hotfix',False),('Rescue.Tests','tests-rescue',False),('ProductionChecks','FishingMachine/tests/ProductionChecks',False),('CaveChecks','FishingMachine/tests/CaveChecks',False),('AfkRecoveryChecks','tests-afk-hotfix',True),('SpiritNativeChecks','tests-spirit',True),('FourFixesNativeChecks','tests-four-fixes-r16',True),('Totems.Tests','tests-totems',False),('CleanupNativeChecks','tests-recovery-cleanup',True),('HdSpriteChecks','tests-hd',True),('ProfessionsNativeChecks','tests-professions-native',True),('FarmingNativeChecks','tests-farming-native',True),('TractorNativeChecks','tests-tractor-native',True)]:
 project=next((source/folder).glob('*.csproj'));inputs=files(project)
 if not any(r.name=='GlobalUsings.cs' for r in inputs):inputs.append(implicit)
 extra=[a.smapi.resolve()/'smapi-internal/0Harmony.dll'] if folder=='tests-hotfix' else []
 if folder=='tests-totems':extra.append(a.smapi.resolve()/'smapi-internal/Newtonsoft.Json.dll')
 if native:
  inputs += [source/'tools/TestDependencyResolver.cs']
  extra=refs+([] if folder=='tests-afk-hotfix' else [main])
  inputs.append(source/'tools/NativeTranslationFixture.cs')
 if folder in {'tests-spirit','tests-hd','tests-professions-native','tests-four-fixes-r16'}:extra.append(a.smapi.resolve()/'smapi-internal/Mono.Cecil.dll')
 build(name,inputs,extra,True)
# Local native-test dependencies only: excluded from distributable packages.
for dep in refs+list((a.smapi/'smapi-internal').glob('*.dll')):shutil.copy2(dep,a.out/dep.name)
(a.out/'build-evidence.json').write_text(json.dumps(evidence,indent=2)+'\n')
print('Compiled production assembly and eighteen test programs. No live-game test claimed.')

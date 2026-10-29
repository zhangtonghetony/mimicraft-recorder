"""Build a local portable Windows test package; does not upload or install anything."""
from pathlib import Path
import datetime,json,os,shutil,subprocess,xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
SDK=ROOT/'.tools/dotnet/dotnet.exe'
stamp=datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
version=ET.parse(ROOT/'src/Mimicraft.Recorder/Mimicraft.Recorder.csproj').findtext('.//Version')
destination=ROOT/'artifacts/packages'/('MimicraftRecorder-'+version+'-win-x64-'+stamp)
destination.mkdir(parents=True,exist_ok=False)
env=dict(os.environ,DOTNET_ROOT=str(SDK.parent),DOTNET_CLI_TELEMETRY_OPTOUT='1')
subprocess.run([str(SDK),'publish',str(ROOT/'src/Mimicraft.Recorder/Mimicraft.Recorder.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:DebugType=None','-p:DebugSymbols=false','-o',str(destination),'-v','minimal'],cwd=ROOT,env=env,check=True)
media=next((ROOT/'.tools/ffmpeg').glob('*/bin/ffmpeg.exe')).parent
shutil.copytree(media,destination/'tools')
notices=destination/'Notices';notices.mkdir()
shutil.copy2(media.parent/'LICENSE.txt',notices/'FFmpeg-LGPL.txt')
shutil.copy2(ROOT/'.tools/dotnet/LICENSE.txt',notices/'dotnet-LICENSE.txt')
for source in (ROOT/'.tools/dotnet').glob('*THIRD*'):
    if source.is_file():shutil.copy2(source,notices/source.name)
shutil.copy2(ROOT/'THIRD_PARTY_NOTICES.md',notices/'THIRD_PARTY_NOTICES.md')
shutil.copytree(ROOT/'third_party',notices/'third_party')
for name in ['README.md','LICENSE','THIRD_PARTY_NOTICES.md','CONTRIBUTING.md']:
    shutil.copy2(ROOT/name,destination/name)
(destination/'docs').mkdir()
for source in (ROOT/'docs').glob('*.md'):
    if source.name!='feature-status.md':shutil.copy2(source,destination/'docs'/source.name)
shutil.copytree(ROOT/'docs/images',destination/'docs/images')
(destination/'START-HERE.txt').write_text('Mimicraft Recorder '+version+' / Windows x64\n\n完整解压后双击 Mimicraft.Recorder.exe。\n保留 tools/ 与 Notices/ 文件夹，不要只复制 EXE。\n无需另装 SDK、Python 或 FFmpeg。\n本地录制无需账号。摄像头和声音按你选择的设置开启。\n当前主要在 Windows 11 x64 验证，发布包未签名。\n\nExtract the entire folder, then open Mimicraft.Recorder.exe.\nNo SDK or Python is required. Keep tools/ and Notices/.\nSee README.md and THIRD_PARTY_NOTICES.md.\n\nProject: https://github.com/zhangtonghetony/mimicraft-recorder\nDownloads and media-source materials: https://github.com/zhangtonghetony/mimicraft-recorder/releases\n',encoding='utf-8')
metadata={'exe':str(destination/'Mimicraft.Recorder.exe'),'directory':str(destination),'selfContained':True,'platform':'win-x64','utc':datetime.datetime.now(datetime.timezone.utc).isoformat()}
(ROOT/'artifacts/portable-latest.json').write_text(json.dumps(metadata,indent=2),encoding='utf-8')
print(json.dumps(metadata))

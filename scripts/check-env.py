"""Read-only diagnostic report for the local recorder development environment."""
from pathlib import Path
import json, os, subprocess

root=Path(__file__).resolve().parents[1]
dotnet=root/'.tools/dotnet/dotnet.exe'
ffmpeg=next((root/'.tools/ffmpeg').rglob('ffmpeg.exe'),None)
report={'sdk_found':dotnet.exists(),'ffmpeg_found':ffmpeg is not None,'global_path_modified':False}
env=dict(os.environ,DOTNET_ROOT=str(dotnet.parent),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
if dotnet.exists():
    result=subprocess.run([str(dotnet),'--info'],capture_output=True,text=True,encoding='utf-8',errors='replace',env=env,cwd=root,timeout=30)
    report['dotnet_info']=result.stdout
if ffmpeg:
    report['ffmpeg_path']=str(ffmpeg)
    for name in ['ffmpeg','ffprobe']:
        binary=ffmpeg.parent/(name+'.exe')
        result=subprocess.run([str(binary),'-version'],capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=15)
        report[name+'_version']=result.stdout.splitlines()[0]
        if name=='ffmpeg':report['ffmpeg_buildconf']=result.stdout
artifacts=root/'artifacts';artifacts.mkdir(exist_ok=True)
(artifacts/'environment.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in {'dotnet_info','ffmpeg_buildconf'}},ensure_ascii=True,indent=2))

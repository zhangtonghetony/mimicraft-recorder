"""Build first, then safely restart a visible WPF app. Never interrupts recording."""
from pathlib import Path
import argparse, datetime, json, os, subprocess, time

ROOT=Path(__file__).resolve().parents[1]
DOTNET=ROOT/'.tools/dotnet/dotnet.exe'
PROJECT=ROOT/'src/Mimicraft.Recorder/Mimicraft.Recorder.csproj'
DEV=ROOT/'artifacts/dev'
ENV=dict(os.environ,DOTNET_ROOT=str(DOTNET.parent),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')

def fingerprint():
    return tuple((str(p),p.stat().st_mtime_ns) for p in sorted((ROOT/'src').rglob('*')) if p.is_file() and not {'bin','obj'}.intersection(p.parts) and p.suffix in {'.cs','.xaml','.csproj','.resx','.manifest'})

def build():
    destination=DEV/('build-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
    result=subprocess.run([str(DOTNET),'build',str(PROJECT),'--no-restore','-o',str(destination),'-v','minimal'],cwd=ROOT,env=ENV)
    if result.returncode:
        print('Build failed. Existing preview stays open.',flush=True)
        return None
    return destination/'Mimicraft.Recorder.exe'

def launch(exe,verification):
    p=subprocess.Popen([str(exe)]+(['--verification'] if verification else []),cwd=ROOT,env=ENV,creationflags=subprocess.CREATE_NO_WINDOW)
    (DEV/'current.json').write_text(json.dumps({'pid':p.pid,'exe':str(exe)}),encoding='utf-8')
    print('Visible preview PID '+str(p.pid),flush=True)
    return p

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--once',action='store_true');parser.add_argument('--verification',action='store_true');args=parser.parse_args()
    DEV.mkdir(parents=True,exist_ok=True)
    if not DOTNET.exists():raise SystemExit('Run scripts/install-tools.py first.')
    previous=fingerprint();exe=build()
    if exe is None:raise SystemExit(1)
    process=launch(exe,args.verification)
    if args.once:raise SystemExit(0)
    print('Watching C#/XAML. Ctrl+C leaves the preview open.',flush=True)
    try:
        while True:
            time.sleep(0.5);current=fingerprint()
            if current==previous:continue
            time.sleep(0.7);previous=fingerprint();updated=build()
            if updated is None:continue
            # The application itself checks IsBusy before acting on this request.
            request=DEV/('state-'+str(process.pid)+'.json.close')
            request.write_text('restart after safe session completion',encoding='utf-8')
            printed=False
            while process.poll() is None:
                if not printed:print('Update ready; waiting for existing preview to close safely. Recording is not interrupted.',flush=True);printed=True
                time.sleep(0.5)
            process=launch(updated,args.verification)
    except KeyboardInterrupt:
        print('Watcher stopped; preview remains available. No recording was stopped.',flush=True)

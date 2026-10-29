"""Offline, silent media validation. Never invokes an audio renderer."""
from pathlib import Path
import fractions, json, subprocess, sys
from PIL import Image, ImageChops, ImageStat

ROOT=Path(__file__).resolve().parents[1]
TOOLS=next((ROOT/'.tools/ffmpeg').rglob('ffmpeg.exe')).parent
output=ROOT/'artifacts/media-validation'
output.mkdir(parents=True,exist_ok=True)
video=Path(sys.argv[1]) if len(sys.argv)>1 else Path(json.loads((ROOT/'artifacts/desktop-latest.json').read_text(encoding='utf-8'))['path'])

def run(args):
    r=subprocess.run(args,capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=45)
    if r.returncode: raise RuntimeError(r.stderr)
    return r.stdout

probe=json.loads(run([str(TOOLS/'ffprobe.exe'),'-v','error','-count_frames','-show_streams','-show_format','-of','json',str(video)]))
streams=probe['streams'];assert len(streams)==1 and streams[0]['codec_type']=='video','Unexpected audio or extra streams'
v=streams[0];duration=float(probe['format']['duration']);fps=float(fractions.Fraction(v['avg_frame_rate']))
assert v['codec_name']=='h264'
assert (v['width'],v['height'])==(1280,720)
assert 9.5<=duration<=10.5
assert abs(fps-30)<=1
assert abs(int(v['nb_read_frames'])/30-duration)<0.1
run([str(TOOLS/'ffmpeg.exe'),'-v','error','-i',str(video),'-an','-f','null','-'])
packets=json.loads(run([str(TOOLS/'ffprobe.exe'),'-v','error','-select_streams','v:0','-show_packets','-show_entries','packet=dts_time','-of','json',str(video)]))['packets']
dts=[float(p['dts_time']) for p in packets if 'dts_time' in p]
assert all(b>a for a,b in zip(dts,dts[1:])), 'Nonmonotonic decode timestamps'
frames=[]
for label,time in [('first',1),('middle',duration/2),('last',duration-1)]:
    file=output/(label+'.png')
    run([str(TOOLS/'ffmpeg.exe'),'-v','error','-ss',str(time),'-i',str(video),'-an','-frames:v','1','-y',str(file)])
    image=Image.open(file).convert('RGB');assert max(ImageStat.Stat(image).stddev)>15,'Blank frame'
    frames.append(image)
diffs=[sum(ImageStat.Stat(ImageChops.difference(a,b)).mean) for a,b in zip(frames,frames[1:])]
assert all(d>0.2 for d in diffs),'Frozen recording'
report={'path':str(video),'codec':v['codec_name'],'width':v['width'],'height':v['height'],'duration':duration,'average_fps':fps,'decoded_frames':v['nb_read_frames'],'audio_tracks':0,'decode':'passed','monotonic_dts':True,'frame_differences':diffs,'sound_playback':'not performed; library muted constraint'}
(output/'result.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
(output/'ffprobe.json').write_text(json.dumps(probe,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=True,indent=2))

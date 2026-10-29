using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Mimicraft.Recorder.Windows;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Xunit;
using static ExtendedDesktopTests;

public class RemainingDesktopTests:IDisposable
{
    private readonly nint _dpi=SetThreadDpiAwarenessContext(new(-4));
    private string Root=>Environment.GetEnvironmentVariable("MIMICRAFT_EV_ROOT")!;
    private Application Launch(object options,bool full=false,int fps=30,string resolution="1280 × 720",string encoder="软件 / OpenH264")
    {
        string dir=Path.Combine(Root,"artifacts/r3-ui-data/"+Guid.NewGuid());Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"settings.json"),JsonSerializer.Serialize(new{fps,resolution,encoder,options}));
        Environment.SetEnvironmentVariable("MIMICRAFT_DATA_DIR",dir);Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",full?"1":null);return Application.Launch(Path.Combine(Root,"src/Mimicraft.Recorder/bin/Debug/net10.0-windows10.0.26100.0/Mimicraft.Recorder.exe"),"--verification");
    }
    public sealed class DeviceDesktopFactAttribute:FactAttribute{public DeviceDesktopFactAttribute(){if(Environment.GetEnvironmentVariable("MIMICRAFT_DESKTOP_TESTS")!="1"||Environment.GetEnvironmentVariable("MIMICRAFT_AUDIO_TESTS")!="1"||Environment.GetEnvironmentVariable("MIMICRAFT_CAMERA_TESTS")!="1")Skip="真实桌面/声音/摄像头需全部明确启用。";}}
    public sealed class StressDesktopFactAttribute:FactAttribute{public StressDesktopFactAttribute(){if(Environment.GetEnvironmentVariable("MIMICRAFT_STRESS_TESTS")!="1"||Environment.GetEnvironmentVariable("MIMICRAFT_DESKTOP_TESTS")!="1"||Environment.GetEnvironmentVariable("MIMICRAFT_AUDIO_TESTS")!="1"||Environment.GetEnvironmentVariable("MIMICRAFT_CAMERA_TESTS")!="1")Skip="60秒真实采集压力检查需显式启用MIMICRAFT_STRESS_TESTS=1，并允许设备测试。";}}
    [StressDesktopFact]public async Task SixtySecond1080p60CameraAndAudioHaveConsistentTimestamps()
    {
        var cameras=await CameraCapture.DevicesAsync();Assert.NotEmpty(cameras);using var app=Launch(new{Audio=new{Mode="system",SampleRate=48000},Layers=new[]{new{Kind="camera",Source=cameras[0],X=.68,Y=.65,Width=.3,Height=.3,Opacity=1,Visible=true}},FloatingCamera=false},fps:60,resolution:"1920 × 1080",encoder:"自动（硬件优先）");using var automation=new UIA3Automation();var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;var before=Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").ToHashSet();
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"StartButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",40);await Task.Delay(60000);B(main,"StopButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已保存",45);var file=Assert.Single(Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").Except(before));var p=await ToolPaths.Discover().ProbeAsync(file);Assert.Equal(1920,p.Width);Assert.Equal(1080,p.Height);Assert.Equal(1,p.AudioTracks);Assert.InRange(p.Duration,59,62);
            using var metadata=JsonDocument.Parse(p.Json);var streams=metadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();double Duration(JsonElement s)=>double.Parse(s.GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture);double drift=Math.Abs(Duration(streams.First(s=>s.GetProperty("codec_type").GetString()=="video"))-Duration(streams.First(s=>s.GetProperty("codec_type").GetString()=="audio")));Assert.InRange(drift,0,.15);
            var decode=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-i",file,"-f","null","-"],90);Assert.Equal(0,decode.ExitCode);Directory.CreateDirectory(Path.Combine(Root,"artifacts/r3-private"));File.WriteAllText(Path.Combine(Root,"artifacts/r3-private/stress-result.json"),JsonSerializer.Serialize(new{file,p.Duration,p.Width,p.Height,p.AudioTracks,driftSeconds=drift}));
        }
        finally{Close(app,main);}
    }
    [DeviceDesktopFact]public async Task CameraAndDualAudioProduceAvRecordingAndPlaybackRendersSound()
    {
        var cameras=await CameraCapture.DevicesAsync();Assert.NotEmpty(cameras);
        using var app=Launch(new{Audio=new{Mode="both",SampleRate=48000,BitrateKbps=128,Enhance=true,Denoise=false},Layers=new[]{new{Kind="camera",Source=cameras[0],X=.68,Y=.65,Width=.3,Height=.3,Opacity=1,Visible=true}},FloatingCamera=true});
        using var automation=new UIA3Automation();var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;var before=Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").ToHashSet();
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"StartButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",40);var camera=Owned(automation,app,"Mimicraft · 摄像头浮窗");Assert.True(camera.IsAvailable);
            await Task.Delay(900);B(main,"AvMarkerButton").Invoke();await Task.Delay(1800);
            B(main,"StopButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已保存",40);
            var file=Assert.Single(Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").Except(before));var probe=await ToolPaths.Discover().ProbeAsync(file);Assert.Equal(1,probe.AudioTracks);Assert.InRange(probe.Duration,2.5,5);
            var decode=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-i",file,"-f","null","-"]);Assert.Equal(0,decode.ExitCode);
            var gray=file+".gray";var pcm=file+".f32";
            var video=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-i",file,"-vf","scale=64:36,format=gray","-an","-f","rawvideo","-y",gray]);Assert.Equal(0,video.ExitCode);
            var audio=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-i",file,"-vn","-ac","1","-ar","16000","-f","f32le","-y",pcm]);Assert.Equal(0,audio.ExitCode);
            var frames=await File.ReadAllBytesAsync(gray);double? flashTime=null;for(int frame=0;frame<frames.Length/2304;frame++){double mean=0;for(int p=0;p<2304;p++)mean+=frames[frame*2304+p];if(mean/2304>155){flashTime=frame/30d;break;}}Assert.NotNull(flashTime);
            var audioBytes=await File.ReadAllBytesAsync(pcm);var samples=new float[audioBytes.Length/4];Buffer.BlockCopy(audioBytes,0,samples,0,audioBytes.Length);double? toneTime=null;int window=320;
            int sustained=0;
            for(int offset=0;offset+window<samples.Length;offset+=160)
            {
                double Amplitude(int frequency){double real=0,imag=0;for(int i=0;i<window;i++){double angle=2*Math.PI*frequency*i/16000;real+=samples[offset+i]*Math.Cos(angle);imag+=samples[offset+i]*Math.Sin(angle);}return Math.Sqrt(real*real+imag*imag)*2/window;}
                double signal=Amplitude(880),noise=Math.Max(Amplitude(660),Amplitude(1000));
                sustained=signal>.003&&signal>4*noise?sustained+1:0;
                if(sustained>=4){toneTime=(offset-3*160)/16000d;break;}
            }
            Assert.NotNull(toneTime);double syncMs=Math.Abs(toneTime!.Value-flashTime!.Value)*1000;Assert.InRange(syncMs,0,200);
            B(main,"PlayButton").Invoke();var player=Owned(automation,app,"Mimicraft · 静音播放");
            using var playbackCapture=new WasapiLoopbackCapture();double peak=0;playbackCapture.DataAvailable+=(_,e)=>{var wave=new WaveBuffer(e.Buffer);for(int i=0;i<e.BytesRecorded/4;i++)peak=Math.Max(peak,Math.Abs(wave.FloatBuffer[i]));};playbackCapture.StartRecording();
            player.FindFirstDescendant(cf=>cf.ByAutomationId("PlayerAudio"))!.AsCheckBox().IsChecked=true;
            await Task.Delay(1800);playbackCapture.StopRecording();Assert.True(peak>.00001,$"Playback loopback remained silent: {peak}");
            Directory.CreateDirectory(Path.Combine(Root,"artifacts/r3-private"));File.WriteAllText(Path.Combine(Root,"artifacts/r3-private/av-result.json"),JsonSerializer.Serialize(new{file,probe.Duration,probe.AudioTracks,playbackPeak=peak,flashTime,toneTime,syncMs}));player.Close();
        }
        finally{Close(app,main);}
    }
    [DesktopFact]public void AdvancedEffectsStampAndTrayPreserveRecordingSession()
    {
        using var app=Launch(new{});using var automation=new UIA3Automation();var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"FloatingControls").Invoke();var controls=Owned(automation,app,"Mimicraft · 快捷控制");B(controls,"ToggleToolbar").Invoke();B(controls,"ToggleToolbar").Invoke();B(controls,"EffectCountdown").Invoke();
            foreach(var id in new[]{"SpotlightEffect","MagnifierEffect","MouseEffect","KeyboardEffect"})controls.FindFirstDescendant(cf=>cf.ByAutomationId(id))!.AsCheckBox().IsChecked=true;
            B(controls,"FloatStart").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);var source=Owned(automation,app,"Mimicraft · 动态练习画面（无声）");source.Focus();var bounds=source.BoundingRectangle;Mouse.Click(new System.Drawing.Point((int)bounds.Left+100,(int)bounds.Top+100));Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL,VirtualKeyShort.KEY_K);Thread.Sleep(450);B(controls,"CaptureStamp").Invoke();
            var picker=Owned(automation,app,"Mimicraft · 框选截图");picker.Focus();Keyboard.Press(VirtualKeyShort.ESCAPE);Keyboard.Release(VirtualKeyShort.ESCAPE);
            B(main,"HideToTray").Invoke();Thread.Sleep(700);using(var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,$"artifacts/dev/state-{app.ProcessId}.json")))){Assert.True(state.RootElement.GetProperty("busy").GetBoolean());Assert.Equal("Recording",state.RootElement.GetProperty("state").GetString());}var hwnd=FindWindow(null,"Mimicraft Recorder");GetWindowThreadProcessId(hwnd,out var owner);Assert.Equal(app.ProcessId,owner);Assert.False(IsWindowVisible(hwnd));PostMessage(hwnd,0x804d,1,0x203);Wait(()=>IsWindowVisible(hwnd),5);
            Thread.Sleep(1200);B(controls,"FloatStop").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已保存",35);Capture.Element(main).ToFile(Path.Combine(Root,"artifacts/screenshots/r3-effects-completed.png"));controls.Close();
        }
        finally{Close(app,main);}
    }
    [DesktopFact]public async Task SplitLayoutCombinesLiveSecondSourceAndTextLayer()
    {
        using var app=Launch(new{Layout="split",Layers=new[]{new{Kind="screen",Source="显示器 1",X=.5,Y=0d,Width=.5,Height=1d,Opacity=1d,Visible=true},new{Kind="text",Source="SPLIT LAYOUT",X=.1,Y=.03,Width=.4,Height=.1,Opacity=1d,Visible=true}}},true);
        using var automation=new UIA3Automation();var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;var before=Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").ToHashSet();
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"StartButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);await Task.Delay(2200);B(main,"StopButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已保存",35);
            var file=Assert.Single(Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").Except(before));var p=await ToolPaths.Discover().ProbeAsync(file);Assert.InRange(p.Duration,1.8,3.5);var raw=file+".rgb";var decode=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-ss","0.5","-i",file,"-frames:v","1","-f","rawvideo","-pix_fmt","rgb24","-y",raw]);Assert.Equal(0,decode.ExitCode);var bytes=await File.ReadAllBytesAsync(raw);double left=0,right=0;for(int y=50;y<650;y+=10)for(int x=50;x<590;x+=10){left+=bytes[(y*1280+x)*3+1];right+=bytes[(y*1280+x+640)*3+1];}Assert.True(left>100000&&right>100000,"Both halves should contain a live picture, not an empty placeholder.");
        }
        finally{Close(app,main);Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",null);}
    }
    private static Window Owned(UIA3Automation automation,Application app,string title){nint hwnd=0;Wait(()=>{hwnd=FindWindow(null,title);GetWindowThreadProcessId(hwnd,out int pid);return hwnd!=0&&pid==app.ProcessId;},15);return automation.FromHandle(hwnd).AsWindow();}
    private static void Close(Application app,Window main){try{if(B(main,"StopButton").IsEnabled){B(main,"StopButton").Invoke();Wait(()=>!B(main,"StopButton").IsEnabled,30);}main.Close();}catch{}if(!app.HasExited){try{Process.GetProcessById(app.ProcessId).WaitForExit(5000);}catch{}if(!app.HasExited)app.Kill();}}
    public void Dispose(){if(_dpi!=0)SetThreadDpiAwarenessContext(_dpi);}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint FindWindow(string? c,string title);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint hwnd,out int pid);
    [DllImport("user32.dll")]private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")]private static extern bool PostMessage(nint hwnd,int msg,nint w,nint l);
}

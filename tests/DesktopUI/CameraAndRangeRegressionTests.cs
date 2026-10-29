using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;
using Mimicraft.Recorder.Windows;
using Xunit;
using static ExtendedDesktopTests;

public sealed class CameraAndRangeRegressionTests:IDisposable
{
    private readonly nint _dpi=SetThreadDpiAwarenessContext(new(-4));
    private string Root=>Environment.GetEnvironmentVariable("MIMICRAFT_EV_ROOT")!;
    private string NewData(object options){var path=Path.Combine(Root,"artifacts/r5-data/"+Guid.NewGuid());Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"settings.json"),JsonSerializer.Serialize(new{fps=30,resolution="1280 × 720",encoder="软件 / OpenH264",options}));return path;}
    private Application Launch(string data,bool full=false){Environment.SetEnvironmentVariable("MIMICRAFT_DATA_DIR",data);Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",full?"1":null);return Application.Launch(Environment.GetEnvironmentVariable("MIMICRAFT_TEST_APP")??Path.Combine(Root,"src/Mimicraft.Recorder/bin/Debug/net10.0-windows10.0.26100.0/Mimicraft.Recorder.exe"),"--verification");}
    private async Task<string> Record(Window main)
    {
        var files=Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").ToHashSet();B(main,"StartButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);await Task.Delay(1600);B(main,"StopButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已保存",35);return Assert.Single(Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").Except(files));
    }
    private async Task<int> LeftGreen(string file,string name)
    {
        var directory=Path.Combine(Root,"artifacts/r5");Directory.CreateDirectory(directory);var raw=Path.Combine(directory,name+".rgb");var d=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-ss","0.5","-i",file,"-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","-y",raw]);Assert.Equal(0,d.ExitCode);var bytes=await File.ReadAllBytesAsync(raw);return bytes[(300*1280+5)*3+1];
    }
    [DesktopFact]public async Task SavedCropDoesNotSurviveRestartAndResetRestoresFullScreenInSameProcess()
    {
        using var a=new UIA3Automation();using var fixture=Launch(NewData(new{}),true);var fixtureMain=fixture.GetMainWindow(a,TimeSpan.FromSeconds(25))!;B(fixtureMain,"PracticeButton").Invoke();
        var data=NewData(new{Region=new{X=200,Y=200,Width=480,Height=320}});Application? app=null;Window? main=null;
        try
        {
            app=Launch(data);main=app.GetMainWindow(a,TimeSpan.FromSeconds(25))!;
            var initial=await Record(main);Assert.True(await LeftGreen(initial,"restart-old-settings")>40,"Old saved crop is still silently applied after startup.");
            B(main,"RegionFields").Invoke();var fields=Owned(a,app,"Mimicraft · 精确取景");E(fields,"RegionX").Text="200";E(fields,"RegionY").Text="200";E(fields,"RegionWidth").Text="480";E(fields,"RegionHeight").Text="320";B(fields,"ApplyRegion").Invoke();
            Assert.Contains("480",Text(main,"CaptureRangeStatus"));var cropped=await Record(main);Assert.True(await LeftGreen(cropped,"selected-crop")<10);
            B(main,"ResetRegionButton").Invoke();Assert.Contains("整屏",Text(main,"CaptureRangeStatus"));var restored=await Record(main);Assert.True(await LeftGreen(restored,"reset-full-screen")>40);
            main.Close();Assert.True(Process.GetProcessById(app.ProcessId).WaitForExit(5000));app.Dispose();app=Launch(data);main=app.GetMainWindow(a,TimeSpan.FromSeconds(25))!;Assert.Contains("整屏",Text(main,"CaptureRangeStatus"));var restarted=await Record(main);Assert.True(await LeftGreen(restarted,"restarted-full-screen")>40);
            using var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(data,"settings.json")));Assert.False(settings.RootElement.GetProperty("options").TryGetProperty("Region",out var stored)&&stored.ValueKind!=JsonValueKind.Null);
            File.WriteAllText(Path.Combine(Root,"artifacts/r5/range-result.json"),JsonSerializer.Serialize(new{initial,cropped,restored,restarted}));
        }
        finally{if(app is not null){Close(app,main);app.Dispose();}Close(fixture,fixtureMain);}
    }
    public sealed class CameraDesktopFactAttribute:FactAttribute{public CameraDesktopFactAttribute(){if(Environment.GetEnvironmentVariable("MIMICRAFT_DESKTOP_TESTS")!="1"||Environment.GetEnvironmentVariable("MIMICRAFT_CAMERA_TESTS")!="1")Skip="需显式允许真实桌面和摄像头。";}}
    [CameraDesktopFact]public async Task GuidedCameraPreviewRecordsInFullScreenAndCropAndCanBeDisabled()
    {
        var data=NewData(new{});using var app=Launch(data,true);using var a=new UIA3Automation();var main=app.GetMainWindow(a,TimeSpan.FromSeconds(25))!;
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"PrimaryCameraSettings").Invoke();var setup=Owned(a,app,"Mimicraft · 摄像头画中画");
            var devices=setup.FindFirstDescendant(c=>c.ByAutomationId("CameraDevice"))!.AsComboBox();Wait(()=>devices.Items.Length>0,15);Assert.True(devices.SelectedItem is not null);Assert.Contains("尚未自动打开",Text(setup,"CameraSetupStatus"));
            Assert.Equal(0,FindWindow(null,"Mimicraft · 摄像头浮窗"));
            B(setup,"EnableCamera").Invoke();Wait(()=>Text(setup,"CameraSetupStatus").Contains("已加入录制")||Text(setup,"CameraSetupStatus").Contains("全黑")||Text(setup,"CameraSetupStatus").Contains("未能开启"),25);
            Assert.Contains("已加入录制",Text(setup,"CameraSetupStatus"));var camera=Owned(a,app,"Mimicraft · 摄像头浮窗");Assert.True(camera.IsAvailable);
            Directory.CreateDirectory(Path.Combine(Root,"artifacts/r5-private"));Capture.Element(setup).ToFile(Path.Combine(Root,"artifacts/r5-private/camera-setup.png"));B(setup,"FinishCameraSetup").Invoke();Assert.Contains("将录入",Text(main,"CameraRecordingStatus"));
            var full=await Record(main);
            B(main,"RegionFields").Invoke();var fields=Owned(a,app,"Mimicraft · 精确取景");E(fields,"RegionX").Text="800";E(fields,"RegionY").Text="600";E(fields,"RegionWidth").Text="480";E(fields,"RegionHeight").Text="320";B(fields,"ApplyRegion").Invoke();var cropped=await Record(main);
            B(main,"PrimaryCameraSettings").Invoke();setup=Owned(a,app,"Mimicraft · 摄像头画中画");B(setup,"DisableCamera").Invoke();Wait(()=>Text(setup,"CameraSetupStatus").Contains("已关闭"),15);Assert.Equal(0,FindWindow(null,"Mimicraft · 摄像头浮窗"));B(setup,"FinishCameraSetup").Invoke();Assert.Contains("未加入",Text(main,"CameraRecordingStatus"));var off=await Record(main);
            async Task<byte[]> Frame(string file,string name){string path=Path.Combine(Root,"artifacts/r5-private/"+name+".rgb");var d=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-ss","0.5","-i",file,"-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","-y",path]);Assert.Equal(0,d.ExitCode);return await File.ReadAllBytesAsync(path);}
            var f=await Frame(full,"camera-full");var c=await Frame(cropped,"camera-crop");var o=await Frame(off,"camera-off");int fullChanged=0,cropChanged=0;var colors=new HashSet<int>();
            for(int y=555;y<680;y++)for(int x=920;x<1110;x++){int p=(y*1280+x)*3;if(Math.Abs(c[p]-o[p])+Math.Abs(c[p+1]-o[p+1])+Math.Abs(c[p+2]-o[p+2])>35)cropChanged++;if(Math.Abs(f[p]-29)+Math.Abs(f[p+1]-59)+Math.Abs(f[p+2]-48)>35)fullChanged++;colors.Add((c[p]/16<<8)|(c[p+1]/16<<4)|(c[p+2]/16));}
            Assert.True(fullChanged>2000,$"Camera missing in full recording: {fullChanged}");Assert.True(cropChanged>2000,$"Camera was cropped out of selected region: {cropChanged}");Assert.True(colors.Count>16,$"Camera image has too little variation: {colors.Count}");
            using var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(data,"settings.json")));Assert.DoesNotContain(settings.RootElement.GetProperty("options").GetProperty("Layers").EnumerateArray(),l=>l.GetProperty("Kind").GetString()=="camera");
            File.WriteAllText(Path.Combine(Root,"artifacts/r5-private/camera-result.json"),JsonSerializer.Serialize(new{full,cropped,off,fullChanged,cropChanged,colors=colors.Count}));
            Owned(a,app,"Mimicraft · 动态练习画面（无声）").Close();main.FindFirstDescendant(c=>c.ByAutomationId("RecordingList"))!.AsListBox().Select(1);B(main,"PlayButton").Invoke();var player=Owned(a,app,"Mimicraft · 静音播放");player.Focus();Thread.Sleep(700);Capture.Element(player).ToFile(Path.Combine(Root,"artifacts/r5-private/camera-crop-playback.png"));player.Close();
        }
        finally{Close(app,main);}
    }
    private static Window Owned(UIA3Automation a,Application app,string title){nint h=0;Wait(()=>{h=FindWindow(null,title);GetWindowThreadProcessId(h,out var pid);return h!=0&&pid==app.ProcessId;},15);return a.FromHandle(h).AsWindow();}
    [DesktopFact]public void CancelWaitOnlyAppliesToCountdownOrPendingSchedule()
    {
        using var app=Launch(NewData(new{}),true);using var a=new UIA3Automation();var main=app.GetMainWindow(a,TimeSpan.FromSeconds(25))!;
        try
        {
            Assert.False(B(main,"CancelWait").IsEnabled);B(main,"PracticeButton").Invoke();B(main,"ScheduleButton").Invoke();var schedule=Owned(a,app,"Mimicraft · 时间安排");E(schedule,"CountdownSeconds").Text="8";B(schedule,"ApplySchedule").Invoke();B(main,"StartButton").Invoke();Wait(()=>Text(main,"StatusMessage").Contains("秒后开始"),8);Assert.True(B(main,"CancelWait").IsEnabled);B(main,"CancelWait").Invoke();Wait(()=>B(main,"StartButton").IsEnabled,5);Assert.False(B(main,"StopButton").IsEnabled);Assert.False(B(main,"CancelWait").IsEnabled);
            B(main,"ScheduleButton").Invoke();schedule=Owned(a,app,"Mimicraft · 时间安排");E(schedule,"CountdownSeconds").Text="0";E(schedule,"ScheduledStart").Text=DateTime.Now.AddSeconds(40).ToString("yyyy-MM-dd HH:mm:ss");B(schedule,"ApplySchedule").Invoke();Assert.True(B(main,"CancelWait").IsEnabled);B(main,"CancelWait").Invoke();Wait(()=>!B(main,"CancelWait").IsEnabled,5);Assert.Contains("取消",Text(main,"StatusMessage"));
        }
        finally{Close(app,main);}
    }
    private static void Close(Application app,Window? main){try{if(main is not null){if(B(main,"StopButton").IsEnabled){B(main,"StopButton").Invoke();Wait(()=>!B(main,"StopButton").IsEnabled,30);}main.Close();}}catch{}if(!app.HasExited&&!Process.GetProcessById(app.ProcessId).WaitForExit(5000))app.Kill();}
    public void Dispose(){Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",null);if(_dpi!=0)SetThreadDpiAwarenessContext(_dpi);}
    [DllImport("user32.dll")]private static extern nint SetThreadDpiAwarenessContext(nint value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint FindWindow(string? cls,string title);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint hwnd,out int pid);
}

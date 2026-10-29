using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Xunit;

public class ExtendedDesktopTests:IDisposable
{
    private readonly nint _dpi=SetThreadDpiAwarenessContext(new(-4));
    public void Dispose(){if(_dpi!=0)SetThreadDpiAwarenessContext(_dpi);}
    private string Root=>Environment.GetEnvironmentVariable("MIMICRAFT_EV_ROOT")!;
    private string Exe=>Path.Combine(Root,"src/Mimicraft.Recorder/bin/Debug/net10.0-windows10.0.26100.0/Mimicraft.Recorder.exe");
    private Application Launch(object options,bool full=false)
    {
        var dir=Path.Combine(Root,"artifacts/r2-ui-data/"+Guid.NewGuid());Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"settings.json"),JsonSerializer.Serialize(new{fps=30,resolution="1280 × 720",encoder="软件 / OpenH264",options}));
        Environment.SetEnvironmentVariable("MIMICRAFT_DATA_DIR",dir);Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",full?"1":null);return Application.Launch(Exe,"--verification");
    }
    [DesktopFact]public void PauseFreezesTimelineAndStopFromPausedPublishesCroppedMkv()
    {
        using var app=Launch(new{Region=new{X=60,Y=60,Width=480,Height=320},Container="mkv",Profile="baseline",Saturation=0,WatermarkText="MIMICRAFT TEST",MarkX=.1,MarkY=.1,MarkSize=32,MarkOpacity=1});
        using var automation=new UIA3Automation();var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;
        var output=Path.Combine(Root,"artifacts/recordings");var before=Directory.GetFiles(output,"*.mkv").ToHashSet();
        try
        {
            B(main,"PracticeButton").Invoke();
            B(main,"RegionFields").Invoke();var region=Owned(automation,app,"Mimicraft · 精确取景");E(region,"RegionX").Text="60";E(region,"RegionY").Text="60";E(region,"RegionWidth").Text="480";E(region,"RegionHeight").Text="320";B(region,"ApplyRegion").Invoke();
            B(main,"StartButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);
            Wait(()=>Text(main,"ElapsedTime")=="00:00:02",8);B(main,"PauseButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已暂停",5);
            string time=Text(main,"ElapsedTime");Thread.Sleep(2200);Assert.Equal(time,Text(main,"ElapsedTime"));Assert.False(B(main,"StartButton").IsEnabled);
            B(main,"PauseButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",5);Thread.Sleep(1000);B(main,"PauseButton").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已暂停",5);
            B(main,"StopButton").Invoke();Wait(()=>Text(main,"RecordingStatus") is "已保存" or "请检查提示",40);Assert.Equal("已保存",Text(main,"RecordingStatus"));
            var file=Assert.Single(Directory.GetFiles(output,"*.mkv").Except(before));using var probe=JsonDocument.Parse(File.ReadAllText(file+".probe.json"));var duration=double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,CultureInfo.InvariantCulture);Assert.InRange(duration,2.8,4.2);
            Directory.CreateDirectory(Path.Combine(Root,"artifacts/screenshots"));Capture.Element(main).ToFile(Path.Combine(Root,"artifacts/screenshots/r2-paused-completed.png"));
        }
        finally{Close(app,main);}
    }
    [DesktopFact]public void CountdownCanCancelAndFloatingControlsCanStartAndAutoStop()
    {
        using var app=Launch(new{CountdownSeconds=2,StopAfterSeconds=2});using var automation=new UIA3Automation();var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"StartButton").Invoke();Wait(()=>Text(main,"StatusMessage").Contains("秒后"),5);B(main,"CancelWait").Invoke();Wait(()=>B(main,"StartButton").IsEnabled,5);Assert.Equal("准备就绪",Text(main,"RecordingStatus"));
            B(main,"FloatingControls").Invoke();var controls=Owned(automation,app,"Mimicraft · 快捷控制");B(controls,"FloatStart").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);Wait(()=>Text(main,"RecordingStatus")=="已保存",15);
            Capture.Element(controls).ToFile(Path.Combine(Root,"artifacts/screenshots/r2-floating-controls.png"));controls.Close();
        }
        finally{Close(app,main);}
    }
    [DesktopFact]public void EditorTrimsSplitsUndoesAndExportsThroughRealControls()
    {
        using var app=Launch(new{});using var automation=new UIA3Automation();var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;
        try
        {
            Wait(()=>main.FindFirstDescendant(cf=>cf.ByAutomationId("RecordingList"))!.AsListBox().Items.Length>0,20);
            main.FindFirstDescendant(cf=>cf.ByAutomationId("RecordingList"))!.AsListBox().Select(0);
            B(main,"EditorButton").Invoke();var editor=Owned(automation,app,"Mimicraft · 片段工坊");
            Wait(()=>Text(editor,"EditStatus").StartsWith("已打开"),20);
            E(editor,"TrimStart").Text="0";E(editor,"TrimEnd").Text="1.6";B(editor,"ApplyTrim").Invoke();E(editor,"SplitTime").Text="0.8";B(editor,"SplitClip").Invoke();
            Assert.Equal(2,editor.FindFirstDescendant(cf=>cf.ByAutomationId("EditPieces"))!.AsListBox().Items.Length);B(editor,"EditUndo").Invoke();Assert.Single(editor.FindFirstDescendant(cf=>cf.ByAutomationId("EditPieces"))!.AsListBox().Items);B(editor,"EditRedo").Invoke();
            E(editor,"ExportSpeed").Text="2";string output=Path.Combine(Root,"artifacts/recordings/ui-edit-"+Guid.NewGuid()+".mp4");E(editor,"ExportPath").Text=output;B(editor,"ExportMedia").Invoke();Wait(()=>Text(editor,"EditStatus").StartsWith("导出完成"),40);Assert.True(File.Exists(output));
            Capture.Element(editor).ToFile(Path.Combine(Root,"artifacts/screenshots/r2-editor-export.png"));editor.Close();
        }
        finally{Close(app,main);}
    }
    [DesktopFact]public void DesktopAnnotationsAndJpegWatermarkAppearInRealWindowCapture()
    {
        var ffmpeg=Directory.GetFiles(Path.Combine(Root,".tools/ffmpeg"),"ffmpeg.exe",SearchOption.AllDirectories).Single();
        string fixture=Path.Combine(Root,"artifacts/blue-watermark.jpg");
        Run(ffmpeg,["-v","error","-f","lavfi","-i","color=c=blue:s=120x60","-frames:v","1","-y",fixture]);
        using var app=Launch(new{WatermarkText="独立画面标记",WatermarkImage=fixture,MarkX=.65,MarkY=.12,MarkSize=32,MarkOpacity=1});using var automation=new UIA3Automation();var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;
        var output=Path.Combine(Root,"artifacts/recordings");var before=Directory.GetFiles(output,"*.mp4").ToHashSet();
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"FloatingControls").Invoke();var controls=Owned(automation,app,"Mimicraft · 快捷控制");
            controls.FindFirstDescendant(cf=>cf.ByAutomationId("InkWidth"))!.AsComboBox().Select("12");
            controls.FindFirstDescendant(cf=>cf.ByAutomationId("InkTool"))!.AsComboBox().Select("线");var annotation=Owned(automation,app,"Mimicraft · 屏幕批注");
            annotation.Focus();var rect=annotation.BoundingRectangle;
            Mouse.Drag(new System.Drawing.Point((int)rect.Left+100,(int)rect.Bottom-95),new System.Drawing.Point((int)rect.Left+440,(int)rect.Bottom-95),MouseButton.Left);
            B(controls,"InkUndo").Invoke();B(controls,"InkRedo").Invoke();B(controls,"FloatStart").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);Thread.Sleep(2100);B(controls,"FloatStop").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已保存",35);
            annotation.Close();controls.Close();var file=Assert.Single(Directory.GetFiles(output,"*.mp4").Except(before));
            var raw=Path.Combine(Root,"artifacts/annotations-frame.rgb");Run(ffmpeg,["-v","error","-ss","0.5","-i",file,"-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","-y",raw]);var pixels=File.ReadAllBytes(raw);int red=0,blue=0;
            for(int i=0;i<pixels.Length;i+=3){if(pixels[i]>200&&pixels[i+1]<105&&pixels[i+2]<130)red++;if(pixels[i+2]>200&&pixels[i]<80&&pixels[i+1]<80)blue++;}
            Assert.True(red>1000,$"Rendered annotation not found; red={red}");Assert.True(blue>2000,$"JPEG watermark not found; blue={blue}");
            var png=Path.Combine(Root,"artifacts/screenshots/r2-composited-frame.png");Run(ffmpeg,["-v","error","-ss","0.5","-i",file,"-frames:v","1","-y",png]);
            B(main,"PlayButton").Invoke();var player=Owned(automation,app,"Mimicraft · 静音播放");Thread.Sleep(800);Capture.Element(player).ToFile(Path.Combine(Root,"artifacts/screenshots/r2-composited-playback.png"));player.Close();
        }
        finally{Close(app,main);}
    }
    [DesktopFact]public void MonitorRegionCanMoveDuringCaptureAndKeyboardShortcutCanPause()
    {
        using var app=Launch(new{},true);using var automation=new UIA3Automation();var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"RegionFields").Invoke();var region=Owned(automation,app,"Mimicraft · 精确取景");E(region,"RegionX").Text="60";E(region,"RegionY").Text="60";E(region,"RegionWidth").Text="480";E(region,"RegionHeight").Text="320";B(region,"ApplyRegion").Invoke();
            B(main,"FloatingControls").Invoke();var controls=Owned(automation,app,"Mimicraft · 快捷控制");B(controls,"FloatStart").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);
            B(controls,"MoveRegion").Invoke();var mover=Owned(automation,app,"Mimicraft · 移动取景框");var rect=mover.BoundingRectangle;Mouse.Drag(new System.Drawing.Point((int)rect.Left+90,(int)rect.Top+12),new System.Drawing.Point((int)rect.Left+210,(int)rect.Top+72),MouseButton.Left);
            Thread.Sleep(700);mover.Close();main.Focus();FlaUI.Core.Input.Keyboard.TypeSimultaneously(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,FlaUI.Core.WindowsAPI.VirtualKeyShort.ALT,FlaUI.Core.WindowsAPI.VirtualKeyShort.F7);Wait(()=>Text(main,"RecordingStatus")=="已暂停",8);
            B(controls,"FloatStop").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已保存",35);Capture.Element(main).ToFile(Path.Combine(Root,"artifacts/screenshots/r2-monitor-region.png"));controls.Close();
        }
        finally{Close(app,main);Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",null);}
    }
    [DesktopFact]public void OneShotScheduleStartsAndStopsWithoutUserClickingStart()
    {
        using var app=Launch(new{});using var automation=new UIA3Automation();var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"ScheduleButton").Invoke();var dialog=Owned(automation,app,"Mimicraft · 时间安排");E(dialog,"StopAfterSeconds").Text="2";E(dialog,"ScheduledStart").Text=DateTime.Now.AddSeconds(5).ToString("yyyy-MM-dd HH:mm:ss");B(dialog,"ApplySchedule").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);Wait(()=>Text(main,"RecordingStatus")=="已保存",15);
        }
        finally{Close(app,main);}
    }
    private static void Run(string executable,IEnumerable<string> args)
    {
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};foreach(var arg in args)info.ArgumentList.Add(arg);using var p=Process.Start(info)!;var diagnostic=p.StandardError.ReadToEnd();Assert.True(p.WaitForExit(30000),"Fixture tool timeout");Assert.True(p.ExitCode==0,diagnostic);
    }
    private static AutomationElement Element(Window w,string id)
    {
        AutomationElement? element=null;Wait(()=>{element=w.FindFirstDescendant(cf=>cf.ByAutomationId(id));return element is not null;},8);return element!;
    }
    public static Button B(Window w,string id)=>Element(w,id).AsButton();
    public static TextBox E(Window w,string id)=>Element(w,id).AsTextBox();
    public static string Text(Window w,string id)=>w.FindFirstDescendant(cf=>cf.ByAutomationId(id))?.Name??"";
    public static void Wait(Func<bool> f,int seconds){var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<seconds){if(f())return;Thread.Sleep(100);}Assert.Fail("等待桌面行为超时。");}
    private static Window Owned(UIA3Automation automation,Application app,string title)
    {
        nint handle=0;Wait(()=>{handle=FindWindow(null,title);GetWindowThreadProcessId(handle,out int pid);return handle!=0&&pid==app.ProcessId;},15);return automation.FromHandle(handle).AsWindow();
    }
    private static void Close(Application app,Window main)
    {
        try{if(B(main,"StopButton").IsEnabled){B(main,"StopButton").Invoke();Wait(()=>!B(main,"StopButton").IsEnabled,30);}main.Close();}catch{}
        if(!app.HasExited){try{Process.GetProcessById(app.ProcessId).WaitForExit(5000);}catch{}if(!app.HasExited)app.Kill();}
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint FindWindow(string? cls,string title);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint hwnd,out int pid);
    [DllImport("user32.dll")]private static extern nint SetThreadDpiAwarenessContext(nint dpi);
}

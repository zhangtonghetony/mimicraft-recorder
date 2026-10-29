using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;
using Mimicraft.Recorder.Windows;
using Xunit;
using static ExtendedDesktopTests;

public sealed class AnnotationRegressionTests:IDisposable
{
    private readonly nint _dpi=SetThreadDpiAwarenessContext(new(-4));
    private string Root=>Environment.GetEnvironmentVariable("MIMICRAFT_EV_ROOT")!;
    private string _data="";
    private Application Launch(bool full=false)
    {
        string data=Path.Combine(Root,"artifacts/r4-data/"+Guid.NewGuid());Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data,"settings.json"),JsonSerializer.Serialize(new{fps=30,resolution="1280 × 720",encoder="软件 / OpenH264",options=new{}}));
        _data=data;Environment.SetEnvironmentVariable("MIMICRAFT_DATA_DIR",data);Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",full?"1":null);
        return Application.Launch(Environment.GetEnvironmentVariable("MIMICRAFT_TEST_APP")??Path.Combine(Root,"src/Mimicraft.Recorder/bin/Debug/net10.0-windows10.0.26100.0/Mimicraft.Recorder.exe"),"--verification");
    }
    [DesktopFact]public async Task RegionalStampCanCancelSelectDragUndoRedoAndReachRecordedVideo()
    {
        using var app=Launch(true);using var a=new UIA3Automation();var main=app.GetMainWindow(a,TimeSpan.FromSeconds(20))!;
        var before=Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").ToHashSet();
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"FloatingControls").Invoke();var controls=Owned(a,app,"Mimicraft · 快捷控制");
            controls.Patterns.Transform.Pattern.Move(900,10);
            controls.Focus();Thread.Sleep(250);
            var buttonRect=B(controls,"CaptureStamp").BoundingRectangle;var hit=WindowFromPoint(new((int)(buttonRect.Left+buttonRect.Width/2),(int)(buttonRect.Top+buttonRect.Height/2)));Assert.True(GetAncestor(hit,2)==controls.Properties.NativeWindowHandle.Value,$"Screenshot button is covered by HWND {GetAncestor(hit,2)}; controls HWND {controls.Properties.NativeWindowHandle.Value}; rect {buttonRect}");
            B(controls,"CaptureStamp").Click();var picker=Owned(a,app,"Mimicraft · 框选截图");picker.Focus();Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);Thread.Sleep(200);
            Assert.False(Directory.Exists(Path.Combine(_data,"Clips")));
            controls.Focus();B(controls,"CaptureStamp").Click();picker=Owned(a,app,"Mimicraft · 框选截图");var rect=picker.BoundingRectangle;
            double dpi=GetDpiForWindow(Owned(a,app,"Mimicraft · 动态练习画面（无声）").Properties.NativeWindowHandle.Value)/96d;
            System.Drawing.Point P(int x,int y)=>new((int)(rect.Left+x*dpi),(int)(rect.Top+y*dpi));
            Mouse.Drag(P(80,350),P(280,410),MouseButton.Left);
            Wait(()=>Directory.Exists(Path.Combine(_data,"Clips"))&&Directory.GetFiles(Path.Combine(_data,"Clips"),"*.png").Length==1,8);
            var file=Directory.GetFiles(Path.Combine(_data,"Clips"),"*.png").Single();using(var bitmap=new System.Drawing.Bitmap(file)){Assert.InRange(bitmap.Width,(int)(200*dpi)-2,(int)(200*dpi)+2);Assert.InRange(bitmap.Height,(int)(60*dpi)-2,(int)(60*dpi)+2);}
            var annotation=Owned(a,app,"Mimicraft · 屏幕批注");Assert.Equal("移动",controls.FindFirstDescendant(c=>c.ByAutomationId("InkTool"))!.AsComboBox().SelectedItem!.Text);
            annotation.Focus();Mouse.Drag(P(140,380),P(740,580),MouseButton.Left);Thread.Sleep(300);
            var evidence=Path.Combine(Root,"artifacts/r4");Directory.CreateDirectory(evidence);var screen=Path.Combine(evidence,"stamp-dragged.png");Capture.Element(annotation).ToFile(screen);
            // Excluded overlay HWNDs intentionally do not appear in screen captures.
            // Validate the moved stamp and undo/redo in actual encoded output below.
            // Controls must remain physically clickable above the annotation canvas.
            var mag=controls.FindFirstDescendant(c=>c.ByAutomationId("MagnifierEffect"))!.AsCheckBox();mag.Click();Assert.True(mag.IsChecked);mag.Click();Assert.False(mag.IsChecked);
            B(controls,"FloatStart").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);var clock=Stopwatch.StartNew();Thread.Sleep(1500);B(controls,"InkUndo").Invoke();double undoAt=clock.Elapsed.TotalSeconds;Thread.Sleep(1500);B(controls,"InkRedo").Invoke();Thread.Sleep(1500);B(controls,"FloatStop").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已保存",35);
            var video=Assert.Single(Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").Except(before));var probe=await ToolPaths.Discover().ProbeAsync(video);
            async Task<int> Green(double at,string name){var raw=Path.Combine(evidence,name+".rgb");var decode=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-ss",at.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",video,"-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","-y",raw]);Assert.Equal(0,decode.ExitCode);var pixels=File.ReadAllBytes(raw);int outputX=(int)(740*dpi/rect.Width*1280),outputY=(int)(580*dpi/rect.Height*720);return pixels[(outputY*1280+outputX)*3+1];}
            int moved=await Green(.7,"stamp-moved"),undone=await Green(undoAt+.7,"stamp-undone"),redone=await Green(probe.Duration-.6,"stamp-redone");Assert.True(moved>105,$"Moved stamp is absent from encoded video: G={moved}");Assert.True(undone<90,$"Undo did not remove moved stamp: G={undone}");Assert.True(redone>105,$"Redo did not restore moved stamp: G={redone}");File.WriteAllText(Path.Combine(evidence,"stamp-result.json"),JsonSerializer.Serialize(new{video,croppedFile=file,movedGreen=moved,undoneGreen=undone,redoneGreen=redone}));
            annotation.Close();controls.Close();Owned(a,app,"Mimicraft · 动态练习画面（无声）").Close();B(main,"PlayButton").Invoke();var player=Owned(a,app,"Mimicraft · 静音播放");player.Focus();Thread.Sleep(800);Capture.Element(player).ToFile(Path.Combine(evidence,"stamp-playback.png"));player.Close();
        }
        finally{Close(app,main);}
    }
    [DesktopFact]public void ClosingAndReopeningControlsPreservesAndCanDisableEveryEffect()
    {
        using var app=Launch();using var a=new UIA3Automation();var main=app.GetMainWindow(a,TimeSpan.FromSeconds(20))!;
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"FloatingControls").Invoke();var controls=Owned(a,app,"Mimicraft · 快捷控制");
            string[] ids=["SpotlightEffect","MagnifierEffect","MouseEffect","KeyboardEffect"];
            foreach(string id in ids)controls.FindFirstDescendant(c=>c.ByAutomationId(id))!.AsCheckBox().IsChecked=true;
            controls.Close();B(main,"FloatingControls").Invoke();controls=Owned(a,app,"Mimicraft · 快捷控制");
            foreach(string id in ids)Assert.True(controls.FindFirstDescendant(c=>c.ByAutomationId(id))!.AsCheckBox().IsChecked,$"Reopened {id} lost its active state");
            foreach(string id in ids){var box=controls.FindFirstDescendant(c=>c.ByAutomationId(id))!.AsCheckBox();box.Click();Assert.False(box.IsChecked);box.Click();Assert.True(box.IsChecked);}
            B(controls,"ResetEffects").Click();foreach(string id in ids)Assert.False(controls.FindFirstDescendant(c=>c.ByAutomationId(id))!.AsCheckBox().IsChecked);
            controls.Close();B(main,"FloatingControls").Invoke();controls=Owned(a,app,"Mimicraft · 快捷控制");foreach(string id in ids)Assert.False(controls.FindFirstDescendant(c=>c.ByAutomationId(id))!.AsCheckBox().IsChecked);controls.Close();
        }
        finally{Close(app,main);}
    }
    [DesktopFact]public async Task EnlargedMagnifierAndLiveDisableAreVisibleInEncodedFrames()
    {
        using var app=Launch(true);using var a=new UIA3Automation();var main=app.GetMainWindow(a,TimeSpan.FromSeconds(20))!;var before=Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").ToHashSet();
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"FloatingControls").Invoke();var controls=Owned(a,app,"Mimicraft · 快捷控制");controls.Patterns.Transform.Pattern.Move(900,10);
            var mag=controls.FindFirstDescendant(c=>c.ByAutomationId("MagnifierEffect"))!.AsCheckBox();mag.IsChecked=true;
            double dpi=GetDpiForWindow(Owned(a,app,"Mimicraft · 动态练习画面（无声）").Properties.NativeWindowHandle.Value)/96d;int centerX=(int)(483*dpi)-80,centerY=(int)(380*dpi);
            B(controls,"FloatStart").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);Mouse.MoveTo(centerX,centerY);var onAt=DateTimeOffset.UtcNow;Thread.Sleep(1800);
            // Toggle while recording, without closing or restarting the process.
            mag.IsChecked=false;Wait(()=>mag.IsChecked==false,5);Mouse.MoveTo(centerX,centerY);Thread.Sleep(1800);B(controls,"FloatStop").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已保存",35);
            var video=Assert.Single(Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").Except(before));var probe=await ToolPaths.Discover().ProbeAsync(video);
            var directory=Path.Combine(Root,"artifacts/r4");Directory.CreateDirectory(directory);
            async Task<byte[]> Frame(double at,string name){string path=Path.Combine(directory,name+".rgb");var d=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-ss",at.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",video,"-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","-y",path]);Assert.Equal(0,d.ExitCode);return await File.ReadAllBytesAsync(path);}
            string log=Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mimicraft/Recorder/Logs"),$"recorder-*-{app.ProcessId}.jsonl").Single();var started=File.ReadAllLines(log).Select(line=>JsonDocument.Parse(line)).First(d=>d.RootElement.GetProperty("eventId").GetString()=="RECORDING_STARTED").RootElement.GetProperty("utc").GetDateTimeOffset();
            var enabled=await Frame((onAt-started).TotalSeconds+.7,"magnifier-on");var disabled=await Frame(probe.Duration-.6,"magnifier-off");
            // Original 180px lens ended at x490; a 360px lens reaches x580.
            // At x560 the enlarged lens samples the dark gap at x480, whereas
            // disabling it restores the light color bar at x560.
            int Green(byte[] frame,int x,int y)=>frame[((int)(y*720d/1080)*1280+(int)(x*1280d/1920))*3+1];
            Assert.InRange(Green(enabled,centerX+160,centerY),35,90);Assert.InRange(Green(disabled,centerX+160,centerY),140,195);
            Assert.InRange(Math.Abs(Green(enabled,centerX+200,centerY)-Green(disabled,centerX+200,centerY)),0,12);
            File.WriteAllText(Path.Combine(directory,"magnifier-result.json"),JsonSerializer.Serialize(new{video,probe.Duration,region="360x360 source pixels",zoom=2,onGreen=Green(enabled,centerX+160,centerY),offGreen=Green(disabled,centerX+160,centerY)}));
            controls.Close();Owned(a,app,"Mimicraft · 动态练习画面（无声）").Close();B(main,"PlayButton").Invoke();var player=Owned(a,app,"Mimicraft · 静音播放");player.Focus();Thread.Sleep(800);Capture.Element(player).ToFile(Path.Combine(directory,"magnifier-playback.png"));player.Close();
        }
        finally{Close(app,main);}
    }
    [DesktopFact]public async Task LeftAndRightClickMarkersStopWhenUncheckedDuringRecording()
    {
        using var app=Launch(true);using var a=new UIA3Automation();var main=app.GetMainWindow(a,TimeSpan.FromSeconds(20))!;var before=Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").ToHashSet();
        try
        {
            B(main,"PracticeButton").Invoke();B(main,"FloatingControls").Invoke();var controls=Owned(a,app,"Mimicraft · 快捷控制");controls.Patterns.Transform.Pattern.Move(900,10);var clicks=controls.FindFirstDescendant(c=>c.ByAutomationId("MouseEffect"))!.AsCheckBox();clicks.IsChecked=true;
            B(controls,"FloatStart").Invoke();Wait(()=>Text(main,"RecordingStatus")=="录制中",35);Mouse.Click(new(900,700),MouseButton.Left);var leftAt=DateTimeOffset.UtcNow;Thread.Sleep(900);Mouse.Click(new(900,700),MouseButton.Right);var rightAt=DateTimeOffset.UtcNow;Thread.Sleep(900);
            clicks.IsChecked=false;Wait(()=>clicks.IsChecked==false,5);Mouse.Click(new(900,700),MouseButton.Left);var offAt=DateTimeOffset.UtcNow;Thread.Sleep(900);B(controls,"FloatStop").Invoke();Wait(()=>Text(main,"RecordingStatus")=="已保存",35);
            var video=Assert.Single(Directory.GetFiles(Path.Combine(Root,"artifacts/recordings"),"*.mp4").Except(before));string log=Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mimicraft/Recorder/Logs"),$"recorder-*-{app.ProcessId}.jsonl").Single();var started=File.ReadAllLines(log).Select(line=>JsonDocument.Parse(line)).First(d=>d.RootElement.GetProperty("eventId").GetString()=="RECORDING_STARTED").RootElement.GetProperty("utc").GetDateTimeOffset();
            async Task<(int Red,int Blue)> Counts(DateTimeOffset at,string name){string raw=Path.Combine(Root,"artifacts/r4/"+name+".rgb");var d=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-ss",((at-started).TotalSeconds+.12).ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",video,"-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","-y",raw]);Assert.Equal(0,d.ExitCode);var bytes=File.ReadAllBytes(raw);int red=0,blue=0;for(int y=445;y<490;y++)for(int x=578;x<623;x++){int p=(y*1280+x)*3;byte r=bytes[p],g=bytes[p+1],b=bytes[p+2];if(r>180&&g<130&&b<130)red++;if(b>180&&g<150&&r<130)blue++;}return(red,blue);}
            var left=await Counts(leftAt,"mouse-left");var right=await Counts(rightAt,"mouse-right");var off=await Counts(offAt,"mouse-disabled");Assert.True(left.Red>80,$"Left marker missing: {left}");Assert.True(right.Blue>80,$"Right marker missing: {right}");Assert.Equal(0,off.Red);Assert.Equal(0,off.Blue);File.WriteAllText(Path.Combine(Root,"artifacts/r4/mouse-result.json"),JsonSerializer.Serialize(new{video,leftRed=left.Red,rightBlue=right.Blue,offRed=off.Red,offBlue=off.Blue}));controls.Close();
        }
        finally{Close(app,main);}
    }
    private static Window Owned(UIA3Automation a,Application app,string title){nint h=0;Wait(()=>{h=FindWindow(null,title);GetWindowThreadProcessId(h,out var pid);return h!=0&&pid==app.ProcessId;},12);return a.FromHandle(h).AsWindow();}
    private static void Close(Application app,Window main){try{main.Close();}catch{}if(!app.HasExited){try{Process.GetProcessById(app.ProcessId).WaitForExit(3000);}catch{}if(!app.HasExited)app.Kill();}}
    public void Dispose(){Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",null);if(_dpi!=0)SetThreadDpiAwarenessContext(_dpi);}
    [DllImport("user32.dll")]private static extern nint SetThreadDpiAwarenessContext(nint value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint FindWindow(string? cls,string title);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint hwnd,out int pid);
    [DllImport("user32.dll")]private static extern nint WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")]private static extern nint GetAncestor(nint hwnd,uint flags);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(nint hwnd);
}

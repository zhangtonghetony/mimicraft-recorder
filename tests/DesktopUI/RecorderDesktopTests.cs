using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;
using Xunit;

[assembly:CollectionBehavior(DisableTestParallelization=true)]

public sealed class DesktopFactAttribute:FactAttribute
{
    public DesktopFactAttribute(){if(Environment.GetEnvironmentVariable("MIMICRAFT_DESKTOP_TESTS")!="1")Skip="需要显式设置MIMICRAFT_DESKTOP_TESTS=1，在可见桌面执行无声录制。";}
}
public sealed class DesktopTheoryAttribute:TheoryAttribute
{
    public DesktopTheoryAttribute(){if(Environment.GetEnvironmentVariable("MIMICRAFT_DESKTOP_TESTS")!="1")Skip="需要显式设置MIMICRAFT_DESKTOP_TESTS=1，在可见桌面执行无声录制。";}
}

public class RecorderDesktopTests
{
    [DesktopFact]
    public void ClosingCaptureSourceReportsFailureAndDoesNotPublishPartialFile()
    {
        var root=Environment.GetEnvironmentVariable("MIMICRAFT_EV_ROOT")!;
        var exe=Path.Combine(root,"src","Mimicraft.Recorder","bin","Debug","net10.0-windows10.0.26100.0","Mimicraft.Recorder.exe");
        var files=Directory.GetFiles(Path.Combine(root,"artifacts","recordings"),"*.mp4").ToHashSet();
        Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",null);
        Environment.SetEnvironmentVariable("MIMICRAFT_DATA_DIR",Path.Combine(root,"artifacts","ui-test-data"));
        using var app=Application.Launch(exe,"--verification");using var automation=new UIA3Automation();
        var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20))!;
        try
        {
            main.FindFirstDescendant(cf=>cf.ByAutomationId("PracticeButton"))!.AsButton().Invoke();
            main.FindFirstDescendant(cf=>cf.ByAutomationId("StartButton"))!.AsButton().Invoke();
            Wait(()=>main.FindFirstDescendant(cf=>cf.ByAutomationId("RecordingStatus"))!.Name=="录制中",30,"录制未开始");
            var source=FindWindow(null,"Mimicraft · 动态练习画面（无声）");GetWindowThreadProcessId(source,out var pid);Assert.Equal(app.ProcessId,pid);
            automation.FromHandle(source).AsWindow().Close();
            Wait(()=>!IsWindow(source),8,"测试窗口未完成关闭，不能据此断言捕获故障处理。");
            Wait(()=>main.FindFirstDescendant(cf=>cf.ByAutomationId("RecordingStatus"))!.Name=="请检查提示",15,"来源关闭后未报告错误");
            var message=main.FindFirstDescendant(cf=>cf.ByAutomationId("StatusMessage"))!.Name;Assert.Contains("来源",message);
            Assert.True(main.FindFirstDescendant(cf=>cf.ByAutomationId("StartButton"))!.AsButton().IsEnabled);
            Assert.Empty(Directory.GetFiles(Path.Combine(root,"artifacts","recordings"),"*.mp4").Except(files));
        }
        finally{main.Close();if(!app.HasExited){try{Process.GetProcessById(app.ProcessId).WaitForExit(4000);}catch{};if(!app.HasExited)app.Kill();}}
    }

    [DesktopTheory]
    [InlineData("自动（硬件优先）","auto")]
    [InlineData("软件 / OpenH264","software")]
    [InlineData("自动（硬件优先）","monitor")]
    public void RealWindowCaptureProducesVerifiedSilentVideoAndMutedPlayback(string encoder,string suffix)
    {
        var previousDpi=SetThreadDpiAwarenessContext(new nint(-4));
        var root=Environment.GetEnvironmentVariable("MIMICRAFT_EV_ROOT")??throw new InvalidOperationException("MIMICRAFT_EV_ROOT缺失");
        var exe=Environment.GetEnvironmentVariable("MIMICRAFT_TEST_APP")??Path.Combine(root,"src","Mimicraft.Recorder","bin","Debug","net10.0-windows10.0.26100.0","Mimicraft.Recorder.exe");
        var output=Path.Combine(root,"artifacts","recordings");Directory.CreateDirectory(output);
        var before=Directory.GetFiles(output,"*.mp4").ToHashSet();
        var config=Path.Combine(root,"artifacts","baseline-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config,"settings.json"),JsonSerializer.Serialize(new{fps=30,resolution="1280 × 720",encoder,options=new{StopAfterSeconds=10}}));
        Environment.SetEnvironmentVariable("MIMICRAFT_DATA_DIR",config);
        Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",suffix=="monitor"?"1":null);
        var launch=new ProcessStartInfo(exe,"--verification"){UseShellExecute=false};
        if(Environment.GetEnvironmentVariable("MIMICRAFT_PORTABLE_PROBE")=="1")
        {
            launch.WorkingDirectory=Path.GetDirectoryName(exe)!;launch.Environment.Remove("DOTNET_ROOT");launch.Environment.Remove("DOTNET_ROOT_X64");launch.Environment["DOTNET_MULTILEVEL_LOOKUP"]="0";launch.Environment["PATH"]=Environment.GetFolderPath(Environment.SpecialFolder.System);launch.Environment.Remove("MIMICRAFT_FFMPEG_DIR");
        }
        using var app=Application.Launch(launch);
        using var automation=new UIA3Automation();
        Window? main=null;
        try
        {
            main=app.GetMainWindow(automation,TimeSpan.FromSeconds(20));Assert.NotNull(main);
            Button Button(string id)=>main!.FindFirstDescendant(cf=>cf.ByAutomationId(id)).AsButton()!;
            string Status()=>main!.FindFirstDescendant(cf=>cf.ByAutomationId("RecordingStatus"))!.Name;
            string Message()=>main!.FindFirstDescendant(cf=>cf.ByAutomationId("StatusMessage"))!.Name;
            Assert.True(Button("StartButton").IsEnabled);Assert.False(Button("StopButton").IsEnabled);
            Assert.Equal("准备就绪",Status());
            Directory.CreateDirectory(Path.Combine(root,"artifacts","screenshots"));
            main.FindFirstDescendant(cf=>cf.ByAutomationId("EncoderSelect"))!.AsComboBox().Select(encoder);
            Capture.Element(main).ToFile(Path.Combine(root,"artifacts","screenshots","idle-"+suffix+".png"));
            Button("PracticeButton").Invoke();
            Wait(()=>main.FindFirstDescendant(cf=>cf.ByAutomationId("SourceSelect"))!.AsComboBox().SelectedItem?.Name.Contains("练习")??false,10,"练习来源未选中");
            Button("StartButton").Invoke();
            Wait(()=>Status()=="录制中",35,"未进入录制状态："+Message());
            Assert.False(Button("StartButton").IsEnabled);Assert.True(Button("StopButton").IsEnabled);
            Capture.Element(main).ToFile(Path.Combine(root,"artifacts","screenshots","recording-"+suffix+".png"));
            // Automatic stop gives the engine a precise 10s deadline; UIA polling
            // an integer label and then invoking Stop adds variable desktop latency.
            // Manual Stop (including while paused) is covered by extended tests.
            Wait(()=>Status() is "已保存" or "请检查提示",45,"收尾超时");
            Assert.True(Status()=="已保存",Message());
            Assert.True(Button("StartButton").IsEnabled);Assert.False(Button("StopButton").IsEnabled);
            var recorded=Directory.GetFiles(output,"*.mp4").Except(before).ToArray();Assert.Single(recorded);
            using var probe=JsonDocument.Parse(File.ReadAllText(recorded[0]+".probe.json"));
            var streams=probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();Assert.Single(streams);
            Assert.Equal("h264",streams[0].GetProperty("codec_name").GetString());
            Assert.Equal(1280,streams[0].GetProperty("width").GetInt32());Assert.Equal(720,streams[0].GetProperty("height").GetInt32());
            Assert.Equal("video",streams[0].GetProperty("codec_type").GetString());
            Assert.InRange(double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture),9.5,10.5);
            Capture.Element(main).ToFile(Path.Combine(root,"artifacts","screenshots","completed-"+suffix+".png"));
            Button("PlayButton").Invoke();
            // Avoid traversing unrelated desktop UI providers while a video is playing.
            nint playerHandle=0;
            Wait(()=>{playerHandle=FindWindow(null,"Mimicraft · 静音播放");GetWindowThreadProcessId(playerHandle,out var owner);return playerHandle!=0&&owner==app.ProcessId;},10,"静音播放窗口未打开");
            var playback=automation.FromHandle(playerHandle).AsWindow();
            Thread.Sleep(1500);
            Capture.Element(playback).ToFile(Path.Combine(root,"artifacts","screenshots","muted-playback-"+suffix+".png"));
            playback.Close();
            File.WriteAllText(Path.Combine(root,"artifacts","desktop-latest.json"),JsonSerializer.Serialize(new{success=true,path=recorded[0],appPid=app.ProcessId,utc=DateTimeOffset.UtcNow,audio="not captured; muted playback"}));
        }
        catch
        {
            if(main is not null)try{Capture.Element(main).ToFile(Path.Combine(root,"artifacts","screenshots","failure.png"));}catch{}
            throw;
        }
        finally
        {
            if(main is not null)try
            {
                var stop=main.FindFirstDescendant(cf=>cf.ByAutomationId("StopButton"))?.AsButton();if(stop?.IsEnabled==true){stop.Invoke();Thread.Sleep(3000);}
                main.Close();
            }catch{}
            if(!app.HasExited){try{Process.GetProcessById(app.ProcessId).WaitForExit(6000);}catch{};if(!app.HasExited)app.Kill();}
            if(previousDpi!=0)SetThreadDpiAwarenessContext(previousDpi);
            Environment.SetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE",null);
        }
    }

    private static void Wait(Func<bool> condition,int seconds,string message)
    {
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(seconds)){if(condition())return;Thread.Sleep(100);}
        Assert.Fail(message);
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint FindWindow(string? cls,string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out int pid);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
}

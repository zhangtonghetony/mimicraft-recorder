using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;

public class WatcherTests
{
    [DesktopFact]
    public void BuildFailurePreservesPreviewAndBusyRecordingDefersRestart()
    {
        var root=Environment.GetEnvironmentVariable("MIMICRAFT_EV_ROOT")!;
        var dev=Path.Combine(root,"artifacts","dev");Directory.CreateDirectory(dev);
        var probe=Path.Combine(root,"src","Mimicraft.Recorder","DevRestartVerification.cs");
        Assert.False(File.Exists(probe));
        string? CurrentJson()=>File.Exists(Path.Combine(dev,"current.json"))?File.ReadAllText(Path.Combine(dev,"current.json")):null;
        var previous=CurrentJson();
        var info=new ProcessStartInfo("python"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(Path.Combine(root,"scripts","dev.py"));info.ArgumentList.Add("--verification");
        using var watcher=Process.Start(info)!;
        var output=new System.Text.StringBuilder();var gate=new object();
        watcher.OutputDataReceived+=(_,e)=>{lock(gate)output.AppendLine(e.Data);};watcher.ErrorDataReceived+=(_,e)=>{lock(gate)output.AppendLine(e.Data);};watcher.BeginOutputReadLine();watcher.BeginErrorReadLine();
        using var automation=new UIA3Automation();int pid=0;
        try
        {
            Wait(()=>CurrentJson()!=previous&&CurrentJson() is not null,40,"初始预览未启动");pid=CurrentPid();
            Wait(()=>File.Exists(StateFile(pid)),10,"开发状态文件未生成");
            File.WriteAllText(probe,"This is a deliberate build error.");
            Wait(()=>ReadOutput().Contains("Build failed. Existing preview stays open."),40,"未收到构建失败反馈");
            Assert.False(Process.GetProcessById(pid).HasExited);
            File.WriteAllText(probe,"namespace Mimicraft.Recorder; internal static class DevRestartVerification {} ");
            Wait(()=>CurrentPid()!=pid,40,"空闲预览未安全重启");pid=CurrentPid();
            using var app=Application.Attach(pid);var main=app.GetMainWindow(automation,TimeSpan.FromSeconds(15))!;
            main.FindFirstDescendant(cf=>cf.ByAutomationId("PracticeButton"))!.AsButton().Invoke();
            main.FindFirstDescendant(cf=>cf.ByAutomationId("StartButton"))!.AsButton().Invoke();
            Wait(()=>main.FindFirstDescendant(cf=>cf.ByAutomationId("RecordingStatus"))!.Name=="录制中",30,"测试录制未开始");
            var before=ReadOutput().Split("Update ready;").Length;
            File.WriteAllText(probe,"namespace Mimicraft.Recorder; internal static class DevRestartVerification { internal const int Revision=2; } ");
            Wait(()=>ReadOutput().Split("Update ready;").Length>before,40,"录制中修改没有排队");
            Assert.Equal(pid,CurrentPid());Assert.False(Process.GetProcessById(pid).HasExited);
            main.FindFirstDescendant(cf=>cf.ByAutomationId("StopButton"))!.AsButton().Invoke();
            Wait(()=>CurrentPid()!=pid,45,"停止后未应用更新");pid=CurrentPid();
            Wait(()=>File.Exists(StateFile(pid)),10,"新进程状态缺失");
            using var state=JsonDocument.Parse(File.ReadAllText(StateFile(pid)));
            Assert.False(state.RootElement.GetProperty("busy").GetBoolean());Assert.Equal("Idle",state.RootElement.GetProperty("state").GetString());
            File.WriteAllText(Path.Combine(root,"artifacts","watcher-validation.json"),JsonSerializer.Serialize(new{buildFailurePreserved=true,restartDeferredDuringRecording=true,restartedIdle=true,pid}));
        }
        finally
        {
            if(!watcher.HasExited){watcher.Kill();watcher.WaitForExit();}
            File.WriteAllText(Path.Combine(root,"artifacts","watcher-test.log"),ReadOutput());
            if(pid>0)File.WriteAllText(StateFile(pid)+".close","test complete; close when safe");
            if(File.Exists(probe))File.Delete(probe);
        }
        string ReadOutput(){lock(gate)return output.ToString();}
        int CurrentPid(){using var d=JsonDocument.Parse(CurrentJson()!);return d.RootElement.GetProperty("pid").GetInt32();}
        string StateFile(int id)=>Path.Combine(dev,$"state-{id}.json");
    }
    private static void Wait(Func<bool> check,int seconds,string error)
    {
        var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<seconds){try{if(check())return;}catch(IOException){};Thread.Sleep(150);}Assert.Fail(error);
    }
}

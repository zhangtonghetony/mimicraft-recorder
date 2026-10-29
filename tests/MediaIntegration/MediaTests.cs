using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;
using Xunit;

public class MediaTests
{
    [Fact] public async Task InvalidSettingsFailBeforeCaptureAndCanRetry()
    {
        var dir=Path.Combine(Path.GetTempPath(),"Mimicraft-test-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        var engine=new RecorderEngine(new EventLog(dir));
        await Assert.ThrowsAsync<ArgumentException>(()=>engine.StartAsync(new("invalid",0),new RecordingSettings(Fps:0),dir));
        Assert.Equal(RecordingState.Failed,engine.State.Value);
        await Assert.ThrowsAsync<ArgumentException>(()=>engine.StartAsync(new("invalid",0),new RecordingSettings(Width:1),dir));
        Assert.Empty(Directory.GetFiles(dir,"*.mp4"));
    }
    [Fact] public async Task FileInsteadOfOutputDirectoryFailsBeforeCapture()
    {
        var dir=Path.Combine(Path.GetTempPath(),"Mimicraft-test-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        var file=Path.Combine(dir,"not-a-folder");await File.WriteAllTextAsync(file,"preserve original");
        var engine=new RecorderEngine(new EventLog(dir));
        await Assert.ThrowsAnyAsync<IOException>(()=>engine.StartAsync(new("invalid",0),new(),file));
        Assert.Equal("preserve original",await File.ReadAllTextAsync(file));Assert.Equal(RecordingState.Failed,engine.State.Value);
    }
    [Fact] public async Task SoftwareEncoderCreatesSilentDecodableH264()
    {
        var tools=ToolPaths.Discover();var file=Path.Combine(Path.GetTempPath(),"Mimicraft-software-"+Guid.NewGuid()+".mp4");
        var result=await ToolPaths.RunAsync(tools.Ffmpeg,["-hide_banner","-loglevel","error","-f","lavfi","-i","testsrc2=size=1280x720:rate=30","-t","1","-an","-c:v","libopenh264","-b:v","4M","-n",file]);
        Assert.True(result.ExitCode==0,result.Error);
        var probe=await tools.ProbeAsync(file);Assert.Equal("h264",probe.Codec);Assert.Equal(0,probe.AudioTracks);Assert.Equal(1280,probe.Width);
        var decode=await ToolPaths.RunAsync(tools.Ffmpeg,["-v","error","-i",file,"-an","-f","null","-"]);Assert.Equal(0,decode.ExitCode);
    }
    [Fact] public async Task ToolTimeoutKillsOnlyItsOwnProcess()
    {
        var tools=ToolPaths.Discover();
        await Assert.ThrowsAsync<TimeoutException>(()=>ToolPaths.RunAsync(tools.Ffmpeg,["-hide_banner","-loglevel","error","-re","-f","lavfi","-i","color=size=64x64:rate=1","-an","-f","null","-"],1));
    }
}

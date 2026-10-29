using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;
using Xunit;

public class EditingMediaTests
{
    private async Task<(ToolPaths Tools,string Dir,string A,string B)> Sources()
    {
        var tools=ToolPaths.Discover();var dir=Path.Combine(Path.GetTempPath(),"Mimicraft-edit-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        var a=Path.Combine(dir,"original-a.mp4");var b=Path.Combine(dir,"original-b.mp4");
        var createA=await ToolPaths.RunAsync(tools.Ffmpeg,["-v","error","-f","lavfi","-i","testsrc2=s=320x240:r=25","-f","lavfi","-i","anullsrc=r=44100:cl=mono","-t","3","-c:v","libopenh264","-b:v","1M","-c:a","aac","-n",a]);Assert.True(createA.ExitCode==0,createA.Error);
        var createB=await ToolPaths.RunAsync(tools.Ffmpeg,["-v","error","-f","lavfi","-i","color=c=blue:s=640x360:r=30","-t","2","-an","-c:v","libopenh264","-b:v","1M","-n",b]);Assert.True(createB.ExitCode==0,createB.Error);
        return(tools,dir,a,b);
    }
    [Theory][InlineData(1,2)][InlineData(2,1)][InlineData(.25,8)]
    public async Task TrimAndSpeedHaveExpectedDurationAndAudio(double speed,double duration)
    {
        var s=await Sources();var output=Path.Combine(s.Dir,"export.mp4");var before=await File.ReadAllBytesAsync(s.A);
        var p=await new MediaJobs(new(s.Dir)).ExportAsync([new(s.A,new(.5,2.5))],output,new(640,360,30,1000,speed));
        Assert.Equal(1,p.AudioTracks);Assert.InRange(p.Duration,duration-.15,duration+.15);Assert.Equal(before,await File.ReadAllBytesAsync(s.A));
        var decode=await ToolPaths.RunAsync(s.Tools.Ffmpeg,["-v","error","-i",output,"-f","null","-"]);Assert.Equal(0,decode.ExitCode);
    }
    [Fact]public async Task MergeNormalizesSilentAndAudioSourcesAndRetainsOrder()
    {
        var s=await Sources();var output=Path.Combine(s.Dir,"joined.mkv");var p=await new MediaJobs(new(s.Dir)).ExportAsync([new(s.B,new(0,1)),new(s.A,new(1,2))],output,new(640,360,30,1000));
        Assert.Equal(1,p.AudioTracks);Assert.InRange(p.Duration,1.85,2.15);Assert.Equal(640,p.Width);
        var frame=Path.Combine(s.Dir,"first.rgb");var extract=await ToolPaths.RunAsync(s.Tools.Ffmpeg,["-v","error","-i",output,"-frames:v","1","-f","rawvideo","-pix_fmt","rgb24",frame]);Assert.Equal(0,extract.ExitCode);var bytes=await File.ReadAllBytesAsync(frame);Assert.True(bytes[2]>200&&bytes[0]<30,"First frame must come from the blue source.");
    }
    [Fact]public async Task GifHasPaletteAndSelectedSegmentDuration()
    {
        var s=await Sources();var output=Path.Combine(s.Dir,"export.gif");var p=await new MediaJobs(new(s.Dir)).ExportAsync([new(s.A,new(.5,1.5))],output,new(640,360,30,1000,Gif:true,GifFps:12));Assert.Equal("gif",p.Codec);Assert.Equal(0,p.AudioTracks);Assert.InRange(p.Duration,.8,1.2);
        var decode=await ToolPaths.RunAsync(s.Tools.Ffmpeg,["-v","error","-i",output,"-f","null","-"]);Assert.Equal(0,decode.ExitCode);
    }
    [Fact]public async Task ExistingOutputAndCancellationNeverOverwriteSources()
    {
        var s=await Sources();var job=new MediaJobs(new(s.Dir));
        await Assert.ThrowsAsync<IOException>(()=>job.ExportAsync([new(s.A,new(0,1))],s.A,new()));
        var output=Path.Combine(s.Dir,"cancel.mp4");using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>job.ExportAsync([new(s.A,new(0,1))],output,new(),cancel.Token));Assert.False(File.Exists(output));Assert.Empty(Directory.GetFiles(s.Dir,".mimicraft-job-*"));
    }
    [Theory][InlineData("baseline")][InlineData("main")][InlineData("high")]
    public async Task SoftwareRecordingProfilesCanInitialize(string profile)
    {
        var t=ToolPaths.Discover();var result=await ToolPaths.RunAsync(t.Ffmpeg,["-v","error","-f","lavfi","-i","color=s=320x240:r=30","-t","0.2","-an","-c:v","libopenh264","-b:v","1M","-profile:v",RecorderEngine.ProfileName("libopenh264",profile),"-f","null","-"]);Assert.True(result.ExitCode==0,result.Error);
    }
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)]public async Task RecordingFilterWorksWithBundledLgplBuild(double saturation)
    {
        var t=ToolPaths.Discover();var result=await ToolPaths.RunAsync(t.Ffmpeg,["-v","error","-f","lavfi","-i","testsrc2=s=1264x804:r=30","-t","0.2","-an","-vf",RecorderEngine.VideoFilter(new(Saturation:saturation)),"-c:v","libopenh264","-b:v","1M","-f","null","-"]);Assert.True(result.ExitCode==0,result.Error);
    }
    [Fact]public async Task BatchMarksBothFilesPreservesSourcesAndPublishesOnlyValidatedOutputs()
    {
        var s=await Sources();var mark=Path.Combine(s.Dir,"mark.png");var create=await ToolPaths.RunAsync(s.Tools.Ffmpeg,["-v","error","-f","lavfi","-i","color=c=red:s=120x80","-frames:v","1",mark]);Assert.Equal(0,create.ExitCode);
        var original=await File.ReadAllBytesAsync(s.A);var result=await new MediaJobs(new(s.Dir)).BatchMarkAsync([s.A,Path.Combine(s.Dir,"missing.mp4"),s.B],Path.Combine(s.Dir,"marked"),mark);Assert.Equal(2,result.Files.Count);Assert.Single(result.Errors);Assert.Equal(original,await File.ReadAllBytesAsync(s.A));
        foreach(var file in result.Files)
        {
            var p=await s.Tools.ProbeAsync(file);Assert.Equal(1280,p.Width);var rgb=file+".rgb";var extract=await ToolPaths.RunAsync(s.Tools.Ffmpeg,["-v","error","-i",file,"-frames:v","1","-pix_fmt","rgb24","-f","rawvideo",rgb]);Assert.Equal(0,extract.ExitCode);var pixels=await File.ReadAllBytesAsync(rgb);Assert.True(pixels[0]>220&&pixels[1]<30&&pixels[2]<30,"Watermark must be visible in the output.");
        }
        Assert.Empty(Directory.GetFiles(Path.Combine(s.Dir,"marked"),".mimicraft-job-*"));
    }
    [Fact]public async Task SpatialCropRejectsOutOfBoundsAndExportsSelectedImageArea()
    {
        var s=await Sources();var output=Path.Combine(s.Dir,"crop.mp4");var job=new MediaJobs(new(s.Dir));
        await Assert.ThrowsAsync<ArgumentException>(()=>job.ExportAsync([new(s.A,new(0,1))],output,new(Crop:new(250,0,200,200))));Assert.False(File.Exists(output));
        var result=await job.ExportAsync([new(s.A,new(0,1))],output,new(640,360,30,1000,Crop:new(40,40,160,90)));Assert.Equal(640,result.Width);Assert.InRange(result.Duration,.9,1.1);
    }
}

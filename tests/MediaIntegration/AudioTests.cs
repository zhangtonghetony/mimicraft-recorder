using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Xunit;

public sealed class AudioTheoryAttribute:TheoryAttribute
{
    public AudioTheoryAttribute(){if(Environment.GetEnvironmentVariable("MIMICRAFT_AUDIO_TESTS")!="1")Skip="真实声音测试需明确启用MIMICRAFT_AUDIO_TESTS=1。";}
}
public class AudioTests
{
    [Fact]public async Task Rf64RecoveryBackupIsReadableByFfprobe()
    {
        var path=Path.Combine(Path.GetTempPath(),"Mimicraft-rf64-"+Guid.NewGuid()+".wav");using(var writer=new Rf64Backup(path))writer.Write(new byte[384000],0,384000);var p=await ToolPaths.Discover().ProbeAsync(path,true);Assert.Equal(1,p.AudioTracks);Assert.InRange(p.Duration,.99,1.01);
    }
    [AudioTheory][InlineData("system","wav")][InlineData("microphone","mp3")][InlineData("both","m4a")]
    public async Task RealWasapiCaptureProducesAudioOnlyFileAndPauseExcludesTime(string mode,string format)
    {
        var dir=Path.Combine(Path.GetTempPath(),"Mimicraft-audio-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        var engine=new RecorderEngine(new(dir));
        int rate=format=="mp3"?44100:48000;await engine.StartAsync(new("not-used",0),new(Audio:new(mode,AudioOnly:true,Format:format,Enhance:true,Denoise:true,SampleRate:rate,BitrateKbps:256)),dir);
        using var output=new WasapiOut(AudioClientShareMode.Shared,100);
        output.Init(new SignalGenerator(48000,2){Gain=.015,Frequency=660,Type=SignalGeneratorType.Sin});
        if(mode!="microphone")output.Play();
        await Task.Delay(1200);double peak=engine.AudioPeak;
        await engine.TogglePauseAsync();var time=engine.Clock.Elapsed;await Task.Delay(4000);Assert.InRange((engine.Clock.Elapsed-time).TotalMilliseconds,-2,2);
        await engine.TogglePauseAsync();await Task.Delay(800);output.Stop();
        var entry=await engine.StopAsync();Assert.NotNull(entry);var p=await ToolPaths.Discover().ProbeAsync(entry.Path,true);Assert.Equal("audio",p.Codec);Assert.Equal(1,p.AudioTracks);Assert.InRange(p.Duration,1.8,3.5);
        using(var metadata=System.Text.Json.JsonDocument.Parse(p.Json)){var stream=metadata.RootElement.GetProperty("streams")[0];Assert.Equal(rate.ToString(),stream.GetProperty("sample_rate").GetString());if(format=="mp3")Assert.Equal("256000",stream.GetProperty("bit_rate").GetString());}
        if(mode!="microphone")Assert.True(peak>.001,$"Loopback signal missing: {peak}");
        var decode=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-i",entry.Path,"-f","null","-"]);Assert.Equal(0,decode.ExitCode);
        File.WriteAllText(Path.Combine(dir,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{mode,format,peak,p.Duration,p.AudioTracks,path=entry.Path}));
    }
}

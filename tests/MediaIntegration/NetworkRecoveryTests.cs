using System.Net;
using System.Net.Sockets;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;
using Xunit;

public class NetworkRecoveryTests
{
    public sealed class NetworkFactAttribute:FactAttribute{public NetworkFactAttribute(){if(Environment.GetEnvironmentVariable("MIMICRAFT_NETWORK_TESTS")!="1")Skip="网络验证只在显式启用时监听本地回环。";}}
    private static int Port(){using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();return ((IPEndPoint)listener.LocalEndpoint).Port;}
    private async Task<(string Dir,string File)> Source()
    {
        var dir=Path.Combine(Path.GetTempPath(),"Mimicraft-network-"+Guid.NewGuid());Directory.CreateDirectory(dir);var file=Path.Combine(dir,"source.mp4");var tools=ToolPaths.Discover();
        var p=await ToolPaths.RunAsync(tools.Ffmpeg,["-v","error","-f","lavfi","-i","testsrc2=s=640x360:r=30","-f","lavfi","-i","sine=f=440:r=48000","-t","2","-c:v","libopenh264","-b:v","1M","-c:a","aac","-n",file]);Assert.True(p.ExitCode==0,p.Error);return(dir,file);
    }
    [NetworkFact]public async Task FilePushReachesTwoIndependentRtmpReceiversAndRedactsSecrets()
    {
        var s=await Source();var urls=new[]{ $"rtmp://127.0.0.1:{Port()}/live/secret-one",$"rtmp://127.0.0.1:{Port()}/live/secret-two"};var receivers=new List<NetworkProcess>();var pushers=new List<NetworkProcess>();
        try
        {
            for(int i=0;i<2;i++)receivers.Add(new(["-v","warning","-rw_timeout","10000000","-listen","1","-i",urls[i],"-c","copy","-f","matroska",Path.Combine(s.Dir,$"received{i}.mkv")],urls[i]));
            await Task.Delay(500);foreach(var url in urls)pushers.Add(NetworkJobs.PushFile(s.File,url,false));
            pushers.Add(NetworkJobs.PushFile(s.File,$"rtmp://127.0.0.1:{Port()}/live/bad-secret",false));
            for(int i=0;i<pushers.Count;i++){var p=pushers[i];await p.WaitAsync().WaitAsync(TimeSpan.FromSeconds(15));if(i<2)Assert.Equal(0,p.ExitCode);else Assert.NotEqual(0,p.ExitCode);Assert.DoesNotContain("secret-",p.Diagnostic);Assert.DoesNotContain("bad-secret",p.Diagnostic);}
            foreach(var p in receivers)await p.WaitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            for(int i=0;i<2;i++){var result=await ToolPaths.Discover().ProbeAsync(Path.Combine(s.Dir,$"received{i}.mkv"));Assert.Equal(1,result.AudioTracks);Assert.InRange(result.Duration,1.8,2.3);}
        }
        finally{foreach(var p in pushers.Concat(receivers))await p.DisposeAsync();}
    }
    [NetworkFact]public async Task LiveEncoderTeeReachesReceiverAndKeepsLocalAudioRecording()
    {
        var s=await Source();var url=$"rtmp://127.0.0.1:{Port()}/live/local-key";string received=Path.Combine(s.Dir,"live.mkv");
        await using var receiver=new NetworkProcess(["-v","warning","-rw_timeout","10000000","-listen","1","-i",url,"-c","copy","-f","matroska",received],url);await Task.Delay(400);
        var engine=new RecorderEngine(new(s.Dir));await engine.StartAsync(new("notused",0),new(640,360,Audio:new("system",AudioOnly:true,Format:"m4a"),Destinations:[url,$"rtmp://127.0.0.1:{Port()}/live/bad-key"]),s.Dir);await Task.Delay(4000);var entry=await engine.StopAsync();Assert.NotNull(entry);await receiver.WaitAsync().WaitAsync(TimeSpan.FromSeconds(15));var p=await ToolPaths.Discover().ProbeAsync(received);Assert.Equal(1,p.AudioTracks);Assert.InRange(p.Duration,2,5.5);Assert.NotEmpty(engine.StreamWarning);
        foreach(var log in Directory.GetFiles(s.Dir,"recorder-*.jsonl"))Assert.DoesNotContain("local-key",await File.ReadAllTextAsync(log));
    }
    [NetworkFact]public async Task NetworkInputDownloadsActualHttpMediaAndFinalizesWithoutOverwriting()
    {
        var s=await Source();var bytes=await File.ReadAllBytesAsync(s.File);using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;using var stop=new CancellationTokenSource();
        var server=Task.Run(async()=>
        {
            try{while(!stop.IsCancellationRequested){using var client=await listener.AcceptTcpClientAsync(stop.Token);using var stream=client.GetStream();var request=new byte[8192];int count=await stream.ReadAsync(request,stop.Token);string headers=System.Text.Encoding.ASCII.GetString(request,0,count);long start=0;var match=System.Text.RegularExpressions.Regex.Match(headers,@"Range: bytes=(\d+)-",System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(match.Success)start=long.Parse(match.Groups[1].Value);var head=System.Text.Encoding.ASCII.GetBytes((start>0?"HTTP/1.1 206 Partial Content":"HTTP/1.1 200 OK")+$"\r\nContent-Type: video/mp4\r\nAccept-Ranges: bytes\r\nContent-Length: {bytes.Length-start}\r\nContent-Range: bytes {start}-{bytes.Length-1}/{bytes.Length}\r\nConnection: close\r\n\r\n");await stream.WriteAsync(head,stop.Token);try{await stream.WriteAsync(bytes.AsMemory((int)start),stop.Token);}catch(IOException){}}}catch(OperationCanceledException){}
        });
        try{var output=Path.Combine(s.Dir,"ingest.mkv");var p=await new NetworkJobs(new(s.Dir)).IngestAsync($"http://127.0.0.1:{port}/sample.mp4",output,CancellationToken.None);Assert.InRange(p.Duration,1.8,2.3);Assert.Equal(1,p.AudioTracks);Assert.True(File.Exists(output));}
        finally{stop.Cancel();listener.Stop();await server;}
    }
    [NetworkFact]public async Task LocalUdpBroadcastCanBeReceivedStoppedAndDecoded()
    {
        var s=await Source();int port;using(var reserve=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))port=((IPEndPoint)reserve.Client.LocalEndPoint!).Port;
        string output=Path.Combine(s.Dir,"udp-capture.mkv");using var stop=new CancellationTokenSource();var ingest=new NetworkJobs(new(s.Dir)).IngestAsync($"udp://127.0.0.1:{port}?timeout=5000000",output,stop.Token);await Task.Delay(600);
        await using(var sender=NetworkJobs.PushFile(s.File,$"udp://127.0.0.1:{port}?pkt_size=1316",false)){await sender.WaitAsync().WaitAsync(TimeSpan.FromSeconds(15));Assert.Equal(0,sender.ExitCode);await Task.Delay(400);stop.Cancel();}
        var p=await ingest.WaitAsync(TimeSpan.FromSeconds(20));Assert.InRange(p.Duration,1.3,2.5);Assert.Equal(1,p.AudioTracks);var decode=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-v","error","-i",output,"-f","null","-"]);Assert.Equal(0,decode.ExitCode);
    }
    [Fact]public async Task RecoverRemuxesFragmentedRecordingWithoutChangingOriginalAndRejectsMissingStructure()
    {
        var s=await Source();var before=await File.ReadAllBytesAsync(s.File);var service=new RecoveryService(new(s.Dir));var output=Path.Combine(s.Dir,"recovered.mkv");var p=await service.RecoverAsync(s.File,output);Assert.InRange(p.Duration,1.8,2.3);Assert.Equal(before,await File.ReadAllBytesAsync(s.File));
        var bad=Path.Combine(s.Dir,"bad.mp4");await File.WriteAllTextAsync(bad,"broken input");await Assert.ThrowsAsync<IOException>(()=>service.RecoverAsync(bad,Path.Combine(s.Dir,"bad-recovered.mp4")));Assert.Equal("broken input",await File.ReadAllTextAsync(bad));
    }
    [Fact]public async Task TruncatedMatroskaTailCanBeRecoveredButMp4WithoutMoovIsRejected()
    {
        var s=await Source();var broken=Path.Combine(s.Dir,"interrupted.partial.mkv");var tool=ToolPaths.Discover();var remux=await ToolPaths.RunAsync(tool.Ffmpeg,["-v","error","-i",s.File,"-c","copy",broken]);Assert.Equal(0,remux.ExitCode);using(var stream=new FileStream(broken,FileMode.Open,FileAccess.ReadWrite))stream.SetLength(stream.Length-64);var original=await File.ReadAllBytesAsync(broken);var service=new RecoveryService(new(s.Dir));var p=await service.RecoverAsync(broken,Path.Combine(s.Dir,"recovered-tail.mkv"));Assert.InRange(p.Duration,1.5,2.3);Assert.Equal(original,await File.ReadAllBytesAsync(broken));
        var mp4=await File.ReadAllBytesAsync(s.File);int moov=mp4.AsSpan().IndexOf("moov"u8);Assert.True(moov>4);var missing=Path.Combine(s.Dir,"missing-moov.mp4");await File.WriteAllBytesAsync(missing,mp4[..(moov-4)]);await Assert.ThrowsAsync<IOException>(()=>service.RecoverAsync(missing,Path.Combine(s.Dir,"not-invented.mp4")));Assert.False(File.Exists(Path.Combine(s.Dir,"not-invented.mp4")));
    }
}

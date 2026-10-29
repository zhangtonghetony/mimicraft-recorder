using System.Diagnostics;
using System.Text;
using Mimicraft.Recorder.Core;

namespace Mimicraft.Recorder.Windows;

public sealed class NetworkProcess:IAsyncDisposable
{
    private readonly Process _process;private readonly Task _stderr;private readonly string[] _secrets;private readonly StringBuilder _diagnostic=new();private bool _stopping;
    public int Id=>_process.Id;
    public bool HasExited=>_process.HasExited;
    public int? ExitCode=>HasExited?_process.ExitCode:null;
    public string Diagnostic { get {lock(_diagnostic)return _diagnostic.ToString();} }
    public NetworkProcess(IEnumerable<string> arguments,params string[] secrets)
    {
        _secrets=secrets;
        var info=new ProcessStartInfo(ToolPaths.Discover().Ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true};foreach(var arg in arguments)info.ArgumentList.Add(arg);
        _process=Process.Start(info)??throw new IOException("无法启动网络媒体任务。");
        _stderr=Task.Run(async()=>{while(await _process.StandardError.ReadLineAsync() is {} line){foreach(var secret in _secrets)line=line.Replace(secret,StreamAddress.Redact(secret));lock(_diagnostic){_diagnostic.AppendLine(line);if(_diagnostic.Length>6000)_diagnostic.Remove(0,_diagnostic.Length-6000);}}});
    }
    public async Task WaitAsync(CancellationToken cancel=default)=>await _process.WaitForExitAsync(cancel);
    public async Task StopAsync()
    {
        if(_stopping)return;_stopping=true;
        if(!_process.HasExited)
        {
            try{await _process.StandardInput.WriteLineAsync("q");await _process.StandardInput.FlushAsync();await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(6));}
            catch(Exception e)when(e is TimeoutException or IOException or InvalidOperationException){if(!_process.HasExited)_process.Kill(true);await _process.WaitForExitAsync();}
        }
        await _stderr;
    }
    public async ValueTask DisposeAsync(){await StopAsync();_process.Dispose();}
}

public sealed class NetworkJobs(EventLog log)
{
    public static NetworkProcess PushFile(string file,string destination,bool repeat)
    {
        StreamAddress.Validate(destination);if(!File.Exists(file))throw new FileNotFoundException("推流文件不存在。");
        List<string> args=["-hide_banner","-loglevel","warning","-re"];
        if(repeat)args.AddRange(["-stream_loop","-1"]);
        args.AddRange(["-i",file,"-map","0:v:0","-map","0:a?","-c:v","copy","-c:a","aac","-rw_timeout","5000000","-f",destination.StartsWith("rtmp")?"flv":"mpegts",destination]);
        return new(args,destination);
    }
    public async Task<ProbeResult> IngestAsync(string address,string output,CancellationToken stop,IProgress<string>? progress=null)
    {
        StreamAddress.Validate(address,true);output=Path.GetFullPath(output);
        if(File.Exists(output)||Path.GetExtension(output).ToLowerInvariant() is not (".mkv" or ".mp4"))throw new IOException("请选择新的MP4/MKV文件。");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);string temp=output+".capture-"+Guid.NewGuid().ToString("N")+".mkv";string pending=output+".pending-"+Guid.NewGuid()+Path.GetExtension(output);
        await using var job=new NetworkProcess(["-hide_banner","-loglevel","warning","-rw_timeout","5000000","-analyzeduration","500000","-probesize","200000","-protocol_whitelist","http,https,tcp,tls,rtmp,rtmps,rtsp,udp,rtp,crypto","-i",address,"-map","0:v:0","-map","0:a?","-c","copy","-flush_packets","1","-cluster_time_limit","500","-f","matroska","-n",temp],address);
        log.Write("NETWORK_INGEST_START",new{source=StreamAddress.Redact(address)});progress?.Report("正在接收；停止后检查媒体并保存。");
        try
        {
            try{await job.WaitAsync(stop);}catch(OperationCanceledException)when(stop.IsCancellationRequested){await job.StopAsync();}
            await job.StopAsync();
            if(!stop.IsCancellationRequested&&job.ExitCode!=0)throw new IOException("网络输入失败："+job.Diagnostic);
            var tools=ToolPaths.Discover();if(!File.Exists(temp)||new FileInfo(temp).Length==0)throw new IOException("尚未接收到有效画面。");
            var result=await ToolPaths.RunAsync(tools.Ffmpeg,["-v","error","-fflags","+genpts+discardcorrupt","-i",temp,"-map","0:v:0","-map","0:a?","-c","copy","-n",pending],60);if(result.ExitCode!=0)throw new IOException("网络媒体收尾失败。临时材料已保留。");
            var probe=await tools.ProbeAsync(pending);File.Move(pending,output);File.Delete(temp);log.Write("NETWORK_INGEST_SAVED",new{probe.Duration,probe.Codec});return probe;
        }
        catch(Exception e){log.Write("NETWORK_INGEST_FAILED",error:new IOException(e.Message.Replace(address,StreamAddress.Redact(address))));throw;}
        finally{if(File.Exists(pending))File.Delete(pending);}
    }
}

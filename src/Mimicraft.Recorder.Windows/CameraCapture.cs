using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Mimicraft.Recorder.Windows;

public sealed class CameraCapture:IAsyncDisposable
{
    public const int Width=640,Height=360;
    private readonly object _gate=new();private readonly byte[] _latest=new byte[Width*Height*4];
    private readonly CancellationTokenSource _stop=new();private readonly Process _process;private readonly Task _reader;private readonly Task<string> _diagnostic;
    private readonly TaskCompletionSource _first=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _failure;public long Frames { get;private set; }public bool NearlyBlack { get;private set; }
    public Task FirstFrame=>_first.Task;
    public static async Task<string[]> DevicesAsync()
    {
        var result=await ToolPaths.RunAsync(ToolPaths.Discover().Ffmpeg,["-hide_banner","-list_devices","true","-f","dshow","-i","dummy"]);
        return Regex.Matches(result.Error,"\"([^\"]+)\" \\(video\\)").Select(m=>m.Groups[1].Value).ToArray();
    }
    public CameraCapture(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Contains('\n')||name.Length>2000)throw new ArgumentException("摄像头名称无效。");
        var info=new ProcessStartInfo(ToolPaths.Discover().Ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        string[] args=["-hide_banner","-loglevel","warning","-f","dshow","-i","video="+name,"-an","-vf","scale=640:360:force_original_aspect_ratio=decrease,pad=640:360:(ow-iw)/2:(oh-ih)/2,fps=15","-pix_fmt","bgra","-f","rawvideo","pipe:1"];foreach(var a in args)info.ArgumentList.Add(a);
        _process=Process.Start(info)??throw new IOException("无法打开摄像头。");_diagnostic=_process.StandardError.ReadToEndAsync();_reader=Task.Run(ReadAsync);
    }
    private async Task ReadAsync()
    {
        var buffer=new byte[_latest.Length];
        try
        {
            while(!_stop.IsCancellationRequested)
            {
                await _process.StandardOutput.BaseStream.ReadExactlyAsync(buffer,_stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(8),_stop.Token);
                lock(_gate){Buffer.BlockCopy(buffer,0,_latest,0,buffer.Length);Frames++;double mean=0;for(int i=0;i<buffer.Length;i+=400)mean+=buffer[i]+buffer[i+1]+buffer[i+2];NearlyBlack=mean/(buffer.Length/400*3)<6;}_first.TrySetResult();
            }
        }
        catch(OperationCanceledException)when(_stop.IsCancellationRequested){}
        catch(Exception e){_failure=e;_first.TrySetException(new IOException("摄像头没有输出画面；请检查物理开关、隐私权限或设备占用。",e));}
    }
    public bool CopyLatest(byte[] target)
    {
        lock(_gate){if(_failure is {} e)throw new IOException("摄像头已停止输出画面。",e);if(Frames==0)return false;Buffer.BlockCopy(_latest,0,target,0,_latest.Length);return true;}
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();try{if(!_process.HasExited)_process.Kill(true);await _process.WaitForExitAsync();}catch(InvalidOperationException){}try{await _reader;await _diagnostic;}catch{} _process.Dispose();_stop.Dispose();
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace Mimicraft.Recorder.Windows;

public sealed record ToolPaths(string Ffmpeg, string Ffprobe)
{
    public static ToolPaths Discover()
    {
        var configured = Environment.GetEnvironmentVariable("MIMICRAFT_FFMPEG_DIR");
        if (configured is not null && File.Exists(Path.Combine(configured,"ffmpeg.exe")))
            return new(Path.Combine(configured,"ffmpeg.exe"),Path.Combine(configured,"ffprobe.exe"));
        var portable=Path.Combine(AppContext.BaseDirectory,"tools");
        if(File.Exists(Path.Combine(portable,"ffmpeg.exe")))return new(Path.Combine(portable,"ffmpeg.exe"),Path.Combine(portable,"ffprobe.exe"));
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var tools = Path.Combine(dir.FullName,".tools","ffmpeg");
            if (!Directory.Exists(tools)) continue;
            var exe = Directory.GetFiles(tools,"ffmpeg.exe",SearchOption.AllDirectories).FirstOrDefault();
            if (exe is not null) return new(exe,Path.Combine(Path.GetDirectoryName(exe)!,"ffprobe.exe"));
        }
        var bundled = Path.Combine(AppContext.BaseDirectory,"tools");
        if (File.Exists(Path.Combine(bundled,"ffmpeg.exe"))) return new(Path.Combine(bundled,"ffmpeg.exe"),Path.Combine(bundled,"ffprobe.exe"));
        throw new FileNotFoundException("MEDIA_TOOLS_MISSING：未找到FFmpeg，请先运行环境检查。");
    }

    public static async Task<(int ExitCode,string Output,string Error)> RunAsync(string exe, IEnumerable<string> args, int timeoutSeconds=30)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("无法启动媒体工具。");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException("MEDIA_TOOL_TIMEOUT：媒体处理超时。"); }
        return(process.ExitCode,await stdout,await stderr);
    }

    public async Task<ProbeResult> ProbeAsync(string path,bool allowAudioOnly=false)
    {
        var result=await RunAsync(Ffprobe,["-v","error","-show_format","-show_streams","-of","json",path]);
        if(result.ExitCode!=0)throw new IOException("PROBE_FAILED："+result.Error);
        using var doc=JsonDocument.Parse(result.Output);
        var streams=doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video=streams.FirstOrDefault(s=>s.GetProperty("codec_type").GetString()=="video");
        if(video.ValueKind==JsonValueKind.Undefined&&!allowAudioOnly)throw new IOException("PROBE_NO_VIDEO：输出没有视频轨。");
        double duration=double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
        return new(video.ValueKind==JsonValueKind.Undefined?"audio":video.GetProperty("codec_name").GetString()!,video.ValueKind==JsonValueKind.Undefined?0:video.GetProperty("width").GetInt32(),video.ValueKind==JsonValueKind.Undefined?0:video.GetProperty("height").GetInt32(),duration,
            streams.Count(s=>s.GetProperty("codec_type").GetString()=="audio"), result.Output);
    }
}

public sealed record ProbeResult(string Codec,int Width,int Height,double Duration,int AudioTracks,string Json);

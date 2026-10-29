using System.Diagnostics;
using System.Globalization;
using Mimicraft.Recorder.Core;

namespace Mimicraft.Recorder.Windows;

public sealed record MediaInput(string Path,ClipRange Range);
public sealed record ExportOptions(int Width=1280,int Height=720,int Fps=30,int BitrateKbps=4000,double Speed=1,bool Gif=false,int GifFps=12,string? WatermarkPng=null,PixelRegion? Crop=null);
public sealed record BatchResult(IReadOnlyList<string> Files,IReadOnlyList<string> Errors);

public sealed class MediaJobs(EventLog log)
{
    public async Task<BatchResult> BatchMarkAsync(IReadOnlyList<string> files,string newDirectory,string watermark,CancellationToken cancel=default,IProgress<string>? progress=null)
    {
        if(files.Count is <1 or >100||!File.Exists(watermark))throw new ArgumentException("请选择1～100个视频和有效画面标记。");
        if(Directory.Exists(newDirectory)||File.Exists(newDirectory))throw new IOException("批量输出需要新的文件夹，避免覆盖。");
        Directory.CreateDirectory(newDirectory);var done=new List<string>();var errors=new List<string>();
        for(int index=0;index<files.Count;index++)
        {
            var file=files[index];cancel.ThrowIfCancellationRequested();progress?.Report($"正在处理 {index+1}/{files.Count}：{Path.GetFileName(file)}");
            try
            {
                var p=await ToolPaths.Discover().ProbeAsync(file);var output=Path.Combine(newDirectory,$"{index+1:000}_"+Path.GetFileNameWithoutExtension(file)+".mp4");
                await ExportAsync([new(file,new(0,p.Duration))],output,new(WatermarkPng:watermark),cancel);done.Add(output);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception e){errors.Add(Path.GetFileName(file)+"："+e.Message);log.Write("BATCH_ITEM_FAILED",new{index},error:e);progress?.Report($"第{index+1}项失败，继续处理其余文件。");}
        }
        return new(done,errors);
    }
    public async Task<ProbeResult> ExportAsync(IReadOnlyList<MediaInput> inputs,string output,ExportOptions options,CancellationToken cancel=default,IProgress<string>? progress=null)
    {
        if(inputs.Count==0||inputs.Count>100)throw new ArgumentException("请选择1～100个片段。");
        new RecordingSettings(options.Width,options.Height,options.Fps,BitrateKbps:options.BitrateKbps).Validate();
        if(!double.IsFinite(options.Speed)||options.Speed is < .25 or > 4||options.GifFps is < 1 or > 30)throw new ArgumentException("倍速或GIF帧率无效。");
        output=Path.GetFullPath(output);
        if(File.Exists(output)||inputs.Any(i=>string.Equals(Path.GetFullPath(i.Path),output,StringComparison.OrdinalIgnoreCase)))throw new IOException("另存文件已存在，请换一个名称。原文件不会覆盖。");
        var extension=Path.GetExtension(output).ToLowerInvariant();
        if(options.Gif?extension!=".gif":extension is not (".mp4" or ".mkv"))throw new ArgumentException("请选择MP4、MKV或GIF后缀。");
        var tools=ToolPaths.Discover();var probes=new List<ProbeResult>();
        foreach(var input in inputs){cancel.ThrowIfCancellationRequested();var p=await tools.ProbeAsync(input.Path);input.Range.Validate(p.Duration);options.Crop?.Validate(p.Width,p.Height);probes.Add(p);}
        bool audio=probes.Any(p=>p.AudioTracks>0)&&!options.Gif;
        var id=Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary=Path.Combine(Path.GetDirectoryName(output)!,".mimicraft-job-"+id+extension);
        List<string> args=["-hide_banner","-loglevel","warning","-nostats"];
        foreach(var input in inputs)args.AddRange(["-i",input.Path]);
        if(options.WatermarkPng is not null)args.AddRange(["-i",options.WatermarkPng]);
        var filter=new List<string>();
        for(int i=0;i<inputs.Count;i++)
        {
            var r=inputs[i].Range;
            string crop=options.Crop is {} c?$"crop={c.Width}:{c.Height}:{c.X}:{c.Y},":"";
            filter.Add($"[{i}:v:0]trim=start={N(r.Start)}:end={N(r.End)},setpts=(PTS-STARTPTS)/{N(options.Speed)},{crop}scale={options.Width}:{options.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={options.Width}:{options.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={options.Fps},format=yuv420p[v{i}]");
            if(audio)
            {
                if(probes[i].AudioTracks>0)filter.Add($"[{i}:a:0]atrim=start={N(r.Start)}:end={N(r.End)},asetpts=PTS-STARTPTS,{Tempo(options.Speed)},aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,apad,atrim=duration={N(r.Duration/options.Speed)}[a{i}]");
                else filter.Add($"anullsrc=r=48000:cl=stereo,atrim=duration={N(r.Duration/options.Speed)}[a{i}]");
            }
        }
        string pins=string.Concat(Enumerable.Range(0,inputs.Count).Select(i=>$"[v{i}]"+(audio?$"[a{i}]":"")));
        filter.Add(pins+$"concat=n={inputs.Count}:v=1:a={(audio?1:0)}[joined]"+(audio?"[sound]":""));
        var video="[joined]";
        if(options.WatermarkPng is not null){filter.Add($"[joined][{inputs.Count}:v:0]overlay=0:0:format=auto[marked]");video="[marked]";}
        if(options.Gif)filter.Add($"{video}fps={options.GifFps},split[p][q];[p]palettegen=stats_mode=diff[palette];[q][palette]paletteuse=dither=sierra2_4a[gif]");
        args.AddRange(["-filter_complex",string.Join(';',filter),"-map",options.Gif?"[gif]":video]);
        if(options.Gif)args.AddRange(["-an","-loop","0"]);
        else
        {
            args.AddRange(["-c:v","libopenh264","-b:v",options.BitrateKbps+"k"]);
            if(audio)args.AddRange(["-map","[sound]","-c:a","aac","-b:a","128k"]);else args.Add("-an");
            if(extension==".mp4")args.AddRange(["-movflags","+faststart"]);
        }
        args.AddRange(["-n",temporary]);
        log.Write("MEDIA_JOB_STARTED",new{id,count=inputs.Count,options});
        try
        {
            progress?.Report("正在处理片段，可取消；原文件保持不变…");
            var diagnostic=await RunCancelableAsync(tools.Ffmpeg,args,cancel);
            cancel.ThrowIfCancellationRequested();
            var result=await tools.ProbeAsync(temporary);
            double expected=inputs.Sum(i=>i.Range.Duration)/options.Speed;
            if(result.Width!=options.Width||result.Height!=options.Height||Math.Abs(result.Duration-expected)>Math.Max(.5,expected*.02)||result.AudioTracks!=(audio?1:0))throw new IOException("输出参数校验失败，结果未发布。");
            File.Move(temporary,output);log.Write("MEDIA_JOB_COMPLETED",new{id,result.Codec,result.Duration,diagnostic});
            progress?.Report("已保存："+output);return result;
        }
        catch(Exception e){log.Write("MEDIA_JOB_FAILED",new{id},error:e);throw;}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static string Tempo(double speed)
    {
        var parts=new List<string>();
        while(speed<.5){parts.Add("atempo=0.5");speed/=.5;}
        while(speed>2){parts.Add("atempo=2");speed/=2;}
        parts.Add("atempo="+N(speed));return string.Join(',',parts);
    }
    private static string N(double v)=>v.ToString("0.########",CultureInfo.InvariantCulture);
    public static async Task<string> RunCancelableAsync(string executable,IEnumerable<string> arguments,CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(var argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("无法启动媒体任务。");
        var error=process.StandardError.ReadToEndAsync();var stdout=process.StandardOutput.ReadToEndAsync();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromHours(6));
        try{await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();if(cancel.IsCancellationRequested)throw new OperationCanceledException(cancel);throw new TimeoutException("媒体任务超过6小时。");}
        await stdout;string diagnostic=await error;
        if(process.ExitCode!=0)throw new IOException("媒体处理失败："+(diagnostic.Length>4000?diagnostic[^4000..]:diagnostic));
        return diagnostic.Length>2000?diagnostic[^2000..]:diagnostic;
    }
}

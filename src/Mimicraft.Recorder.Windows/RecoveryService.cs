namespace Mimicraft.Recorder.Windows;

public sealed class RecoveryService(EventLog log)
{
    public static string[] Candidates(string directory)=>Directory.Exists(directory)?Directory.GetFiles(directory).Where(p=>p.Contains(".partial.mkv")||p.Contains(".capture-")||p.Contains(".pending.")).OrderByDescending(File.GetLastWriteTime).ToArray():[];
    public async Task<ProbeResult> RecoverAsync(string input,string output,CancellationToken cancel=default)
    {
        input=Path.GetFullPath(input);output=Path.GetFullPath(output);
        if(!File.Exists(input)||File.Exists(output)||string.Equals(input,output,StringComparison.OrdinalIgnoreCase)||Path.GetExtension(output).ToLowerInvariant() is not (".mkv" or ".mp4"))throw new IOException("需要已有输入和新的MP4/MKV副本路径。");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);var pending=output+".recovery-"+Guid.NewGuid()+Path.GetExtension(output);var tools=ToolPaths.Discover();
        try
        {
            var result=await ToolPaths.RunAsync(tools.Ffprobe,["-v","error","-show_streams","-of","json",input]);
            if(result.ExitCode!=0)throw new IOException("无法读取媒体结构。缺失moov或关键码流不能凭空重建；请保留原件及其他备份。");
            await MediaJobs.RunCancelableAsync(tools.Ffmpeg,["-v","warning","-err_detect","ignore_err","-fflags","+genpts+discardcorrupt","-i",input,"-map","0:v?","-map","0:a?","-c","copy","-n",pending],cancel);
            var p=await tools.ProbeAsync(pending,true);if(p.Duration<=0)throw new IOException("没有可恢复的媒体时长。");
            try{var diagnostic=await MediaJobs.RunCancelableAsync(tools.Ffmpeg,["-v","error","-i",pending,"-f","null","-"],cancel);if(!string.IsNullOrWhiteSpace(diagnostic))throw new IOException("完整解码报告错误。");}
            catch(IOException){throw new IOException("恢复副本未通过完整解码，原文件保留，不能将该文件标为成功修复。");}
            File.Move(pending,output);log.Write("RECOVERY_SAVED",new{p.Codec,p.Duration});return p;
        }
        catch(Exception e){log.Write("RECOVERY_FAILED",error:e);throw;}
        finally{if(File.Exists(pending))File.Delete(pending);}
    }
}

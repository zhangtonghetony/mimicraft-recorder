using System.Diagnostics;
using Mimicraft.Recorder.Core;

namespace Mimicraft.Recorder.Windows;

public sealed class RecorderEngine
{
    private readonly EventLog _log;
    private readonly SemaphoreSlim _operation=new(1,1);
    private ScreenCapture? _capture;
    private Process? _encoder;
    private Task<string>? _stderr;
    private CancellationTokenSource? _stop;
    private Task? _pump;
    private string? _temporary;
    private string? _final;
    private string? _id;
    private string _selectedEncoder="";
    private RecordingSettings? _settings;
    private WasapiAudioSession? _audio;
    private int _captureWidth,_captureHeight;
    private string _streamWarning="";
    public string StreamWarning=>_streamWarning;
    public double AudioPeak=>_audio?.Peak??0;
    private volatile bool _paused;
    private readonly SemaphoreSlim _frameGate=new(1,1);
    private OverlayBitmap? _overlay;
    private PixelRegion? _region;
    public void SetOverlay(OverlayBitmap? overlay)=>Volatile.Write(ref _overlay,overlay);
    public Func<int,int,OverlayBitmap?>? OverlayFactory { get;set; }
    public Action<byte[],int,int>? FrameTransform { get;set; }
    public (int Width,int Height) CaptureSize=>(_captureWidth,_captureHeight);
    public void MoveRegion(PixelRegion region)
    {
        var original=_settings?.Region??throw new InvalidOperationException("当前不是区域录制。");
        if(region.Width!=original.Width||region.Height!=original.Height)throw new ArgumentException("录制中只可移动选区，不能改变大小。");
        region.Validate(CaptureSize.Width,CaptureSize.Height);Volatile.Write(ref _region,region);
        _log.Write("REGION_MOVED",new{region},_id);
    }
    public SessionState State { get; }=new();
    public Stopwatch Clock { get; }=new();
    public long WrittenFrames { get; private set; }
    public long LateFrames { get; private set; }
    public event Action<byte[],int,int>? PreviewFrame;

    public RecorderEngine(EventLog log) { _log=log; State.Changed+=s=>_log.Write("SESSION_STATE",new {state=s.ToString()},_id); }

    public async Task StartAsync(CaptureSource source,RecordingSettings settings,string directory)
    {
        await _operation.WaitAsync();
        try
        {
            if(State.IsBusy)return;
            _id=Guid.NewGuid().ToString("N");
            if(!State.TryBegin())return;
            try
            {
                settings.Validate();_settings=settings;_region=settings.Region;_streamWarning="";
                var tools=ToolPaths.Discover();
                if(!File.Exists(tools.Ffprobe))throw new FileNotFoundException("FFprobe缺失。");
                Directory.CreateDirectory(directory);
                var probe=Path.Combine(directory,".write-test-"+_id);
                using(File.Create(probe)){} File.Delete(probe);
                var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!);
                if(drive.AvailableFreeSpace<512L*1024*1024)throw new IOException("DISK_LOW：可用空间不足512MB。");
                bool audioOnly=settings.Audio?.AudioOnly==true;
                _final=Path.Combine(directory,$"录制_{DateTime.Now:yyyyMMdd_HHmmss}_{_id[..6]}.{(audioOnly?settings.Audio!.Format:settings.Container)}");
                _temporary=_final+".partial.mkv";
                _selectedEncoder=audioOnly&&(settings.Destinations?.Length??0)==0?"aac":await SelectEncoder(tools,settings);
                if(!audioOnly){_capture=new();_capture.Start(source);await _capture.FirstFrame.WaitAsync(TimeSpan.FromSeconds(12));}
                _captureWidth=_capture?.Width??64;_captureHeight=_capture?.Height??64;
                settings.Region?.Validate(_captureWidth,_captureHeight);
                SetOverlay(audioOnly?null:OverlayFactory?.Invoke(_captureWidth,_captureHeight));
                if(settings.Audio?.Enabled==true)_audio=new(settings.Audio,_temporary+".audio.wav");
                _encoder=StartEncoder(tools,settings,settings.Region?.Width??_captureWidth,settings.Region?.Height??_captureHeight,_temporary,_selectedEncoder,_audio?.PipePath);
                _stderr=ReadDiagnosticAsync(_encoder);
                _stop=new();_paused=false;WrittenFrames=0;LateFrames=0;_audio?.Arm();Clock.Restart();
                var firstWritten=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pump=Task.Run(()=>PumpAsync(firstWritten,_stop.Token));
                await firstWritten.Task.WaitAsync(TimeSpan.FromSeconds(12));
                if(_encoder.HasExited)throw new IOException("ENCODER_START_FAILED："+await _stderr);
                State.Move(RecordingState.Recording);
                _log.Write("RECORDING_STARTED",new {source=audioOnly?"audio-only":source.IsWindow?"window":"monitor",settings=settings with{Destinations=settings.Destinations?.Select(StreamAddress.Redact).ToArray()},encoder=_selectedEncoder,captureWidth=_captureWidth,captureHeight=_captureHeight},_id);
            }
            catch(Exception e)
            {
                await CleanupAsync();
                State.Move(RecordingState.Failed);_log.Write("START_FAILED",recordingId:_id,error:new IOException(Sanitize(e.Message)));if(settings.Destinations is {Length:>0})throw new IOException(Sanitize(e.Message));throw;
            }
        }
        finally{_operation.Release();}
    }

    private async Task<string> SelectEncoder(ToolPaths tools,RecordingSettings settings)
    {
        var candidates=settings.Encoder=="auto"?new[]{"h264_qsv","h264_nvenc","h264_amf","libopenh264"}:new[]{settings.Encoder};
        foreach(var name in candidates)
        {
            (int ExitCode,string Output,string Error) result;
            try { result=await ToolPaths.RunAsync(tools.Ffmpeg,["-hide_banner","-loglevel","error","-f","lavfi","-i",$"color=c=black:s={settings.Width}x{settings.Height}:r={settings.Fps}","-frames:v","2","-an","-c:v",name,"-b:v",settings.BitrateKbps+"k","-profile:v",ProfileName(name,settings.Profile),"-f","null","-"],15); }
            catch(TimeoutException e){_log.Write("ENCODER_PROBE_TIMEOUT",new{encoder=name},_id,e);continue;}
            _log.Write("ENCODER_PROBE",new {encoder=name,exitCode=result.ExitCode,diagnostic=result.Error.Length>3000?result.Error[^3000..]:result.Error},_id);
            if(result.ExitCode==0)return name;
            if(candidates.Length>1)_log.Write("ENCODER_FALLBACK",new {encoder=name},_id);
        }
        throw new IOException("ENCODER_UNAVAILABLE：可用编码器未能初始化，请查看日志。");
    }

    private static Process StartEncoder(ToolPaths tools,RecordingSettings s,int width,int height,string output,string encoder,string? audioPipe)
    {
        var info=new ProcessStartInfo(tools.Ffmpeg) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true};
        if(s.Audio?.AudioOnly==true&&(s.Destinations?.Length??0)==0)
        {
            List<string> audioArgs=["-hide_banner","-loglevel","warning","-nostats","-f","f32le","-ar","48000","-ac","2","-i",audioPipe!,"-map","0:a:0","-c:a","aac","-b:a",s.Audio.BitrateKbps+"k","-ar",s.Audio.SampleRate.ToString()];
            if(s.Audio.Denoise)audioArgs.AddRange(["-af",AudioFilter(s.Audio)]);audioArgs.AddRange(["-f","matroska","-n",output]);foreach(var a in audioArgs)info.ArgumentList.Add(a);return Process.Start(info)??throw new IOException("音频编码器无法启动。");
        }
        string filter=VideoFilter(s);
        List<string> args=["-hide_banner","-loglevel","warning","-nostats","-thread_queue_size","256","-f","rawvideo","-pixel_format","bgra","-video_size",$"{width}x{height}","-framerate",s.Fps.ToString(),"-probesize","32","-analyzeduration","0","-i","pipe:0"];
        if(audioPipe is not null)args.AddRange(["-thread_queue_size","512","-f","f32le","-ar","48000","-ac","2","-probesize","32","-analyzeduration","0","-i",audioPipe,"-map","0:v:0","-map","1:a:0","-c:a","aac","-b:a",s.Audio!.BitrateKbps+"k","-ar",s.Audio.SampleRate.ToString()]);else args.AddRange(["-map","0:v:0","-an"]);
        if(s.Audio?.Denoise==true)args.AddRange(["-af",AudioFilter(s.Audio)]);
        args.AddRange(["-vf",filter,"-c:v",encoder,"-b:v",s.BitrateKbps+"k","-profile:v",ProfileName(encoder,s.Profile)]);
        if(s.Destinations is {Length:>0})
        {
            args.AddRange(["-flags","+global_header","-f","tee","-n","[f=matroska]"+output.Replace("\\","/").Replace(":","\\:")+"|"+string.Join('|',s.Destinations.Select(d=>$"[f={(d.StartsWith("rtmp")?"flv":"mpegts")}:onfail=ignore:use_fifo=1:rw_timeout=5000000:fifo_options=drop_pkts_on_overflow=1]{d}"))]);
        }
        else args.AddRange(["-f","matroska","-n",output]);
        foreach(var arg in args)info.ArgumentList.Add(arg);
        return Process.Start(info)??throw new IOException("ENCODER_START_FAILED：无法启动编码器。");
    }
    public static string ProfileName(string encoder,string profile)=>encoder=="libopenh264"&&profile=="baseline"?"constrained_baseline":profile;
    public static string AudioFilter(AudioSettings audio)=>"afftdn=nr="+audio.ReductionDb.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static string VideoFilter(RecordingSettings s)=>$"scale={s.Width}:{s.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={s.Width}:{s.Height}:(ow-iw)/2:(oh-ih)/2,hue=s={s.Saturation.ToString(System.Globalization.CultureInfo.InvariantCulture)},format=yuv420p";

    private async Task PumpAsync(TaskCompletionSource first,CancellationToken stop)
    {
        var capture=_capture;var settings=_settings!;var encoder=_encoder!;
        int width=_captureWidth,height=_captureHeight;
        var buffer=new byte[checked(width*height*4)];
        var cropped=settings.Region is {} r?new byte[checked(r.Width*r.Height*4)]:null;
        var nextPreview=TimeSpan.Zero;
        try
        {
            while(!stop.IsCancellationRequested)
            {
                _audio?.CheckHealth();
                if(_paused){await Task.Delay(15,stop);continue;}
                await _frameGate.WaitAsync(stop);
                try
                {
                if(_paused)continue;
                if(capture is not null&&!capture.CopyLatest(buffer)){await Task.Delay(5,stop);continue;}
                FrameTransform?.Invoke(buffer,width,height);
                FrameComposite.Apply(buffer,width,height,Volatile.Read(ref _overlay));
                // Finish the current raw frame before stopping, otherwise FFmpeg
                // may receive a truncated BGRA frame on an ordinary user stop.
                if(Volatile.Read(ref _region) is {} region&&cropped is not null)
                {
                    for(int y=0;y<region.Height;y++)Buffer.BlockCopy(buffer,((region.Y+y)*width+region.X)*4,cropped,y*region.Width*4,region.Width*4);
                }
                if(!(settings.Audio?.AudioOnly==true&&(settings.Destinations?.Length??0)==0))await encoder.StandardInput.BaseStream.WriteAsync(cropped??buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(8));
                WrittenFrames++;first.TrySetResult();
                _audio?.WriteToVideoFrame(WrittenFrames,settings.Fps);
                if(WrittenFrames%(settings.Fps*5)==0&&new DriveInfo(Path.GetPathRoot(_final!)!).AvailableFreeSpace<128L*1024*1024)throw new IOException("DISK_LOW：磁盘剩余不足128MB，会话停止，临时材料保留。");
                }
                finally{_frameGate.Release();}
                if(Clock.Elapsed>=nextPreview){PreviewFrame?.Invoke(buffer,width,height);nextPreview=Clock.Elapsed+TimeSpan.FromMilliseconds(300);}
                var due=FrameClock.FrameTime(WrittenFrames,settings.Fps)-Clock.Elapsed;
                if(due>TimeSpan.Zero)await Task.Delay(due,stop);
                else if(due<TimeSpan.FromMilliseconds(-100))LateFrames++;
                if(WrittenFrames%settings.Fps==0)
                    _log.Write("RECORDING_PROGRESS",new {writtenFrames=WrittenFrames,lateFrames=LateFrames,capturedFrames=capture?.CapturedFrames??0,elapsedMs=Clock.ElapsedMilliseconds},_id);
            }
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) { first.TrySetCanceled(); }
        catch(Exception e){first.TrySetException(e);_log.Write("CAPTURE_PUMP_FAILED",recordingId:_id,error:e);throw;}
    }

    public async Task CheckHealthAsync()
    {
        if(State.Value is RecordingState.Recording or RecordingState.Paused && _pump is {IsCompleted:true})await StopAsync();
    }

    public async Task TogglePauseAsync()
    {
        await _operation.WaitAsync();
        try
        {
            if(State.Value is not (RecordingState.Recording or RecordingState.Paused))return;
            await _frameGate.WaitAsync();
            try
            {
                _paused=!_paused;
                _audio?.SetPaused(_paused);
                if(_paused){Clock.Stop();State.Move(RecordingState.Paused);}
                else{_audio?.Arm();Clock.Start();State.Move(RecordingState.Recording);}
            }
            finally{_frameGate.Release();}
        }
        finally{_operation.Release();}
    }

    public async Task<RecordingEntry?> StopAsync()
    {
        await _operation.WaitAsync();
        try
        {
            if(State.Value is not (RecordingState.Recording or RecordingState.Paused))return null;
            State.Move(RecordingState.Stopping);
            try
            {
                _stop!.Cancel();
                try{await _pump!;}catch(OperationCanceledException){}
                Clock.Stop();
                _capture?.Dispose();_capture=null;
                State.Move(RecordingState.Finalizing);
                _encoder!.StandardInput.Close();
                if(_audio is not null)await _audio.CompleteAsync();
                using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)))await _encoder.WaitForExitAsync(timeout.Token);
                var diagnostic=await _stderr!;
                if(_encoder.ExitCode!=0)throw new IOException("ENCODER_EXIT_FAILED："+Sanitize(diagnostic));
                var tools=ToolPaths.Discover();
                bool audioOnly=_settings!.Audio?.AudioOnly==true;
                var pending=_final!+".pending."+(audioOnly?_settings.Audio!.Format:_settings.Container);
                List<string> remuxArgs=["-hide_banner","-loglevel","error","-i",_temporary!];
                if(audioOnly){remuxArgs.AddRange(["-map","0:a:0","-c:a",_settings.Audio!.Format=="wav"?"pcm_s16le":_settings.Audio.Format=="mp3"?"libmp3lame":"aac","-ar",_settings.Audio.SampleRate.ToString()]);if(_settings.Audio.Format!="wav")remuxArgs.AddRange(["-b:a",_settings.Audio.BitrateKbps+"k"]);}
                else{remuxArgs.AddRange(["-map","0:v:0","-map","0:a?","-c","copy"]);if(_settings.Container=="mp4")remuxArgs.AddRange(["-movflags","+faststart"]);}
                remuxArgs.AddRange(["-n",pending]);
                var remux=await ToolPaths.RunAsync(tools.Ffmpeg,remuxArgs,30);
                if(remux.ExitCode!=0)throw new IOException("FINALIZE_FAILED："+remux.Error);
                var probe=await tools.ProbeAsync(pending,allowAudioOnly:audioOnly);
                if((!audioOnly&&(probe.Codec!="h264"||probe.Width!=_settings!.Width||probe.Height!=_settings.Height))||probe.AudioTracks!=(_settings.Audio?.Enabled==true?1:0)||probe.Duration<=0)
                    throw new IOException("VALIDATION_FAILED：成片参数与录制设置不一致。");
                var finishedAudio=_audio;_audio=null;if(finishedAudio is not null)try{finishedAudio.Dispose();}catch(Exception e){_log.Write("AUDIO_CLEANUP_FAILED",recordingId:_id,error:e);}
                File.Move(pending,_final!);
                DeleteTemporary(_temporary!);DeleteTemporary(_temporary!+".audio.wav");
                try{File.WriteAllText(_final!+".probe.json",probe.Json);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){_log.Write("SIDECAR_WRITE_FAILED",recordingId:_id,error:e);}
                var entry=new RecordingEntry(_final!,DateTime.Now,probe.Duration,new FileInfo(_final!).Length,_selectedEncoder);
                _log.Write("RECORDING_COMPLETED",new {probe.Codec,probe.Width,probe.Height,probe.Duration,writtenFrames=WrittenFrames,lateFrames=LateFrames,encoder=_selectedEncoder},_id);
                State.Move(RecordingState.Completed);return entry;
            }
            catch(Exception e)
            {
                Clock.Stop();State.Move(RecordingState.Failed);_log.Write("RECORDING_FAILED",recordingId:_id,error:e);
                if(_encoder is {HasExited:true}&&_stderr is not null)_log.Write("ENCODER_FAILURE_DIAGNOSTIC",new{diagnostic=Sanitize(await _stderr)},_id);
                throw;
            }
            finally{await CleanupAsync();}
        }
        finally{_operation.Release();}
    }

    private async Task CleanupAsync()
    {
        _stop?.Cancel();
        var audio=_audio;_audio=null;if(audio is not null)try{audio.Dispose();}catch(Exception e){_log.Write("AUDIO_CLEANUP_FAILED",recordingId:_id,error:e);}
        if(_encoder is not null)
        {
            try{if(!_encoder.HasExited)_encoder.Kill(true);await _encoder.WaitForExitAsync();}catch(InvalidOperationException){}
        }
        if(_pump is not null)try{await _pump;}catch{}
        _capture?.Dispose();_capture=null;
        if(_stderr is not null)try{await _stderr;}catch{}
        _encoder?.Dispose();_encoder=null;_pump=null;_stderr=null;_stop?.Dispose();_stop=null;Clock.Stop();
    }
    private string Sanitize(string value){foreach(var address in _settings?.Destinations??[])value=value.Replace(address,StreamAddress.Redact(address));return value;}
    private void DeleteTemporary(string path){try{if(File.Exists(path))File.Delete(path);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){_log.Write("TEMPORARY_CLEANUP_FAILED",recordingId:_id,error:e);}}
    private async Task<string> ReadDiagnosticAsync(Process process)
    {
        var buffer=new System.Text.StringBuilder();while(await process.StandardError.ReadLineAsync().ConfigureAwait(false) is {} raw)
        {
            string line=Sanitize(raw);buffer.AppendLine(line);if(buffer.Length>8000)buffer.Remove(0,buffer.Length-8000);
            if(_settings?.Destinations is {Length:>0}&&(line.Contains("failed",StringComparison.OrdinalIgnoreCase)||line.Contains("refused",StringComparison.OrdinalIgnoreCase)||line.Contains("error",StringComparison.OrdinalIgnoreCase))){_streamWarning=line.Length>200?line[..200]:line;_log.Write("STREAM_WARNING",new{message=_streamWarning},_id);}
        }
        return buffer.ToString();
    }
}

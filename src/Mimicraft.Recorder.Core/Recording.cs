namespace Mimicraft.Recorder.Core;

public enum RecordingState { Idle, Starting, Recording, Paused, Stopping, Finalizing, Completed, Failed }

public sealed record RecordingSettings(int Width = 1280, int Height = 720, int Fps = 30, string Encoder = "auto",
    PixelRegion? Region = null, int BitrateKbps = 4000, double Saturation = 1, string Profile = "main", string Container = "mp4",AudioSettings? Audio=null,string[]? Destinations=null)
{
    public void Validate()
    {
        if (Width < 64 || Height < 64 || Width > 3840 || Height > 2160 || Width % 2 != 0 || Height % 2 != 0)
            throw new ArgumentException("输出宽高必须为64～3840/2160范围内的偶数。");
        if (!new[] { 15, 20, 24, 25, 30, 60 }.Contains(Fps)) throw new ArgumentException("不支持该帧率。");
        if (Encoder is not ("auto" or "h264_qsv" or "libopenh264")) throw new ArgumentException("不支持该编码器。");
        if (BitrateKbps is < 256 or > 50000 || !double.IsFinite(Saturation) || Saturation is < 0 or > 2) throw new ArgumentException("画质或饱和度超出范围。");
        if (Profile is not ("baseline" or "main" or "high") || Container is not ("mp4" or "mkv")) throw new ArgumentException("不支持的编码规格或容器。");
        Region?.Validate();
        Audio?.Validate();
        foreach(var address in Destinations??[])StreamAddress.Validate(address);
        if(Destinations?.Length>3)throw new ArgumentException("最多同时推送3个目的端。");
    }
}

public static class StreamAddress
{
    public static Uri Validate(string value,bool input=false)
    {
        if(string.IsNullOrWhiteSpace(value)||value.IndexOfAny(['|','[',']','\'', '"','\\','\r','\n'])>=0||!Uri.TryCreate(value,UriKind.Absolute,out var uri)||!(input?new[]{"http","https","rtmp","rtmps","rtsp","udp","tcp"}:new[]{"rtmp","rtmps","udp","tcp"}).Contains(uri.Scheme))throw new ArgumentException("网络地址无效或协议不受支持。");
        return uri;
    }
    public static string Redact(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)?$"{uri.Scheme}://{uri.Host}:{uri.Port}/[redacted]":"[redacted]";
}

public sealed class SessionState
{
    private readonly object _gate = new();
    private RecordingState _value = RecordingState.Idle;
    public RecordingState Value { get { lock (_gate) return _value; } }
    public bool IsBusy => Value is RecordingState.Starting or RecordingState.Recording or RecordingState.Paused or RecordingState.Stopping or RecordingState.Finalizing;
    public event Action<RecordingState>? Changed;

    public bool TryBegin()
    {
        lock (_gate)
        {
            if (_value is not (RecordingState.Idle or RecordingState.Completed or RecordingState.Failed)) return false;
            _value = RecordingState.Starting;
        }
        Changed?.Invoke(RecordingState.Starting);
        return true;
    }

    public void Move(RecordingState next)
    {
        lock (_gate)
        {
            bool allowed = (_value, next) switch
            {
                (RecordingState.Starting, RecordingState.Recording) => true,
                (RecordingState.Recording, RecordingState.Paused) => true,
                (RecordingState.Paused, RecordingState.Recording) => true,
                (RecordingState.Paused, RecordingState.Stopping) => true,
                (RecordingState.Recording, RecordingState.Stopping) => true,
                (RecordingState.Stopping, RecordingState.Finalizing) => true,
                (RecordingState.Finalizing, RecordingState.Completed) => true,
                (_, RecordingState.Failed) when IsBusy => true,
                _ => false
            };
            if (!allowed) throw new InvalidOperationException($"不允许的会话状态：{_value} → {next}");
            _value = next;
        }
        Changed?.Invoke(next);
    }
}

public static class FrameClock
{
    public static long DueFrame(TimeSpan elapsed, int fps) => Math.Max(0, (long)Math.Floor(elapsed.TotalSeconds * fps));
    public static TimeSpan FrameTime(long frame, int fps) => TimeSpan.FromSeconds((double)frame / fps);
}

public sealed record RecordingEntry(string Path, DateTime Created, double Duration, long Bytes, string Encoder)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Summary => $"{TimeSpan.FromSeconds(Duration):mm\\:ss}  ·  {Bytes / 1024d / 1024d:0.0} MB  ·  {Created:MM-dd HH:mm}";
}

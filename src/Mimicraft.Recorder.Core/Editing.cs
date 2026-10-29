namespace Mimicraft.Recorder.Core;

public sealed record PixelRegion(int X, int Y, int Width, int Height)
{
    public void Validate(int sourceWidth = int.MaxValue, int sourceHeight = int.MaxValue)
    {
        if (X < 0 || Y < 0 || Width < 16 || Height < 16 || (long)X + Width > sourceWidth || (long)Y + Height > sourceHeight)
            throw new ArgumentException("选区必须至少16×16像素，并完整位于录制来源内。");
    }
}

public sealed record ClipRange(double Start, double End)
{
    public override string ToString()=>$"{Start:0.###} → {End:0.###} 秒   ·   {Duration:0.###} 秒";
    public double Duration => End - Start;
    public void Validate(double duration)
    {
        if (!double.IsFinite(Start) || !double.IsFinite(End) || Start < 0 || End - Start < .05 || End > duration + .001)
            throw new ArgumentException("片段必须在原视频内，且至少0.05秒。");
    }
}

public sealed class EditTimeline
{
    private readonly Stack<ClipRange[]> _undo = new(), _redo = new();
    private ClipRange[] _ranges;
    public double Duration { get; }
    public IReadOnlyList<ClipRange> Ranges => Array.AsReadOnly(_ranges);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public EditTimeline(double duration)
    {
        if (!double.IsFinite(duration) || duration < .05) throw new ArgumentException("视频过短或时长无效。");
        Duration = duration; _ranges = [new(0, duration)];
    }
    public void Replace(IEnumerable<ClipRange> ranges)
    {
        var next = ranges.ToArray();
        if (next.Length == 0) throw new ArgumentException("至少保留一个片段。");
        foreach (var range in next) range.Validate(Duration);
        _undo.Push(_ranges); _redo.Clear(); _ranges = next;
    }
    public void Trim(int index, double start, double end) => Replace(_ranges.Select((r, i) => i == index ? new(start, end) : r));
    public void Split(int index, double time)
    {
        var old = _ranges[index]; new ClipRange(old.Start, time).Validate(old.End); new ClipRange(time, old.End).Validate(Duration);
        var list = _ranges.ToList(); list.RemoveAt(index); list.InsertRange(index, [new(old.Start, time), new(time, old.End)]); Replace(list);
    }
    public void Delete(int index) => Replace(_ranges.Where((_, i) => i != index));
    public void Reset() => Replace([new(0, Duration)]);
    public void Undo() { if (CanUndo) { _redo.Push(_ranges); _ranges = _undo.Pop(); } }
    public void Redo() { if (CanRedo) { _undo.Push(_ranges); _ranges = _redo.Pop(); } }
}

public sealed class SchedulePlan(DateTimeOffset start, TimeSpan? limit)
{
    public DateTimeOffset Start { get; } = start;
    public TimeSpan? Limit { get; } = limit;
    public bool Consumed { get; private set; }
    public string? MissReason { get; private set; }
    public bool Poll(DateTimeOffset now, bool busy)
    {
        if (Consumed || now < Start) return false;
        Consumed = true;
        if (now - Start > TimeSpan.FromSeconds(60)) { MissReason = "已错过计划时间超过60秒，请重新安排。"; return false; }
        if (busy) { MissReason = "已有录制或处理任务，计划已取消。"; return false; }
        return true;
    }
}

public sealed record OverlayBitmap(int Width, int Height, byte[] Pixels);
public static class FrameComposite
{
    // Source and overlay are straight-alpha BGRA; overlay dimensions match the source.
    public static void Apply(byte[] frame, int width, int height, OverlayBitmap? overlay)
    {
        if (overlay is null) return;
        if (overlay.Width != width || overlay.Height != height || overlay.Pixels.Length != frame.Length) throw new ArgumentException("合成图尺寸不一致。");
        var src = overlay.Pixels;
        for (int p = 0; p < frame.Length; p += 4)
        {
            int a = src[p + 3]; if (a == 0) continue;
            for (int c = 0; c < 3; c++) frame[p + c] = (byte)((src[p + c] * a + frame[p + c] * (255 - a) + 127) / 255);
            frame[p + 3] = 255;
        }
    }
}

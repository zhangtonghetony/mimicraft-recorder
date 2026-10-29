using Mimicraft.Recorder.Core;
using Xunit;

public class RecordingTests
{
    [Fact] public void StartIsAtomicAndRepeatableOnlyAfterCompletion()
    {
        var state = new SessionState();
        var wins = 0;
        Parallel.For(0, 40, _ => { if (state.TryBegin()) Interlocked.Increment(ref wins); });
        Assert.Equal(1, wins);
        state.Move(RecordingState.Recording); state.Move(RecordingState.Stopping); state.Move(RecordingState.Finalizing);
        Assert.False(state.TryBegin());
        state.Move(RecordingState.Completed); Assert.True(state.TryBegin());
    }
    [Fact] public void CannotClaimCompletionWithoutFinalizing()
    {
        var state = new SessionState(); state.TryBegin();
        Assert.Throws<InvalidOperationException>(() => state.Move(RecordingState.Completed));
    }
    [Fact] public void FailedSessionCanRetry()
    {
        var state = new SessionState(); state.TryBegin(); state.Move(RecordingState.Failed);
        Assert.False(state.IsBusy); Assert.True(state.TryBegin());
    }
    [Theory]
    [InlineData(1279,720,30)] [InlineData(1280,719,30)] [InlineData(1280,720,0)] [InlineData(0,720,30)] [InlineData(1280,720,200)]
    public void RejectsInvalidEncodingSizes(int width,int height,int fps) => Assert.Throws<ArgumentException>(() => new RecordingSettings(width,height,fps).Validate());
    [Theory] [InlineData(15)] [InlineData(20)] [InlineData(24)] [InlineData(25)] [InlineData(30)] [InlineData(60)]
    public void AcceptsDocumentedFrameRates(int fps) => new RecordingSettings(Fps:fps).Validate();
    [Fact] public void ClockTracksRealTimeIncludingStaticFrames()
    {
        Assert.Equal(300, FrameClock.DueFrame(TimeSpan.FromSeconds(10),30));
        Assert.Equal(TimeSpan.FromSeconds(10),FrameClock.FrameTime(300,30));
        Assert.Equal(0,FrameClock.DueFrame(TimeSpan.FromSeconds(-1),30));
    }
}

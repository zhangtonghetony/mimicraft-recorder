using Mimicraft.Recorder.Core;
using Xunit;

public class EditingTests
{
    [Fact]public void PausedSessionRemainsBusyAndCanStop()
    {
        var s=new SessionState();s.TryBegin();s.Move(RecordingState.Recording);s.Move(RecordingState.Paused);Assert.True(s.IsBusy);Assert.False(s.TryBegin());s.Move(RecordingState.Recording);s.Move(RecordingState.Paused);s.Move(RecordingState.Stopping);s.Move(RecordingState.Finalizing);s.Move(RecordingState.Completed);Assert.False(s.IsBusy);
    }
    [Theory][InlineData(-1,0,100,100)][InlineData(0,0,15,100)][InlineData(99,0,100,100)]
    public void CropCannotExceedSource(int x,int y,int width,int height)=>Assert.Throws<ArgumentException>(()=>new PixelRegion(x,y,width,height).Validate(128,128));
    [Fact]public void TimelineIsNonDestructiveAndUndoable()
    {
        var t=new EditTimeline(10);t.Trim(0,1,9);t.Split(0,4);Assert.Equal(2,t.Ranges.Count);t.Delete(0);Assert.Equal(new ClipRange(4,9),t.Ranges[0]);t.Undo();Assert.Equal(2,t.Ranges.Count);t.Redo();Assert.Single(t.Ranges);t.Reset();Assert.Equal(new ClipRange(0,10),t.Ranges[0]);t.Undo();Assert.Equal(new ClipRange(4,9),t.Ranges[0]);
    }
    [Fact]public void TimelineRejectsEmptyInvalidAndNaNRanges()
    {
        var t=new EditTimeline(5);Assert.Throws<ArgumentException>(()=>t.Delete(0));Assert.Throws<ArgumentException>(()=>t.Trim(0,2,1));Assert.Throws<ArgumentException>(()=>t.Trim(0,double.NaN,4));Assert.Throws<ArgumentException>(()=>t.Split(0,5));Assert.Single(t.Ranges);
    }
    [Fact]public void NewEditClearsRedoHistory()
    {
        var t=new EditTimeline(10);t.Trim(0,1,9);t.Undo();t.Trim(0,2,8);Assert.False(t.CanRedo);
    }
    [Fact]public void ScheduleRunsOnceAndDoesNotRunEarly()
    {
        var now=DateTimeOffset.Now;var s=new SchedulePlan(now,TimeSpan.FromSeconds(2));Assert.False(s.Poll(now.AddSeconds(-1),false));Assert.True(s.Poll(now,false));Assert.False(s.Poll(now.AddSeconds(1),false));
    }
    [Theory][InlineData(true,0)][InlineData(false,61)]public void ScheduleDoesNotInterruptBusyOrResumeMissedSessions(bool busy,int late)
    {
        var now=DateTimeOffset.Now;var s=new SchedulePlan(now,null);Assert.False(s.Poll(now.AddSeconds(late),busy));Assert.True(s.Consumed);Assert.NotNull(s.MissReason);
    }
    [Fact]public void AlphaCompositeMatchesExpectedPixelAndPreservesUntouchedPixels()
    {
        byte[] frame=[0,0,0,255,10,20,30,255];FrameComposite.Apply(frame,2,1,new(2,1,[0,0,255,128,0,0,0,0]));Assert.Equal(new byte[]{0,0,128,255,10,20,30,255},frame);
    }
    [Fact]public void AnnotationMovementAndHistoryPreserveStyle()
    {
        var d=new AnnotationDocument();d.Add(new("矩形",[new(.1,.2),new(.3,.4)]));d.Move(0,.2,.1);Assert.Equal(.3,d.Items[0].Points[0].X,6);d.Remove(0);Assert.Empty(d.Items);d.Undo();Assert.Single(d.Items);d.Clear();d.Undo();d.Redo();Assert.Empty(d.Items);
    }
    [Theory][InlineData(0,double.NaN)][InlineData(500000,1)][InlineData(1000,3)]public void VideoControlsRejectUnsafeValues(int bitrate,double saturation)=>Assert.Throws<ArgumentException>(()=>new RecordingSettings(BitrateKbps:bitrate,Saturation:saturation).Validate());
}

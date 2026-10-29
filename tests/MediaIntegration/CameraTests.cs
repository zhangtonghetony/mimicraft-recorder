using Mimicraft.Recorder.Windows;
using Xunit;

public class CameraTests
{
    public sealed class CameraFactAttribute:FactAttribute{public CameraFactAttribute(){if(Environment.GetEnvironmentVariable("MIMICRAFT_CAMERA_TESTS")!="1")Skip="真实摄像头测试需明确启用。";}}
    [CameraFact]public async Task CameraProducesNonBlackFramesAndStopsItsOwnProcess()
    {
        var devices=await CameraCapture.DevicesAsync();Assert.NotEmpty(devices);await using var camera=new CameraCapture(devices[0]);await camera.FirstFrame.WaitAsync(TimeSpan.FromSeconds(15));await Task.Delay(700);var data=new byte[CameraCapture.Width*CameraCapture.Height*4];Assert.True(camera.CopyLatest(data));Assert.False(camera.NearlyBlack,"画面接近全黑，请打开物理开关。");Assert.True(camera.Frames>=3);Assert.True(data.Where((_,i)=>i%4!=3).Max()>40);
    }
}

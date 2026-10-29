using Mimicraft.Recorder.Core;

namespace Mimicraft.Recorder;

public sealed class RecorderOptions
{
    public AudioSettings Audio { get;set; }=new();
    [System.Text.Json.Serialization.JsonIgnore]public string[] Destinations { get;set; }=[];
    public List<SceneLayer> Layers { get;set; }=[];
    public List<ScenePreset> Presets { get;set; }=[];
    public CameraPlacement CameraPlacement { get;set; }=new();
    public bool FloatingCamera { get;set; }=true;
    public string Layout { get;set; }="overlay";
    public int BitrateKbps { get; set; }=4000;
    public double Saturation { get; set; }=1;
    public string Profile { get; set; }="main";
    public string Container { get; set; }="mp4";
    // A crop is a session choice. Old settings must not silently crop a new launch.
    [System.Text.Json.Serialization.JsonIgnore]public PixelRegion? Region { get; set; }
    public string WatermarkText { get; set; }="";
    public string WatermarkImage { get; set; }="";
    public double MarkX { get; set; }=.04;
    public double MarkY { get; set; }=.07;
    public double MarkSize { get; set; }=36;
    public double MarkOpacity { get; set; }=.85;
    public int CountdownSeconds { get; set; }
    public int StopAfterSeconds { get; set; }
    public string OutputDirectory { get; set; }="";
    public uint ShortcutModifiers { get; set; }=3;
    public uint StartKey { get; set; }=0x75; // Ctrl+Alt+F6
    public uint PauseKey { get; set; }=0x76;
    public uint StopKey { get; set; }=0x77;
    public void Validate()
    {
        Audio.Validate();foreach(var destination in Destinations)StreamAddress.Validate(destination);
        if(Layers.Count>8||Layout is not ("overlay" or "split"))throw new ArgumentException("画面布局无效或图层超过8个。");foreach(var layer in Layers)layer.Validate();
        if(Presets.Count>20)throw new ArgumentException("最多保存20个本地场景预设。");foreach(var preset in Presets)preset.Validate();CameraPlacement.Validate();
        new RecordingSettings(Region:Region,BitrateKbps:BitrateKbps,Saturation:Saturation,Profile:Profile,Container:Container).Validate();
        if(!double.IsFinite(MarkX+MarkY+MarkSize+MarkOpacity)||MarkX is <0 or >.95||MarkY is <0 or >.95||MarkSize is <8 or >160||MarkOpacity is <0 or >1||CountdownSeconds is <0 or >60||StopAfterSeconds is <0 or >86400)
            throw new ArgumentException("画面标记或时间设置无效。");
        if(WatermarkText.Length>2000||WatermarkImage.Length>32760||OutputDirectory.Length>32760)throw new ArgumentException("设置内容过长。");
        if(ShortcutModifiers!=3||StartKey is not (0x75 or 0x78)||PauseKey is not (0x76 or 0x79)||StopKey is not (0x77 or 0x7a))throw new ArgumentException("快捷键设置无效。");
    }
}

public sealed record CameraPlacement(double Left=80,double Top=80,double Width=380,double Height=250)
{
    public void Validate(){if(!double.IsFinite(Left+Top+Width+Height)||Width is <120 or >3840||Height is <90 or >2160)throw new ArgumentException("摄像头浮窗位置尺寸无效。");}
}

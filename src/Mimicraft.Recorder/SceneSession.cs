using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class SceneSession:IAsyncDisposable
{
    private sealed record Source(SceneLayer Layer,Func<byte[]?> Snapshot);
    private readonly List<Source> _sources=[];private readonly List<ScreenCapture> _screens=[];private CameraCapture? _camera;
    private readonly List<Window> _previews=[];private readonly List<DispatcherTimer> _timers=[];
    public bool Split { get;set; }
    public CameraPlacement Placement { get;set; }=new();
    public Action<CameraPlacement>? PlacementSaved { get;set; }
    private byte[]? _base,_regionFrame;
    public string Warning { get;private set; }="";
    public async Task StartAsync(IReadOnlyList<SceneLayer> layers,bool floating)
    {
        try
        {
            foreach(var layer in layers.Where(l=>l.Visible))
            {
                layer.Validate();
                if(layer.Kind=="camera")
                {
                    if(_camera is not null)throw new ArgumentException("一个场景只开启一个摄像头源，可重复使用图片等静态层。");
                    _camera=new(layer.Source);await _camera.FirstFrame.WaitAsync(TimeSpan.FromSeconds(12));
                    var camera=_camera;var bytes=new byte[CameraCapture.Width*CameraCapture.Height*4];
                    _sources.Add(new(layer,()=>camera.CopyLatest(bytes)?bytes:null));
                    if(camera.NearlyBlack)Warning="摄像头画面接近全黑，请检查物理开关或遮挡。";
                    if(floating)PreviewCamera(camera);
                }
                else if(layer.Kind=="screen")
                {
                    var source=ScreenCapture.GetMonitors().Concat(DesktopSources.GetWindows()).FirstOrDefault(s=>s.Name==layer.Source)??throw new IOException("第二画面来源已不存在，请重新选择。");
                    var screen=new ScreenCapture();_screens.Add(screen);screen.Start(source);await screen.FirstFrame.WaitAsync(TimeSpan.FromSeconds(12));var bytes=new byte[screen.Width*screen.Height*4];_sources.Add(new(layer,()=>screen.CopyLatest(bytes)?bytes:null));
                }
                else
                {
                    var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
                    {
                        if(layer.Kind=="image"){var img=new BitmapImage();img.BeginInit();img.CacheOption=BitmapCacheOption.OnLoad;img.DecodePixelWidth=640;img.UriSource=new Uri(layer.Source);img.EndInit();dc.DrawImage(img,new Rect(0,0,640,360));}
                        else dc.DrawText(new FormattedText(layer.Source,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),40,Brushes.White,1),new(10,10));
                    }
                    var bmp=new RenderTargetBitmap(640,360,96,96,PixelFormats.Pbgra32);bmp.Render(visual);var pixels=new byte[640*360*4];bmp.CopyPixels(pixels,640*4,0);for(int p=0;p<pixels.Length;p+=4){int a=pixels[p+3];if(a is >0 and <255)for(int c=0;c<3;c++)pixels[p+c]=(byte)Math.Min(255,pixels[p+c]*255/a);}_sources.Add(new(layer,()=>pixels));
                }
            }
        }
        catch{await DisposeAsync();throw;}
    }
    public OverlayBitmap? CameraSnapshot()
    {
        if(_camera is null)return null;var bytes=new byte[CameraCapture.Width*CameraCapture.Height*4];return _camera.CopyLatest(bytes)?new(CameraCapture.Width,CameraCapture.Height,bytes):null;
    }
    public void Compose(byte[] frame,int width,int height,PixelRegion? region=null)
    {
        if(_sources.Count==0&&!Split)return;
        if(region is null){ComposeScene(frame,width,height);return;}
        region.Validate(width,height);
        // Compose within the eventual recording area, so camera PiP is not cropped away.
        int length=checked(region.Width*region.Height*4);if(_regionFrame?.Length!=length)_regionFrame=new byte[length];
        for(int y=0;y<region.Height;y++)Buffer.BlockCopy(frame,((region.Y+y)*width+region.X)*4,_regionFrame,y*region.Width*4,region.Width*4);
        ComposeScene(_regionFrame,region.Width,region.Height);
        for(int y=0;y<region.Height;y++)Buffer.BlockCopy(_regionFrame,y*region.Width*4,frame,((region.Y+y)*width+region.X)*4,region.Width*4);
    }
    private void ComposeScene(byte[] frame,int width,int height)
    {
        if(Split){if(_base?.Length!=frame.Length)_base=new byte[frame.Length];Buffer.BlockCopy(frame,0,_base,0,frame.Length);Array.Clear(frame);PixelBlit.Draw(frame,width,height,_base,width,height,new("screen","base",0,0,.5,1));}
        foreach(var source in _sources)
        {
            var data=source.Snapshot();if(data is null)continue;
            int sw=640,sh=360;
            if(source.Layer.Kind=="screen"){var index=_sources.Where(s=>s.Layer.Kind=="screen").ToList().IndexOf(source);sw=_screens[index].Width;sh=_screens[index].Height;}
            PixelBlit.Draw(frame,width,height,data,sw,sh,source.Layer);
        }
    }
    private void PreviewCamera(CameraCapture camera)
    {
        var image=new Image{Stretch=Stretch.Uniform};var window=new Window{Title="Mimicraft · 摄像头浮窗",Content=image,Width=Placement.Width,Height=Placement.Height,Left=Math.Clamp(Placement.Left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Placement.Width)),Top=Math.Clamp(Placement.Top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-Placement.Height)),Topmost=true};_previews.Add(window);
        window.SourceInitialized+=(_,_)=>DesktopSources.Exclude(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};_timers.Add(timer);var data=new byte[CameraCapture.Width*CameraCapture.Height*4];
        timer.Tick+=(_,_)=>{try{if(camera.CopyLatest(data)){var bmp=BitmapSource.Create(CameraCapture.Width,CameraCapture.Height,96,96,PixelFormats.Bgra32,null,data,CameraCapture.Width*4);bmp.Freeze();image.Source=bmp;}}catch(Exception e){timer.Stop();App.Log.Write("CAMERA_PREVIEW_FAILED",error:e);window.Title="摄像头已停止";}};
        window.Closed+=(_,_)=>{timer.Stop();try{PlacementSaved?.Invoke(new(window.Left,window.Top,window.Width,window.Height));}catch(Exception e){App.Log.Write("CAMERA_PLACEMENT_SAVE_FAILED",error:e);}};window.Show();timer.Start();
    }
    public async ValueTask DisposeAsync(){foreach(var timer in _timers)timer.Stop();foreach(var window in _previews.ToArray())window.Close();_timers.Clear();_previews.Clear();foreach(var screen in _screens)screen.Dispose();_screens.Clear();_sources.Clear();if(_camera is {} c){_camera=null;await c.DisposeAsync();}}
}

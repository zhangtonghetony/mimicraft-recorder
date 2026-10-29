using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Mimicraft.Recorder.Core;

namespace Mimicraft.Recorder;

public sealed class OptionsWindow:Window
{
    public OptionsWindow(MainViewModel vm)
    {
        Title="Mimicraft · 画面与保存";Width=650;Height=740;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(24)};Content=root;
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var o=vm.Options;
        panel.Children.Add(new TextBlock{Text="画面与保存",FontSize=25,FontWeight=FontWeights.Bold});
        var bitrate=UiForms.Select(panel,"视频码率 / kbps",["1000","2000","4000","8000","16000"],Array.IndexOf(new[]{1000,2000,4000,8000,16000},o.BitrateKbps));
        var saturation=UiForms.Field(panel,"色彩饱和度（0=灰度，1=原色，2=增强）",o.Saturation.ToString(CultureInfo.InvariantCulture));
        var profile=UiForms.Select(panel,"H.264编码规格",["baseline","main","high"],Array.IndexOf(new[]{"baseline","main","high"},o.Profile));
        var container=UiForms.Select(panel,"视频封装",["mp4","mkv"],o.Container=="mkv"?1:0);
        var directory=UiForms.Field(panel,"保存位置",vm.OutputDirectory);
        UiForms.Button(panel,"选择文件夹",()=>{var dialog=new OpenFolderDialog();if(dialog.ShowDialog(this)==true)directory.Text=dialog.FolderName;});
        var text=UiForms.Field(panel,"文字画面标记（留空关闭）",o.WatermarkText,"WatermarkText");
        var image=UiForms.Field(panel,"图片画面标记（PNG/JPG/JPEG；留空关闭）",o.WatermarkImage);
        UiForms.Button(panel,"选择图片",()=>{var d=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp"};if(d.ShowDialog(this)==true)image.Text=d.FileName;});
        var x=UiForms.Field(panel,"水平位置（0～0.95）",o.MarkX.ToString(CultureInfo.InvariantCulture));
        var y=UiForms.Field(panel,"垂直位置（0～0.95）",o.MarkY.ToString(CultureInfo.InvariantCulture));
        var size=UiForms.Field(panel,"文字字号 / 图片宽度=字号×5",o.MarkSize.ToString(CultureInfo.InvariantCulture));
        var opacity=UiForms.Field(panel,"透明度（0～1）",o.MarkOpacity.ToString(CultureInfo.InvariantCulture));
        var startKey=UiForms.Select(panel,"Ctrl+Alt：开始快捷键",["F6","F9"],o.StartKey==0x78?1:0);
        var pauseKey=UiForms.Select(panel,"Ctrl+Alt：暂停快捷键",["F7","F10"],o.PauseKey==0x79?1:0);
        var stopKey=UiForms.Select(panel,"Ctrl+Alt：停止快捷键",["F8","F11"],o.StopKey==0x7a?1:0);
        UiForms.Button(actions,"取消",Close);
        UiForms.Button(actions,"保存设置",()=>
        {
            try
            {
                var candidate=new RecordingSettings(BitrateKbps:int.Parse((string)bitrate.SelectedItem),Saturation:double.Parse(saturation.Text,CultureInfo.InvariantCulture),Profile:(string)profile.SelectedItem,Container:(string)container.SelectedItem);candidate.Validate();
                double mx=double.Parse(x.Text,CultureInfo.InvariantCulture),my=double.Parse(y.Text,CultureInfo.InvariantCulture),ms=double.Parse(size.Text,CultureInfo.InvariantCulture),mo=double.Parse(opacity.Text,CultureInfo.InvariantCulture);
                if(!double.IsFinite(mx+my+ms+mo)||mx is <0 or >.95||my is <0 or >.95||ms is <8 or >160||mo is <0 or >1)throw new ArgumentException("标记位置、字号或透明度无效。");
                if(image.Text!=""&&!System.IO.File.Exists(image.Text))throw new ArgumentException("图片不存在。");
                string dir=System.IO.Path.GetFullPath(directory.Text);System.IO.Directory.CreateDirectory(dir);
                o.BitrateKbps=candidate.BitrateKbps;o.Saturation=candidate.Saturation;o.Profile=candidate.Profile;o.Container=candidate.Container;o.OutputDirectory=dir;
                o.WatermarkText=text.Text;o.WatermarkImage=image.Text;o.MarkX=mx;o.MarkY=my;o.MarkSize=ms;o.MarkOpacity=mo;
                o.StartKey=startKey.SelectedIndex==0?0x75u:0x78u;o.PauseKey=pauseKey.SelectedIndex==0?0x76u:0x79u;o.StopKey=stopKey.SelectedIndex==0?0x77u:0x7au;
                vm.ApplyOptions();DialogResult=true;
            }
            catch(Exception e){UiForms.Error(this,e);}
        },"SaveOptions");
    }
}

public sealed class ScheduleWindow:Window
{
    public ScheduleWindow(MainViewModel vm)
    {
        Title="Mimicraft · 时间安排";Width=460;Height=470;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new(24)};Content=panel;
        panel.Children.Add(new TextBlock{Text="时间安排",FontSize=24,FontWeight=FontWeights.Bold});
        var countdown=UiForms.Field(panel,"手动开始前倒数 / 秒（0～60）",vm.Options.CountdownSeconds.ToString(),"CountdownSeconds");
        var stop=UiForms.Field(panel,"录制自动停止 / 秒（0=不限）",vm.Options.StopAfterSeconds.ToString(),"StopAfterSeconds");
        var date=UiForms.Field(panel,"一次性计划开始（留空不预约，格式 yyyy-MM-dd HH:mm:ss）","","ScheduledStart");
        panel.Children.Add(new TextBlock{Text="计划不抢占已有录制。睡眠错过超过60秒不会补录。关闭应用会取消预约；可随时点击主界面的“取消等待”。",TextWrapping=TextWrapping.Wrap,Margin=new(0,14,0,4)});
        UiForms.Button(panel,"应用时间安排",()=>
        {
            try
            {
                int a=int.Parse(countdown.Text),b=int.Parse(stop.Text);
                if(a is <0 or >60||b is <0 or >86400)throw new ArgumentException("倒数0～60秒，录制时限0～86400秒。");
                DateTimeOffset? start=string.IsNullOrWhiteSpace(date.Text)?null:DateTimeOffset.ParseExact(date.Text,"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);
                if(start<DateTimeOffset.Now)throw new ArgumentException("请安排未来时间。");
                vm.Options.CountdownSeconds=a;vm.Options.StopAfterSeconds=b;vm.SetSchedule(start);vm.ApplyOptions();DialogResult=true;
            }
            catch(Exception e){UiForms.Error(this,e);}
        },"ApplySchedule");
    }
}

public sealed class RegionPicker:Window
{
    private readonly System.Windows.Controls.Canvas _canvas=new(){Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(55,0,0,0))};
    private readonly System.Windows.Shapes.Rectangle _rect=new(){Stroke=System.Windows.Media.Brushes.Orange,StrokeThickness=3,Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(35,255,255,255))};
    private Point _start;private bool _dragging;
    public PixelRegion? Region { get;private set; }
    public RegionPicker(PixelRegion bounds,System.Windows.Media.Imaging.BitmapSource? snapshot=null)
    {
        Title="Mimicraft · 框选画面";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=System.Windows.Media.Brushes.Transparent;Topmost=true;ShowInTaskbar=false;Content=_canvas;Cursor=System.Windows.Input.Cursors.Cross;
        if(snapshot is not null){Title="Mimicraft · 框选截图";_canvas.Background=new System.Windows.Media.ImageBrush(snapshot){Stretch=System.Windows.Media.Stretch.Fill};}
        _canvas.Children.Add(new TextBlock{Text=snapshot is null?"拖动框选录制范围 · Esc取消":"拖动框选截图区域 · 松开完成 · Esc取消",IsHitTestVisible=false,Foreground=System.Windows.Media.Brushes.White,Background=System.Windows.Media.Brushes.Black,FontSize=24,Margin=new(30)});_canvas.Children.Add(_rect);
        SourceInitialized+=(_,_)=>{var hwnd=new System.Windows.Interop.WindowInteropHelper(this).Handle;Mimicraft.Recorder.Windows.DesktopSources.Exclude(hwnd);SetWindowPos(hwnd,new(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x40);};
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)Close();};
        _canvas.MouseLeftButtonDown+=(_,e)=>{_start=e.GetPosition(_canvas);_dragging=true;_canvas.CaptureMouse();};
        _canvas.MouseMove+=(_,e)=>{if(!_dragging)return;var r=new Rect(_start,e.GetPosition(_canvas));Canvas.SetLeft(_rect,r.X);Canvas.SetTop(_rect,r.Y);_rect.Width=r.Width;_rect.Height=r.Height;};
        _canvas.MouseLeftButtonUp+=(_,e)=>
        {
            if(!_dragging)return;_dragging=false;_canvas.ReleaseMouseCapture();var r=new Rect(_start,e.GetPosition(_canvas));
            var scaleX=bounds.Width/_canvas.ActualWidth;var scaleY=bounds.Height/_canvas.ActualHeight;
            int x=Math.Clamp((int)(r.X*scaleX),0,bounds.Width),y=Math.Clamp((int)(r.Y*scaleY),0,bounds.Height);
            var region=new PixelRegion(x,y,Math.Min((int)(r.Width*scaleX),bounds.Width-x),Math.Min((int)(r.Height*scaleY),bounds.Height-y));
            try{region.Validate(bounds.Width,bounds.Height);Region=region;DialogResult=true;}catch(Exception error){UiForms.Error(this,error);}
        };
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
}

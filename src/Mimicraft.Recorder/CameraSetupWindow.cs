using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class CameraSetupWindow:Window
{
    private bool _working;
    public CameraSetupWindow(MainViewModel vm,Func<Task> preview,Func<Task> stop,Func<OverlayBitmap?> snapshot)
    {
        Title="Mimicraft · 摄像头画中画";Width=580;Height=730;MaxHeight=Math.Max(480,SystemParameters.WorkArea.Height-30);WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var dock=new DockPanel{Margin=new(24)};Content=dock;
        var footer=new WrapPanel{Margin=new(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        var panel=new StackPanel();dock.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(new TextBlock{Text="把摄像头加入录屏",FontSize=25,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock{Text="① 选择设备、位置和大小\n② 点击“加入录制并预览”确认画面\n③ 返回主界面，点击“开始录制”",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,14),LineHeight=24});
        var existing=vm.Options.Layers.FirstOrDefault(l=>l.Kind=="camera");
        panel.Children.Add(new TextBlock{Text="摄像头设备"});var devices=new ComboBox{MinHeight=36};System.Windows.Automation.AutomationProperties.SetAutomationId(devices,"CameraDevice");panel.Children.Add(devices);
        var settingsRow=new Grid();settingsRow.ColumnDefinitions.Add(new());settingsRow.ColumnDefinitions.Add(new());panel.Children.Add(settingsRow);
        var positionPanel=new StackPanel{Margin=new(0,0,12,0)};var sizePanel=new StackPanel();settingsRow.Children.Add(positionPanel);Grid.SetColumn(sizePanel,1);settingsRow.Children.Add(sizePanel);
        var position=UiForms.Select(positionPanel,"成片中的位置",["右下角","右上角","左下角","左上角"],existing is null?0:(existing.X<.5?2:0)+(existing.Y<.5?1:0));System.Windows.Automation.AutomationProperties.SetAutomationId(position,"CameraCorner");
        double[] sizes=[.2,.25,.35];var size=UiForms.Select(sizePanel,"成片中的大小",["小","中","大"],existing is null?1:Array.IndexOf(sizes,sizes.MinBy(s=>Math.Abs(s-existing.Width))));System.Windows.Automation.AutomationProperties.SetAutomationId(size,"CameraSize");
        var floating=new CheckBox{Content="录制时保留摄像头监看小窗",IsChecked=vm.Options.FloatingCamera,Margin=new(0,0,0,10)};System.Windows.Automation.AutomationProperties.SetAutomationId(floating,"CameraMonitor");panel.Children.Add(floating);
        panel.Children.Add(new TextBlock{Text="成片位置由上方选项决定；移动监看小窗不会改变成片位置。选区录制时，摄像头也会放在选区画面内。",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=Brushes.DimGray,Margin=new(0,0,0,12)});
        var image=new Image{Height=160,Stretch=Stretch.Uniform};System.Windows.Automation.AutomationProperties.SetAutomationId(image,"CameraLivePreview");panel.Children.Add(new Border{Background=new SolidColorBrush(Color.FromRgb(32,63,53)),Child=image,CornerRadius=new(8)});
        var status=new TextBlock{Text="正在查找摄像头；不会自动打开设备。",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,8)};System.Windows.Automation.AutomationProperties.SetAutomationId(status,"CameraSetupStatus");panel.Children.Add(status);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
        timer.Tick+=(_,_)=>{try{if(snapshot() is {} frame){var bitmap=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Bgra32,null,frame.Pixels,frame.Width*4);bitmap.Freeze();image.Source=bitmap;}}catch(Exception e){timer.Stop();status.Text="摄像头预览已停止："+e.Message;}};
        Button? enable=null,disable=null;
        void Busy(bool value){_working=value;if(enable is not null)enable.IsEnabled=!value&&devices.SelectedItem is string;if(disable is not null)disable.IsEnabled=!value;}
        enable=UiForms.Button(footer,"加入录制并预览",async()=>
        {
            if(_working)return;
            try
            {
                if(devices.SelectedItem is not string name)throw new InvalidOperationException("没有可用摄像头，请连接设备后点击刷新。");
                if(vm.Options.Layers.Count(l=>l.Kind!="camera")>=8)throw new InvalidOperationException("高级布局已满8层，请先移除一个图层。");
                Busy(true);timer.Stop();status.Text="正在打开摄像头，请稍候…";
                double fraction=sizes[size.SelectedIndex];bool left=position.SelectedIndex>=2,top=position.SelectedIndex%2==1;
                vm.Options.Layers.RemoveAll(l=>l.Kind=="camera");vm.Options.Layers.Add(new("camera",name,left ? .03 : 1-fraction-.03,top ? .03 : 1-fraction-.03,fraction,fraction));vm.Options.FloatingCamera=floating.IsChecked==true;
                vm.Options.Audio=vm.Options.Audio with {AudioOnly=false};vm.ApplyOptions();await preview();
                status.Text=string.IsNullOrWhiteSpace(vm.SourceWarning)?"已加入录制。确认预览后点“返回录制”，再在主界面开始。关闭此设置窗口不会关闭已启用的摄像头。":vm.SourceWarning;
                vm.SetStatus(string.IsNullOrWhiteSpace(vm.SourceWarning)?"摄像头已加入录制；开始后会显示在所选角落。":vm.SourceWarning);timer.Start();
            }
            catch(Exception e){status.Text="摄像头未能开启。请检查物理开关、设备占用或权限后重试。\n"+e.Message;App.Log.Write("CAMERA_SETUP_FAILED",error:e);}
            finally{Busy(false);}
        },"EnableCamera");enable.IsEnabled=false;
        disable=UiForms.Button(footer,"关闭摄像头",async()=>{if(_working)return;try{Busy(true);timer.Stop();vm.Options.Layers.RemoveAll(l=>l.Kind=="camera");vm.ApplyOptions();await stop();image.Source=null;status.Text="摄像头已关闭，下次录制不含摄像头。";vm.SetStatus(status.Text);}catch(Exception e){status.Text=e.Message;}finally{Busy(false);}},"DisableCamera");
        UiForms.Button(footer,"返回录制",Close,"FinishCameraSetup");
        UiForms.Button(panel,"刷新设备",async()=>await Refresh(),"RefreshCameras");
        async Task Refresh(){try{enable.IsEnabled=false;var names=await CameraCapture.DevicesAsync();devices.ItemsSource=names;devices.SelectedItem=names.FirstOrDefault(n=>n==existing?.Source)??names.FirstOrDefault();enable.IsEnabled=devices.SelectedItem is string;status.Text=names.Length==0?"未发现摄像头。请连接设备后刷新；笔记本请检查物理开关。":"设备已找到。选择后点击“加入录制并预览”；当前尚未自动打开设备。";}catch(Exception e){status.Text="查找设备失败："+e.Message;}}
        Loaded+=async(_,_)=>await Refresh();Closing+=(_,e)=>{if(_working){e.Cancel=true;status.Text="正在处理摄像头，请稍候再关闭。";}};Closed+=(_,_)=>timer.Stop();
    }
}

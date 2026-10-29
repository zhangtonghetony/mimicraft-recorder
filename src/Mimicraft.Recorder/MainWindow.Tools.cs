using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public partial class MainWindow
{
    private Window? _controls;
    private AnnotationWindow? _annotations;
    private EditWindow? _editor;
    private HwndSource? _hotkeySource;
    private readonly List<int> _registered=[];
    private string _annotationTool="穿透";
    private SceneSession? _scene;
    private RecordingEffects? _effects;
    private TrayControl? _tray;
    private TextBlock? _clockView;
    private Action<string>? _selectInkTool;
    private bool _capturingStamp;
    private async Task StartSceneAsync()
    {
        await StopSceneAsync();_scene=new(){Split=_vm.Options.Layout=="split",Placement=_vm.Options.CameraPlacement,PlacementSaved=position=>{_vm.Options.CameraPlacement=position;_vm.ApplyOptions();}};
        await _scene.StartAsync(_vm.Options.Layers,_vm.Options.FloatingCamera);_vm.FrameTransform=(frame,w,h)=>{_scene?.Compose(frame,w,h,_vm.Options.Region);_effects?.Compose(frame,w,h);};
        _vm.SourceWarning=_scene.Warning;if(_scene.Warning!="")_vm.SetStatus(_scene.Warning);
    }
    private async Task StopSceneAsync(){_vm.FrameTransform=null;_vm.SourceWarning="";if(_scene is {} scene){_scene=null;await scene.DisposeAsync();}}
    private void InitializeTools()
    {
        _effects=new(_vm);_tray=new(this,_vm);
        var effectsTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};effectsTimer.Tick+=(_,_)=>{_effects.Tick();if(_clockView is not null)_clockView.Text=_effects.ClockLabel;};effectsTimer.Start();
        Closed+=(_,_)=>{effectsTimer.Stop();_effects.Dispose();_tray.Dispose();};
        UiForms.Button(SideTools,"收至托盘",()=>_tray.Hide(),"HideToTray");
        UiForms.Button(SideTools,"音画测试信号（低音量）",()=>{if(_practice?.IsLoaded==true)_practice.PlayAvMarker();else _vm.SetStatus("先打开练习画面，再触发0.5秒低音量音画校验信号。");},"AvMarkerButton");
        UiForms.Button(SideTools,"声音与音频",()=>{if(_vm.CanConfigure)new AudioWindow(_vm){Owner=this}.ShowDialog();},"AudioSettings");
        UiForms.Button(SideTools,"摄像头画中画",ShowCameraSettings,"CameraSettings");
        UiForms.Button(SideTools,"高级画面布局",()=>{if(_vm.CanConfigure)new SceneWindow(_vm,StartSceneAsync){Owner=this}.ShowDialog();},"SceneSettings");
        UiForms.Button(SideTools,"关闭设备预览",async()=>{if(_vm.CanConfigure)await StopSceneAsync();},"StopScenePreview");
        _vm.PrepareSources=StartSceneAsync;_vm.ReleaseSources=StopSceneAsync;
        UiForms.Button(SideTools,"网络工作台",()=>{if(_vm.CanConfigure)new NetworkWindow(_vm){Owner=this}.Show();},"NetworkSettings");
        UiForms.Button(SideTools,"诊断与恢复",()=>{if(_vm.CanConfigure)new RecoveryWindow(_vm){Owner=this}.Show();},"RecoverySettings");
        Closed+=async(_,_)=>await StopSceneAsync();
        UiForms.Button(SideTools,"画面与保存",()=>{if(!_vm.CanConfigure)return;var dialog=new OptionsWindow(_vm){Owner=this};if(dialog.ShowDialog()==true){RegisterShortcuts();UpdateComposite();}},"OptionsButton");
        UiForms.Button(SideTools,"窗口 / 显示器",()=>{if(_vm.CanConfigure)_vm.RefreshSources();},"RefreshSources");
        UiForms.Button(SideTools,"框选录制范围",ChooseRegion,"RegionButton");
        UiForms.Button(SideTools,"精确输入范围",()=>{if(_vm.CanConfigure)new RegionFieldsWindow(_vm){Owner=this}.ShowDialog();},"RegionFields");
        UiForms.Button(SideTools,"恢复完整画面",_vm.ResetCaptureRegion,"ClearRegion");
        UiForms.Button(SideTools,"快捷控制与批注",ShowControls,"FloatingControls");
        UiForms.Button(SideTools,"时间安排",()=>{if(_vm.CanConfigure)new ScheduleWindow(_vm){Owner=this}.ShowDialog();},"ScheduleButton");
        UiForms.Button(SideTools,"片段工坊",()=>{if(!_vm.CanConfigure)return;if(_editor is null){_editor=new(_vm,_vm.SelectedEntry?.Path){Owner=this};_editor.Closed+=(_,_)=>_editor=null;_editor.Show();}else _editor.Activate();},"EditorButton");
        UiForms.Button(SideTools,"批量画面标记",()=>{if(_vm.CanConfigure)new BatchWindow(_vm){Owner=this}.Show();},"BatchButton");
        UiForms.Button(SideTools,"文件改名",RenameSelected,"RenameFile");
        UiForms.Button(SideTools,"移至回收站",DeleteSelected,"DeleteFile");
        UiForms.Button(SideTools,"定位选中文件",()=>{if(_vm.SelectedEntry is {} entry)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe","/select,\""+entry.Path+"\""){UseShellExecute=true});});
        UiForms.Button(SideTools,"打开诊断日志",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",System.IO.Path.Combine(_vm.DataDirectory,"Logs")){UseShellExecute=true}));
        _vm.OverlayFactory=(w,h)=>Application.Current.Dispatcher.CheckAccess()?RenderComposite(w,h):Application.Current.Dispatcher.Invoke(()=>RenderComposite(w,h));
        _vm.Annotations.Changed+=UpdateComposite;
        SourceInitialized+=(_,_)=>{_hotkeySource=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);_hotkeySource?.AddHook(HotkeyHook);RegisterShortcuts();};
        Closed+=(_,_)=>{_annotations?.Close();_controls?.Close();foreach(var id in _registered)UnregisterHotKey(new WindowInteropHelper(this).Handle,id);};
    }
    private void ShowCameraSettings(){if(_vm.CanConfigure)new CameraSetupWindow(_vm,StartSceneAsync,StopSceneAsync,()=>_scene?.CameraSnapshot()){Owner=this}.ShowDialog();}
    private void CameraSettings_Click(object sender,RoutedEventArgs e)=>ShowCameraSettings();
    private void ResetRegion_Click(object sender,RoutedEventArgs e){try{_vm.ResetCaptureRegion();}catch(Exception error){UiForms.Error(this,error);}}
    private void ChooseRegion()
    {
        if(!_vm.CanConfigure||_vm.SelectedSource is not {} source)return;
        try
        {
            if(source.IsWindow){_vm.SetStatus("窗口来源直接捕获整个窗口；框选请先选择显示器。");return;}
            var picker=new RegionPicker(DesktopSources.Bounds(source)){Owner=this};if(picker.ShowDialog()==true){_vm.Options.Region=picker.Region;_vm.ApplyOptions();_vm.SetStatus($"已选择区域：{picker.Region}");}
        }
        catch(Exception e){UiForms.Error(this,e);}
    }
    private void UpdateComposite()
    {
        App.Log.Write("ANNOTATION_CHANGED",new{count=_vm.Annotations.Items.Count});
        var size=_vm.CaptureSize;if(size.Width>0&&size.Height>0)_vm.SetOverlay(RenderComposite(size.Width,size.Height));
    }
    private OverlayBitmap? RenderComposite(int w,int h)=>_vm.Annotations.Items.Count==0&&string.IsNullOrWhiteSpace(_vm.Options.WatermarkText)&&string.IsNullOrWhiteSpace(_vm.Options.WatermarkImage)?null:AnnotationWindow.Render(_vm.Annotations,_vm.Options,w,h);
    private void ShowControls()
    {
        if(_controls is not null){_controls.Show();RaiseControls();return;}
        var panel=new StackPanel{Margin=new(12)};
        _controls=new Window{Title="Mimicraft · 快捷控制",Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},DataContext=_vm,Width=790,MaxHeight=Math.Max(420,SystemParameters.WorkArea.Height-30),SizeToContent=SizeToContent.Height,Topmost=true,ResizeMode=ResizeMode.CanMinimize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,ShowInTaskbar=false};
        var state=new TextBlock{Margin=new(0,0,0,6)};state.SetBinding(TextBlock.TextProperty,"StateLabel");panel.Children.Add(state);
        var row=new WrapPanel();panel.Children.Add(row);
        void CommandButton(string title,System.Windows.Input.ICommand command,string id){var b=new Button{Content=title,Command=command,Margin=new(0,8,6,0)};System.Windows.Automation.AutomationProperties.SetAutomationId(b,id);row.Children.Add(b);}
        CommandButton("开始",_vm.StartCommand,"FloatStart");CommandButton("暂停 / 继续",_vm.PauseCommand,"FloatPause");CommandButton("结束并保存",_vm.StopCommand,"FloatStop");
        UiForms.Button(row,"显示批注画布",ShowAnnotations,"ShowAnnotations");UiForms.Button(row,"收起",()=>_controls?.Hide());
        UiForms.Button(row,"移动区域",()=>{if(_vm.Session is RecordingState.Recording or RecordingState.Paused&&_vm.Options.Region is {} region&&_vm.SelectedSource is {} source&&!source.IsWindow)new RegionMoveWindow(_vm,source,region){Owner=this}.Show();},"MoveRegion");
        UiForms.Button(row,"横 / 竖工具条",()=>{if(_controls is not null)_controls.Width=_controls.Width>500?340:790;},"ToggleToolbar");
        UiForms.Button(row,"截图贴图",async()=>await CaptureStampAsync(),"CaptureStamp");
        var extra=new WrapPanel();panel.Children.Add(extra);
        panel.Children.Add(new TextBlock{Text="以下效果进入录制输出与预览。组合键显示不记录普通文字输入。",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,4,0,4)});
        CheckBox spot=new(){Content="聚光",IsChecked=_effects!.Spotlight,Margin=new(0,10,16,8)},magnifier=new(){Content="放大镜",IsChecked=_effects.Magnifier,Margin=new(0,10,16,8)},clicks=new(){Content="左右键标记",IsChecked=_effects.MouseClicks,Margin=new(0,10,16,8)},keys=new(){Content="显示组合键",IsChecked=_effects.Keys,Margin=new(0,10,16,8)};
        System.Windows.Automation.AutomationProperties.SetAutomationId(spot,"SpotlightEffect");System.Windows.Automation.AutomationProperties.SetAutomationId(magnifier,"MagnifierEffect");System.Windows.Automation.AutomationProperties.SetAutomationId(clicks,"MouseEffect");System.Windows.Automation.AutomationProperties.SetAutomationId(keys,"KeyboardEffect");
        extra.Children.Add(spot);extra.Children.Add(magnifier);extra.Children.Add(clicks);extra.Children.Add(keys);
        spot.Checked+=(_,_)=>_effects!.Spotlight=true;spot.Unchecked+=(_,_)=>_effects!.Spotlight=false;magnifier.Checked+=(_,_)=>_effects!.Magnifier=true;magnifier.Unchecked+=(_,_)=>_effects!.Magnifier=false;
        void InputChanged(){try{_effects!.SetInput(clicks.IsChecked==true,keys.IsChecked==true);}catch(Exception e){UiForms.Error(this,e);}}
        clicks.Checked+=(_,_)=>InputChanged();clicks.Unchecked+=(_,_)=>InputChanged();keys.Checked+=(_,_)=>InputChanged();keys.Unchecked+=(_,_)=>InputChanged();
        UiForms.Button(extra,"关闭全部效果",()=>{spot.IsChecked=false;magnifier.IsChecked=false;clicks.IsChecked=false;keys.IsChecked=false;_effects!.CountdownEnd=null;_effects.StopwatchStart=null;_effects.Tick();_vm.SetStatus("聚光、放大镜、输入标记和时钟均已关闭。");},"ResetEffects");
        var clocks=new WrapPanel();panel.Children.Add(clocks);var seconds=new TextBox{Text="60",Width=65,Padding=new(5)};clocks.Children.Add(seconds);
        _clockView=new TextBlock{Margin=new(0,8,0,8)};panel.Children.Add(_clockView);
        UiForms.Button(clocks,"倒计时",()=>{if(int.TryParse(seconds.Text,out int n)&&n is >0 and <=86400){_effects!.CountdownEnd=DateTimeOffset.Now.AddSeconds(n);_effects.StopwatchStart=null;_effects.Tick();}},"EffectCountdown");
        UiForms.Button(clocks,"计时",()=>{_effects!.CountdownEnd=null;_effects.StopwatchStart=DateTimeOffset.Now;_effects.Tick();},"EffectStopwatch");UiForms.Button(clocks,"关闭时钟",()=>{_effects!.CountdownEnd=null;_effects.StopwatchStart=null;_effects.Tick();},"HideEffectClock");
        string[] inkTools=["穿透","笔","荧光","线","箭头","矩形","圆","文字","橡皮","移动"];
        var tool=UiForms.Select(panel,"批注工具（穿透可操作桌面；Esc关闭画布，批注仍保留在成片）",inkTools,Math.Max(0,Array.IndexOf(inkTools,_annotationTool)));
        System.Windows.Automation.AutomationProperties.SetAutomationId(tool,"InkTool");
        var style=new WrapPanel();panel.Children.Add(style);
        var color=new ComboBox{ItemsSource=new[]{"#FF394A","#FFD329","#2485FF","#FFFFFF","#222222"},SelectedIndex=0,Width=130};style.Children.Add(color);
        var width=new ComboBox{ItemsSource=new[]{2d,4d,8d,12d},SelectedIndex=1,Width=85};style.Children.Add(width);
        System.Windows.Automation.AutomationProperties.SetAutomationId(width,"InkWidth");
        var text=new TextBox{Text="重点",Width=160,Padding=new(8)};style.Children.Add(text);
        UiForms.Button(style,"撤销",_vm.Annotations.Undo,"InkUndo");UiForms.Button(style,"重做",_vm.Annotations.Redo,"InkRedo");UiForms.Button(style,"清空",_vm.Annotations.Clear,"InkClear");
        void ApplyTool(){_annotationTool=(string)tool.SelectedItem;_annotations?.SetTool(_annotationTool,(string)color.SelectedItem,(double)width.SelectedItem,text.Text);}
        tool.SelectionChanged+=(_,_)=>{if(_annotationTool=="穿透"&&tool.SelectedIndex>0)ShowAnnotations();ApplyTool();};color.SelectionChanged+=(_,_)=>ApplyTool();width.SelectionChanged+=(_,_)=>ApplyTool();text.TextChanged+=(_,_)=>ApplyTool();
        _selectInkTool=value=>{tool.SelectedItem=value;ApplyTool();};
        _controls.SourceInitialized+=(_,_)=>{if(!DesktopSources.Exclude(new WindowInteropHelper(_controls).Handle))App.Log.Write("CONTROL_CAPTURE_EXCLUSION_UNAVAILABLE");};
        _controls.Closed+=(_,_)=>{_controls=null;_clockView=null;_selectInkTool=null;};_controls.Show();ApplyTool();RaiseControls();
    }
    private static void Execute(System.Windows.Input.ICommand command){if(command.CanExecute(null))command.Execute(null);}
    private async Task CaptureStampAsync()
    {
        if(_capturingStamp)return;_capturingStamp=true;
        bool restoreControls=_controls?.IsVisible==true,restoreAnnotations=_annotations?.IsVisible==true;
        try
        {
            if(_vm.SelectedSource is not {} source)return;
            // Capture a fresh, unscaled source frame, rather than a stale/processed preview.
            System.Windows.Media.Imaging.BitmapSource image;
            using(var capture=new ScreenCapture())
            {
                capture.Start(source);await capture.FirstFrame.WaitAsync(TimeSpan.FromSeconds(10));var pixels=new byte[capture.Width*capture.Height*4];if(!capture.CopyLatest(pixels))throw new IOException("截图来源尚未准备好，请重试。");image=System.Windows.Media.Imaging.BitmapSource.Create(capture.Width,capture.Height,96,96,System.Windows.Media.PixelFormats.Bgra32,null,pixels,capture.Width*4);image.Freeze();
            }
            _annotations?.Hide();_controls?.Hide();
            var bounds=DesktopSources.Bounds(source);var picker=new RegionPicker(bounds,image){Owner=this};
            if(picker.ShowDialog()!=true||picker.Region is not {} region){_vm.SetStatus("已取消截图，原有批注保持不变。");return;}
            int x=(int)(region.X*(double)image.PixelWidth/bounds.Width),y=(int)(region.Y*(double)image.PixelHeight/bounds.Height);
            int w=Math.Min(image.PixelWidth-x,Math.Max(1,(int)(region.Width*(double)image.PixelWidth/bounds.Width))),h=Math.Min(image.PixelHeight-y,Math.Max(1,(int)(region.Height*(double)image.PixelHeight/bounds.Height)));
            var cropped=new System.Windows.Media.Imaging.CroppedBitmap(image,new Int32Rect(x,y,w,h));cropped.Freeze();
            var dir=Path.Combine(_vm.DataDirectory,"Clips");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"clip-"+Guid.NewGuid()+".png");
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(cropped));using(var file=File.Create(path))encoder.Save(file);
            _vm.Annotations.Add(new("贴图",[new(region.X/(double)bounds.Width,region.Y/(double)bounds.Height),new((region.X+region.Width)/(double)bounds.Width,(region.Y+region.Height)/(double)bounds.Height)],Text:path));
            _selectInkTool?.Invoke("移动");_annotationTool="移动";ShowAnnotations();_annotations?.SetTool("移动","#FF394A",4,"");
            restoreAnnotations=true;_vm.SetStatus($"已截取 {w}×{h} 像素。直接拖动贴图；选择“穿透”可操作桌面，撤销可移除贴图。");
        }
        catch(Exception e){UiForms.Error(this,e);}
        finally{if(restoreAnnotations)_annotations?.Show();if(restoreControls){_controls?.Show();RaiseControls();}_capturingStamp=false;}
    }
    private void ShowAnnotations()
    {
        if(_annotations is not null){_annotations.Show();RaiseControls();return;}
        if(_vm.SelectedSource is not {} source)return;
        try
        {
            _annotations=new(_vm.Annotations,_vm.Options,DesktopSources.Bounds(source)){Owner=this};_annotations.Closed+=(_,_)=>_annotations=null;_annotations.Activated+=(_,_)=>RaiseControls(false);_annotations.Show();_annotations.SetTool(_annotationTool,"#FF394A",4,"重点");RaiseControls();
        }
        catch(Exception e){UiForms.Error(this,e);}
    }
    private void RaiseControls(bool activate=true){if(_controls?.IsVisible==true){SetWindowPos(new WindowInteropHelper(_controls).Handle,new(-1),0,0,0,0,0x53);if(activate)_controls.Activate();}}
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
    private void RenameSelected()
    {
        if(!_vm.CanConfigure||_vm.SelectedEntry is not {} entry)return;
        var dialog=new Window{Title="Mimicraft · 文件改名",Width=450,Height=220,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new(22)};dialog.Content=p;
        var name=UiForms.Field(p,"名称（不含扩展名）",Path.GetFileNameWithoutExtension(entry.Path),"NewFileName");
        UiForms.Button(p,"确认改名",async()=>
        {
            try
            {
                if(string.IsNullOrWhiteSpace(name.Text)||name.Text.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.Text.Trim()!=name.Text||name.Text.EndsWith('.'))throw new ArgumentException("请输入有效名称。");
                var dest=Path.Combine(Path.GetDirectoryName(entry.Path)!,name.Text+Path.GetExtension(entry.Path));File.Move(entry.Path,dest);
                if(File.Exists(entry.Path+".probe.json"))File.Move(entry.Path+".probe.json",dest+".probe.json",true);
                dialog.Close();await _vm.RefreshAsync();
            }
            catch(Exception e){UiForms.Error(dialog,e);}
        },"ConfirmRename");dialog.ShowDialog();
    }
    private async void DeleteSelected()
    {
        if(!_vm.CanConfigure||_vm.SelectedEntry is not {} entry)return;
        if(MessageBox.Show(this,"将“"+entry.Name+"”移入回收站？","删除录制",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        try{FileSystem.DeleteFile(entry.Path,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);if(File.Exists(entry.Path+".probe.json"))FileSystem.DeleteFile(entry.Path+".probe.json",UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);await _vm.RefreshAsync();}catch(Exception e){UiForms.Error(this,e);}
    }
    private void RegisterShortcuts()
    {
        var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==0)return;
        foreach(var id in _registered)UnregisterHotKey(hwnd,id);_registered.Clear();
        uint[] keys=[_vm.Options.StartKey,_vm.Options.PauseKey,_vm.Options.StopKey];
        for(int i=0;i<keys.Length;i++)if(RegisterHotKey(hwnd,500+i,_vm.Options.ShortcutModifiers|0x4000,keys[i]))_registered.Add(500+i);else _vm.SetStatus("部分快捷键被占用，请在“画面与保存”中改选F9/F10/F11组合。");
    }
    private nint HotkeyHook(nint hwnd,int msg,nint w,nint l,ref bool handled)
    {
        if(msg!=0x312)return 0;handled=true;if(!IsWindowEnabled(hwnd))return 0;switch((int)w){case 500:Execute(_vm.StartCommand);break;case 501:Execute(_vm.PauseCommand);break;case 502:Execute(_vm.StopCommand);break;}return 0;
    }
    [DllImport("user32.dll",SetLastError=true)]private static extern bool RegisterHotKey(nint hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(nint hwnd,int id);
    [DllImport("user32.dll")]private static extern bool IsWindowEnabled(nint hwnd);
}

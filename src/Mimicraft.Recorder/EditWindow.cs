using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class EditWindow:Window
{
    private readonly MainViewModel _main;
    private readonly EditViewModel _vm;
    private readonly MediaElement _player=new(){LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,IsMuted=true,Volume=0,Height=210};
    private readonly Slider _seek=new(){Minimum=0,Margin=new(0,10,0,10)};
    private readonly TextBlock _position=new();
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(200)};
    private readonly TextBox _start,_end,_split,_speed,_output,_cropX,_cropY,_cropW,_cropH;
    private readonly CheckBox _cropEnabled=new(){Content="裁剪画面区域（原素材像素）",Margin=new(0,16,0,0)};
    private readonly ComboBox _resolution,_bitrate,_format,_gifFps;
    private readonly ListBox _pieces,_queue;
    private readonly StackPanel _controls=new();
    private CancellationTokenSource? _job;
    private bool _updating;
    public EditWindow(MainViewModel main,string? initial=null)
    {
        _main=main;_vm=new();DataContext=_vm;Title="Mimicraft · 片段工坊";Width=1100;Height=850;MinWidth=900;MinHeight=700;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(24)};Content=root;
        var footer=new StackPanel{Margin=new(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};footer.Children.Add(buttons);
        var export=UiForms.Button(buttons,"导出新文件",()=>Export(),"ExportMedia");export.SetBinding(IsEnabledProperty,nameof(EditViewModel.CanEdit));
        UiForms.Button(buttons,"取消处理",()=>_job?.Cancel(),"CancelExport");
        UiForms.Button(buttons,"静音查看导出结果",()=>{if(_lastOutput is {} file&&File.Exists(file)&&Path.GetExtension(file)!=".gif")new PlaybackWindow(file){Owner=this}.Show();},"PreviewExport");
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,0)};status.SetBinding(TextBlock.TextProperty,nameof(EditViewModel.Status));System.Windows.Automation.AutomationProperties.SetAutomationId(status,"EditStatus");footer.Children.Add(status);
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new(350)});root.Children.Add(grid);
        var left=new StackPanel{Margin=new(0,0,24,0)};grid.Children.Add(new ScrollViewer{Content=left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        left.Children.Add(new TextBlock{Text="片段工坊",FontSize=27,FontWeight=FontWeights.Bold});left.Children.Add(new TextBlock{Text="裁剪、整理并导出。原文件保留。",Margin=new(0,8,0,15)});
        UiForms.Button(left,"打开 / 替换原视频",async()=>await OpenAsync(),"OpenEditInput");
        left.Children.Add(_player);left.Children.Add(_position);left.Children.Add(_seek);
        _seek.PreviewMouseLeftButtonUp+=(_,_)=>{if(!_updating)_player.Position=TimeSpan.FromSeconds(_seek.Value);};
        var play=new StackPanel{Orientation=Orientation.Horizontal};left.Children.Add(play);
        UiForms.Button(play,"静音播放",()=>{_player.IsMuted=true;_player.Volume=0;_player.Play();},"EditPlay");UiForms.Button(play,"暂停",_player.Pause);
        UiForms.Button(play,"预览选中片段",()=>{if(SelectedRange() is {} r){_player.Position=TimeSpan.FromSeconds(r.Start);_previewEnd=r.End;_player.Play();}});
        left.Children.Add(new TextBlock{Text="片段区间（导出按列表顺序）",Margin=new(0,15,0,7)});
        _pieces=new ListBox{ItemsSource=_vm.Ranges,Height=145};System.Windows.Automation.AutomationProperties.SetAutomationId(_pieces,"EditPieces");left.Children.Add(_pieces);
        _pieces.SelectionChanged+=(_,_)=>{if(SelectedRange() is {} r&&_start is not null&&_end is not null){_start.Text=N(r.Start);_end.Text=N(r.End);}};
        var zoom=new Slider{Minimum=1,Maximum=5,Value=1};left.Children.Add(new TextBlock{Text="时间轴缩放",Margin=new(0,10,0,0)});left.Children.Add(zoom);
        var timeline=new Canvas{Height=45,Background=System.Windows.Media.Brushes.White};var scroll=new ScrollViewer{Content=timeline,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Height=70};left.Children.Add(scroll);
        void DrawTimeline()
        {
            timeline.Children.Clear();timeline.Width=Math.Max(400,(scroll.ActualWidth-20)*zoom.Value);
            if(_vm.Timeline is null)return;
            foreach(var range in _vm.Timeline.Ranges)
            {
                var b=new System.Windows.Shapes.Rectangle{Width=Math.Max(1,range.Duration/_vm.Timeline.Duration*timeline.Width),Height=22,Fill=System.Windows.Media.Brushes.SeaGreen,Opacity=.65};Canvas.SetLeft(b,range.Start/_vm.Timeline.Duration*timeline.Width);Canvas.SetTop(b,12);timeline.Children.Add(b);
            }
        }
        zoom.ValueChanged+=(_,_)=>DrawTimeline();scroll.SizeChanged+=(_,_)=>DrawTimeline();_vm.PropertyChanged+=(_,_)=>DrawTimeline();
        var right=new ScrollViewer{Content=_controls,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(right,1);grid.Children.Add(right);_controls.SetBinding(IsEnabledProperty,nameof(EditViewModel.CanEdit));
        _start=UiForms.Field(_controls,"片段起点 / 秒","0","TrimStart");_end=UiForms.Field(_controls,"片段终点 / 秒","1","TrimEnd");
        UiForms.Button(_controls,"应用区间",()=>Edit(t=>t.Trim(Index(),Parse(_start),Parse(_end))),"ApplyTrim");
        _split=UiForms.Field(_controls,"分割位置 / 秒","1","SplitTime");UiForms.Button(_controls,"分割选中片段",()=>Edit(t=>t.Split(Index(),Parse(_split))),"SplitClip");
        var edit=new WrapPanel();_controls.Children.Add(edit);UiForms.Button(edit,"删除片段",()=>Edit(t=>t.Delete(Index())));UiForms.Button(edit,"撤销",()=>Edit(t=>t.Undo()),"EditUndo");UiForms.Button(edit,"重做",()=>Edit(t=>t.Redo()),"EditRedo");UiForms.Button(edit,"重置",()=>Edit(t=>t.Reset()),"ResetEdit");
        UiForms.Button(_controls,"将当前片段加入合并队列",()=>{if(_vm.InputPath is not null&&_vm.Timeline is not null)foreach(var range in _vm.Timeline.Ranges)_vm.Queue.Add(new(_vm.InputPath,range));},"QueueCurrent");
        UiForms.Button(_controls,"添加其他视频到队列",async()=>await AddQueueAsync());
        _queue=new ListBox{ItemsSource=_vm.Queue,Height=100,DisplayMemberPath="Path"};_controls.Children.Add(_queue);
        var order=new WrapPanel();_controls.Children.Add(order);UiForms.Button(order,"上移",()=>MoveQueue(-1));UiForms.Button(order,"下移",()=>MoveQueue(1));UiForms.Button(order,"移除",()=>{if(_queue.SelectedItem is MediaInput i)_vm.Queue.Remove(i);});UiForms.Button(order,"清空队列",()=>_vm.Queue.Clear());
        _speed=UiForms.Field(_controls,"倍速（0.25～4；保留并同步已有声音）","1","ExportSpeed");
        _controls.Children.Add(_cropEnabled);_cropX=UiForms.Field(_controls,"画面区域 X","0","EditCropX");_cropY=UiForms.Field(_controls,"画面区域 Y","0","EditCropY");_cropW=UiForms.Field(_controls,"画面区域宽","160","EditCropWidth");_cropH=UiForms.Field(_controls,"画面区域高","90","EditCropHeight");
        _resolution=UiForms.Select(_controls,"输出尺寸",["1280 × 720","1920 × 1080","640 × 360"]);
        _bitrate=UiForms.Select(_controls,"视频码率 / kbps",["1000","2000","4000","8000"],2);
        _format=UiForms.Select(_controls,"文件格式",["mp4","mkv","gif"]);_gifFps=UiForms.Select(_controls,"GIF每秒帧数",["8","12","20","25"],1);
        _output=UiForms.Field(_controls,"另存为（不能覆盖已有文件）","","ExportPath");
        _format.SelectionChanged+=(_,_)=>{if(!string.IsNullOrWhiteSpace(_output.Text))_output.Text=Path.ChangeExtension(_output.Text,(string)_format.SelectedItem);};
        UiForms.Button(_controls,"选择导出位置",()=>{var d=new SaveFileDialog{Filter="MP4|*.mp4|MKV|*.mkv|GIF|*.gif",FilterIndex=_format.SelectedIndex+1,FileName="片段_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")};if(d.ShowDialog(this)==true){_output.Text=d.FileName;_format.SelectedIndex=d.FilterIndex-1;}});
        _timer.Tick+=(_,_)=>
        {
            _updating=true;_seek.Value=_player.Position.TotalSeconds;_position.Text=$"{_player.Position:hh\\:mm\\:ss} / {TimeSpan.FromSeconds(_seek.Maximum):hh\\:mm\\:ss} · 强制静音";_updating=false;
            if(_previewEnd is {} end&&_player.Position.TotalSeconds>=end){_player.Pause();_previewEnd=null;}
        };
        _timer.Start();
        _player.MediaFailed+=(_,e)=>_vm.Status="预览不可用："+e.ErrorException.Message+"；可尝试导出转码。";
        _player.MediaOpened+=(_,_)=>{_player.Play();var firstFrame=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};firstFrame.Tick+=(_,_)=>{firstFrame.Stop();_player.Pause();_player.Position=TimeSpan.Zero;};firstFrame.Start();App.Log.Write("EDITOR_MUTED_MEDIA_OPENED",new{muted=_player.IsMuted,volume=_player.Volume});};
        Closing+=OnClosing;Closed+=(_,_)=>{_timer.Stop();_player.Close();};
        if(initial is not null)Loaded+=async(_,_)=>await OpenAsync(initial);
    }
    private double? _previewEnd;
    private string? _lastOutput;
    private int Index()=>_pieces.SelectedIndex>=0?_pieces.SelectedIndex:0;
    private ClipRange? SelectedRange()=>_vm.Timeline is {} t&&Index()<t.Ranges.Count?t.Ranges[Index()]:null;
    private void Edit(Action<EditTimeline> action){try{if(_vm.Timeline is null)return;action(_vm.Timeline);_vm.Refresh();_pieces.SelectedIndex=0;}catch(Exception e){UiForms.Error(this,e);}}
    private static string N(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
    private static double Parse(TextBox box)=>double.Parse(box.Text,CultureInfo.InvariantCulture);
    private async Task OpenAsync(string? path=null)
    {
        if(!_vm.CanEdit)return;
        if(path is null){var d=new OpenFileDialog{Filter="视频|*.mp4;*.mkv;*.mov;*.avi;*.webm"};if(d.ShowDialog(this)!=true)return;path=d.FileName;}
        try
        {
            var p=await ToolPaths.Discover().ProbeAsync(path);_vm.InputPath=path;_vm.Timeline=new(p.Duration);_vm.Refresh();_pieces.SelectedIndex=0;
            _player.Close();_player.Source=new Uri(path);_player.IsMuted=true;_player.Volume=0;_player.Play();_seek.Maximum=p.Duration;
            _output.Text=Path.Combine(_main.OutputDirectory,"片段_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".mp4");_vm.Status="已打开："+Path.GetFileName(path)+" · "+N(p.Duration)+"秒。";
        }
        catch(Exception e){UiForms.Error(this,e);}
    }
    private async Task AddQueueAsync()
    {
        var d=new OpenFileDialog{Filter="视频|*.mp4;*.mkv;*.mov;*.avi;*.webm",Multiselect=true};if(d.ShowDialog(this)!=true)return;
        try{foreach(var file in d.FileNames){var p=await ToolPaths.Discover().ProbeAsync(file);_vm.Queue.Add(new(file,new(0,p.Duration)));}}catch(Exception e){UiForms.Error(this,e);}
    }
    private void MoveQueue(int offset){int index=_queue.SelectedIndex,next=index+offset;if(index>=0&&next>=0&&next<_vm.Queue.Count){_vm.Queue.Move(index,next);_queue.SelectedIndex=next;}}
    private async void Export()
    {
        if(!_vm.CanEdit)return;
        if(_main.IsBusy){_vm.Status="请先结束其他录制或导出任务。";return;}
        try
        {
            var inputs=_vm.Queue.Count>0?_vm.Queue.ToArray():_vm.Timeline?.Ranges.Select(r=>new MediaInput(_vm.InputPath!,r)).ToArray()??[];
            int selected=_resolution.SelectedIndex;int width=selected==1?1920:selected==2?640:1280,height=selected==1?1080:selected==2?360:720;
            PixelRegion? crop=_cropEnabled.IsChecked==true?new(int.Parse(_cropX.Text),int.Parse(_cropY.Text),int.Parse(_cropW.Text),int.Parse(_cropH.Text)):null;
            var options=new ExportOptions(width,height,30,int.Parse((string)_bitrate.SelectedItem),Parse(_speed),_format.SelectedIndex==2,int.Parse((string)_gifFps.SelectedItem),Crop:crop);
            _job=new();_vm.CanEdit=false;_main.SetMediaBusy(true);_player.Pause();
            await new MediaJobs(App.Log).ExportAsync(inputs,_output.Text,options,_job.Token,new Progress<string>(s=>_vm.Status=s));
            _lastOutput=_output.Text;_vm.Status="导出完成："+_output.Text;await _main.RefreshAsync();
        }
        catch(OperationCanceledException){_vm.Status="已取消，原文件保留。";}
        catch(Exception e){_vm.Status=e.Message;App.Log.Write("EDITOR_EXPORT_FAILED",error:e);}
        finally{_job?.Dispose();_job=null;_vm.CanEdit=true;_main.SetMediaBusy(false);}
    }
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(_job is null)return;e.Cancel=true;
        if(MessageBox.Show(this,"导出正在进行，取消当前导出？取消后可关闭窗口。","片段工坊",MessageBoxButton.YesNo)==MessageBoxResult.Yes)_job.Cancel();
    }
}

public sealed class EditViewModel:INotifyPropertyChanged
{
    public string? InputPath { get;set; }
    public EditTimeline? Timeline { get;set; }
    public ObservableCollection<ClipRange> Ranges { get; }=[];
    public ObservableCollection<MediaInput> Queue { get; }=[];
    private bool _canEdit=true;private string _status="打开视频开始整理；合并队列不为空时优先导出队列。";
    public bool CanEdit{get=>_canEdit;set{_canEdit=value;PropertyChanged?.Invoke(this,new(nameof(CanEdit)));}}
    public string Status{get=>_status;set{_status=value;PropertyChanged?.Invoke(this,new(nameof(Status)));}}
    public void Refresh(){Ranges.Clear();if(Timeline is {} t)foreach(var r in t.Ranges)Ranges.Add(r);PropertyChanged?.Invoke(this,new(nameof(Timeline)));}
    public event PropertyChangedEventHandler? PropertyChanged;
}

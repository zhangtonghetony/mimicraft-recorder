using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly RecorderEngine _engine=new(App.Log);
    private readonly DispatcherTimer _timer=new() { Interval=TimeSpan.FromMilliseconds(250) };
    private string _status="准备就绪；声音与摄像头按所选设置开启。";
    private ImageSource? _preview;
    private bool _checking;
    private int _previewPending;
    private readonly bool _verification;
    private CancellationTokenSource? _countdown;
    private SchedulePlan? _schedule;
    private bool _starting;
    private DateTimeOffset? _stopAt;
    private bool _mediaBusy;
    public RecorderOptions Options { get; private set; }=new();
    public AnnotationDocument Annotations { get; }=new();
    public string OutputDirectory { get; private set; }
    public string DataDirectory { get; }
    public ObservableCollection<CaptureSource> Sources { get; }=new(ScreenCapture.GetMonitors());
    private CaptureSource? _selectedSource;
    public CaptureSource? SelectedSource { get=>_selectedSource;set{if(_selectedSource!=value){_selectedSource=value;Options.Region=null;Preview=null;Changed();NotifyCaptureSettings();}} }
    public string[] Resolutions { get; }=["1280 × 720","1920 × 1080"];
    public int[] FrameRates { get; }=[15,20,24,25,30,60];
    public string[] Encoders { get; }=["自动（硬件优先）","软件 / OpenH264","Intel QSV"];
    public string Resolution { get; set; }="1280 × 720";
    public int Fps { get; set; }=30;
    public string Encoder { get; set; }="自动（硬件优先）";
    public ObservableCollection<RecordingEntry> Entries { get; }=[];
    private RecordingEntry? _selectedEntry;
    public RecordingEntry? SelectedEntry { get=>_selectedEntry;set{_selectedEntry=value;Changed();CommandManager.InvalidateRequerySuggested();} }
    public string Status { get=>_status;private set{_status=value;Changed();} }
    public ImageSource? Preview { get=>_preview;private set{_preview=value;Changed();Changed(nameof(PlaceholderVisibility));} }
    public Visibility PlaceholderVisibility => Preview is null?Visibility.Visible:Visibility.Collapsed;
    public bool CanConfigure=>!IsBusy;
    public bool IsBusy=>_engine.State.IsBusy||_starting||_mediaBusy;
    public void SetMediaBusy(bool busy){_mediaBusy=busy;NotifyState();}
    public void SetOverlay(OverlayBitmap? overlay)=>_engine.SetOverlay(overlay);
    public void MoveRegion(PixelRegion region){_engine.MoveRegion(region);Options.Region=region;NotifyCaptureSettings();}
    public string CaptureRangeLabel=>Options.Region is {} r?$"录制范围：选区 {r.Width}×{r.Height}（{r.X}, {r.Y}）":SelectedSource?.IsWindow==true?"录制范围：整个窗口":"录制范围：整屏";
    public string ResetRangeLabel=>SelectedSource?.IsWindow==true?"恢复完整窗口":"恢复整屏";
    public string CameraRecordingLabel=>Options.Layers.Any(l=>l.Kind=="camera"&&l.Visible)?"摄像头：将录入画面":"摄像头：未加入录制";
    private void NotifyCaptureSettings(){Changed(nameof(CaptureRangeLabel));Changed(nameof(ResetRangeLabel));Changed(nameof(CameraRecordingLabel));}
    public void ResetCaptureRegion(){if(!CanConfigure){Status="请先停止录制，再恢复完整画面。";return;}Options.Region=null;Preview=null;ApplyOptions();Status=SelectedSource?.IsWindow==true?"已清除选区，下次录制整个窗口。":"已清除选区，下次录制整个显示器。";}
    public Func<int,int,OverlayBitmap?>? OverlayFactory { get=>_engine.OverlayFactory;set=>_engine.OverlayFactory=value; }
    public Action<byte[],int,int>? FrameTransform { get=>_engine.FrameTransform;set=>_engine.FrameTransform=value; }
    public Func<Task>? PrepareSources { get;set; }
    public Func<Task>? ReleaseSources { get;set; }
    public (int Width,int Height) CaptureSize=>_engine.CaptureSize;
    public RecordingState Session=>_engine.State.Value;
    public string Elapsed=>_engine.Clock.Elapsed.ToString(@"hh\:mm\:ss");
    public string PauseLabel=>Session==RecordingState.Paused?"继续":"暂停";
    public string StartLabel=>Options.Destinations.Length>0?"●  录制并推流":Options.Audio.AudioOnly?"●  开始录音频":"●  开始录制";
    public string ActivityHint=>Options.Destinations.Length>0?$"●  同时推流 {Options.Destinations.Length} 路":"●  本地录制 · 设备按设置开启";
    public string AudioModeLabel=>Options.Audio.Mode switch{"system"=>"系统声音","microphone"=>"麦克风","both"=>"系统声音 + 麦克风",_=>"不录声音"};
    public string SourceWarning { get;set; }="";
    public string StateLabel=>Session switch { RecordingState.Starting=>"正在准备",RecordingState.Recording=>"录制中",RecordingState.Paused=>"已暂停",RecordingState.Stopping=>"正在停止",RecordingState.Finalizing=>"正在整理文件",RecordingState.Completed=>"已保存",RecordingState.Failed=>"请检查提示",_=>"准备就绪" };
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PracticeCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand CancelWaitCommand { get; }
    public event Action? PracticeRequested;
    public event Action<string>? PlayRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(bool verification)
    {
        _verification=verification;
        DataDirectory=Environment.GetEnvironmentVariable("MIMICRAFT_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mimicraft","Recorder");
        OutputDirectory=verification?Path.Combine(FindEvRoot(),"artifacts","recordings"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"Mimicraft Recorder");
        Directory.CreateDirectory(OutputDirectory);Directory.CreateDirectory(DataDirectory);
        SelectedSource=Sources.FirstOrDefault();LoadSettings();
        StartCommand=new AsyncCommand(StartAsync,()=>CanConfigure&&SelectedSource is not null);
        StopCommand=new AsyncCommand(StopAsync,()=>Session is RecordingState.Recording or RecordingState.Paused);
        PauseCommand=new AsyncCommand(async()=>{try{await _engine.TogglePauseAsync();}catch(Exception e){Report(e);}},()=>Session is RecordingState.Recording or RecordingState.Paused);
        CancelWaitCommand=new ActionCommand(()=>{_countdown?.Cancel();_schedule=null;Status="已取消等待和预约。";NotifyState();},()=>_countdown is not null||_schedule is not null);
        PracticeCommand=new ActionCommand(()=>PracticeRequested?.Invoke(),()=>CanConfigure);
        OpenFolderCommand=new ActionCommand(()=>Process.Start(new ProcessStartInfo("explorer.exe",OutputDirectory){UseShellExecute=true}));
        PlayCommand=new ActionCommand(()=>{if(SelectedEntry is not null)PlayRequested?.Invoke(SelectedEntry.Path);},()=>SelectedEntry is not null);
        RefreshCommand=new AsyncCommand(RefreshAsync,()=>CanConfigure);
        _engine.State.Changed+=_=>Application.Current.Dispatcher.Invoke(NotifyState);
        _engine.PreviewFrame+=(data,w,h)=>
        {
            // Coalesce previews when a dialog or a busy UI blocks the dispatcher.
            // Encoding must not accumulate an unbounded bitmap queue.
            if(Interlocked.CompareExchange(ref _previewPending,1,0)!=0)return;
            try
            {
                var image=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,data,w*4);image.Freeze();
                Application.Current.Dispatcher.BeginInvoke(()=>{try{Preview=image;}finally{Interlocked.Exchange(ref _previewPending,0);}},DispatcherPriority.Background);
            }
            catch { Interlocked.Exchange(ref _previewPending,0);throw; }
        };
        _timer.Tick+=async(_,_)=>
        {
            Changed(nameof(Elapsed));
            if(Session==RecordingState.Recording&&!string.IsNullOrWhiteSpace(_engine.StreamWarning))Status="本地录制继续，推流警告："+_engine.StreamWarning;
            if(_checking)return;_checking=true;
            try{await _engine.CheckHealthAsync();if(Session==RecordingState.Failed&&ReleaseSources is not null)await ReleaseSources();}catch(Exception e){if(ReleaseSources is not null)await ReleaseSources();Report(e);}finally{_checking=false;}
            if(_schedule is {Consumed:false} schedule&&schedule.Poll(DateTimeOffset.Now,IsBusy)){_schedule=null;await BeginAsync(true);}
            else if(_schedule is {Consumed:true,MissReason:{} reason}){Status=reason;_schedule=null;NotifyState();}
            if(_stopAt is {} stopAt&&DateTimeOffset.Now>=stopAt&&Session is RecordingState.Recording or RecordingState.Paused){_stopAt=null;await StopAsync();}
        };
        _timer.Start();
    }

    public void SelectPractice(nint handle,bool monitor=false)
    {
        foreach(var old in Sources.Where(s=>s.IsWindow).ToArray())Sources.Remove(old);
        var source=new CaptureSource(monitor?"练习画面（全屏显示器 / 无声）":"练习画面（动态 / 无声）",handle,!monitor);Sources.Add(source);SelectedSource=source;Changed(nameof(SelectedSource));
        Status="已选择原创练习窗口；开始录制会真实采集该窗口。";
    }

    private async Task StartAsync()
        =>await BeginAsync(false);
    private async Task BeginAsync(bool scheduled)
    {
        if(IsBusy||SelectedSource is null)return;
        _starting=true;NotifyState();
        try
        {
            if(!scheduled&&Options.CountdownSeconds>0)
            {
                _countdown=new();NotifyState();
                for(int left=Options.CountdownSeconds;left>0;left--){Status=$"{left}秒后开始；可取消等待。";await Task.Delay(1000,_countdown.Token);}
            }
            Status="正在检查来源、输出目录和编码器…";
            var settings=new RecordingSettings(Resolution.StartsWith("1920")?1920:1280,Resolution.StartsWith("1920")?1080:720,Fps,Encoder=="软件 / OpenH264"?"libopenh264":Encoder=="Intel QSV"?"h264_qsv":"auto",Options.Audio.AudioOnly?null:Options.Region,Options.BitrateKbps,Options.Saturation,Options.Profile,Options.Container,Options.Audio,Options.Destinations);
            SaveSettings();
            if(Options.Audio.AudioOnly&&ReleaseSources is not null)await ReleaseSources();
            if(!Options.Audio.AudioOnly&&PrepareSources is not null)await PrepareSources();
            await _engine.StartAsync(SelectedSource!,settings,OutputDirectory);
            _stopAt=Options.StopAfterSeconds>0?DateTimeOffset.Now.AddSeconds(Options.StopAfterSeconds):null;
            Status=(Options.Audio.AudioOnly?"正在录音频，不采集屏幕。":"正在录制；声音："+AudioModeLabel)+(Options.Destinations.Length>0?$"；同时推流{Options.Destinations.Length}路，请核对接收端。":"")+(SourceWarning!=""?"；"+SourceWarning:"");
        }
        catch(OperationCanceledException){Status="已取消倒数。";}
        catch(Exception e){if(ReleaseSources is not null)await ReleaseSources();Report(e);}
        finally{_countdown?.Dispose();_countdown=null;_starting=false;NotifyState();}
    }

    public async Task StopAsync()
    {
        _stopAt=null;
        try
        {
            Status="正在完成编码、检查文件，请稍候…";
            var entry=await _engine.StopAsync();
            if(entry is not null){Entries.Insert(0,entry);SelectedEntry=entry;Status="已保存："+entry.Name+" · "+entry.Encoder;}
        }
        catch(Exception e){Report(e);}
        finally{if(ReleaseSources is not null)await ReleaseSources();}
    }

    public async Task RefreshAsync()
    {
        try
        {
            Entries.Clear();
            foreach(var file in Directory.GetFiles(OutputDirectory).Where(p=>Path.GetExtension(p).ToLowerInvariant() is ".mp4" or ".mkv" or ".wav" or ".mp3" or ".m4a").Where(p=>!p.Contains(".pending.")&&!p.Contains(".partial.")&&!Path.GetFileName(p).StartsWith('.')).OrderByDescending(File.GetLastWriteTime).Take(50))
            {
                try
                {
                    var probe=await ToolPaths.Discover().ProbeAsync(file,allowAudioOnly:true);
                    Entries.Add(new(file,File.GetLastWriteTime(file),probe.Duration,new FileInfo(file).Length,probe.Codec));
                }
                catch(Exception e){App.Log.Write("LIBRARY_FILE_UNREADABLE",error:e);}
            }
            SelectedEntry=Entries.FirstOrDefault();
        }
        catch(Exception e){Report(e);}
    }

    private void Report(Exception e){Status=e.Message.Length>260?e.Message[..260]+"…":e.Message;App.Log.Write("UI_OPERATION_FAILED",error:e);}
    private void LoadSettings()
    {
        try
        {
            var path=Path.Combine(DataDirectory,"settings.json");if(!File.Exists(path))return;
            using var doc=JsonDocument.Parse(File.ReadAllText(path));var root=doc.RootElement;
            int fps=root.GetProperty("fps").GetInt32();string resolution=root.GetProperty("resolution").GetString()!;string encoder=root.GetProperty("encoder").GetString()!;
            if(FrameRates.Contains(fps)&&Resolutions.Contains(resolution)&&Encoders.Contains(encoder)){Fps=fps;Resolution=resolution;Encoder=encoder;}
            if(root.TryGetProperty("options",out var options))
            {
                var candidate=options.Deserialize<RecorderOptions>();
                if(candidate is not null){candidate.Validate();Options=candidate;}
            }
            if(!_verification&&!string.IsNullOrWhiteSpace(Options.OutputDirectory))OutputDirectory=Options.OutputDirectory;
        }
        catch(Exception e){App.Log.Write("SETTINGS_INVALID_DEFAULTS_USED",error:e);}
    }
    private void SaveSettings()
    {
        var temp=Path.Combine(DataDirectory,"settings.json.tmp");File.WriteAllText(temp,JsonSerializer.Serialize(new{fps=Fps,resolution=Resolution,encoder=Encoder,options=Options}));File.Move(temp,Path.Combine(DataDirectory,"settings.json"),true);
    }
    public void ApplyOptions(){Options.Validate();if(!_verification&&!string.IsNullOrWhiteSpace(Options.OutputDirectory))OutputDirectory=Options.OutputDirectory;SaveSettings();NotifyCaptureSettings();Changed(nameof(OutputDirectory));Changed(nameof(StartLabel));Changed(nameof(ActivityHint));}
    public void SetSchedule(DateTimeOffset? start){_schedule=start is {} value?new(value,Options.StopAfterSeconds>0?TimeSpan.FromSeconds(Options.StopAfterSeconds):null):null;Status=start is {} time?$"计划于 {time:MM-dd HH:mm:ss} 开始录制。":"已更新倒数和自动停止设置。";NotifyState();}
    public void SetStatus(string status)=>Status=status;
    public void RefreshSources(){Sources.Clear();foreach(var s in ScreenCapture.GetMonitors().Concat(DesktopSources.GetWindows()))Sources.Add(s);SelectedSource=Sources.FirstOrDefault();Options.Region=null;Changed(nameof(SelectedSource));}
    private void NotifyState(){Changed(nameof(CanConfigure));Changed(nameof(IsBusy));Changed(nameof(Session));Changed(nameof(StateLabel));Changed(nameof(PauseLabel));CommandManager.InvalidateRequerySuggested();}
    public static string FindEvRoot()
    {
        var requested=Environment.GetEnvironmentVariable("MIMICRAFT_EV_ROOT");if(!string.IsNullOrWhiteSpace(requested)&&File.Exists(Path.Combine(requested,"Mimicraft.Recorder.slnx")))return Path.GetFullPath(requested);
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"Mimicraft.Recorder.slnx")))return d.FullName;
        return AppContext.BaseDirectory;
    }
    public void Close(){_timer.Stop();_schedule=null;_countdown?.Cancel();SaveSettings();}
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}

public sealed class ActionCommand(Action execute,Func<bool>? can=null):ICommand
{
    public bool CanExecute(object? p)=>can?.Invoke()??true;
    public void Execute(object? p)=>execute();
    public event EventHandler? CanExecuteChanged { add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value; }
}
public sealed class AsyncCommand(Func<Task> execute,Func<bool>? can=null):ICommand
{
    private bool _running;
    public bool CanExecute(object? p)=>!_running&&(can?.Invoke()??true);
    public async void Execute(object? p)
    {
        if(!CanExecute(p))return;_running=true;CommandManager.InvalidateRequerySuggested();
        try{await execute();}finally{_running=false;CommandManager.InvalidateRequerySuggested();}
    }
    public event EventHandler? CanExecuteChanged { add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value; }
}

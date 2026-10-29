using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Mimicraft.Recorder;

public partial class MainWindow:Window
{
    private readonly MainViewModel _vm;
    private PracticeWindow? _practice;
    private bool _closing;
    private readonly DispatcherTimer _devTimer=new(){Interval=TimeSpan.FromMilliseconds(600)};
    private readonly string _statePath;
    public MainWindow(bool verification)
    {
        InitializeComponent();_vm=new(verification);DataContext=_vm;InitializeTools();
        _vm.PracticeRequested+=()=>
        {
            bool full=verification&&Environment.GetEnvironmentVariable("MIMICRAFT_FULLSCREEN_PRACTICE")=="1";
            if(_practice is null||!_practice.IsLoaded)
            {
                _practice=new();
                if(full){_practice.WindowStyle=WindowStyle.None;_practice.ResizeMode=ResizeMode.NoResize;_practice.Topmost=true;_practice.WindowState=WindowState.Maximized;}
                _practice.Show();
            }
            var handle=new WindowInteropHelper(_practice).Handle;
            _vm.SelectPractice(full?MonitorFromWindow(handle,2):handle,full);
        };
        _vm.PlayRequested+=path=>new PlaybackWindow(path){Owner=this}.Show();
        Closing+=OnClosing;
        string root=MainViewModel.FindEvRoot();_statePath=Path.Combine(File.Exists(Path.Combine(root,"Mimicraft.Recorder.slnx"))?Path.Combine(root,"artifacts","dev"):Path.Combine(_vm.DataDirectory,"Development"),$"state-{Environment.ProcessId}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        _devTimer.Tick+=(_,_)=>
        {
            try
            {
                var temp=_statePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(new{pid=Environment.ProcessId,busy=_vm.IsBusy,state=_vm.Session.ToString(),utc=DateTimeOffset.UtcNow}));File.Move(temp,_statePath,true);
                if(File.Exists(_statePath+".close")&&!_vm.IsBusy){File.Delete(_statePath+".close");Close();}
            }
            catch(IOException e){App.Log.Write("DEV_STATE_IO",error:e);}
        };
        _devTimer.Start();
        Loaded+=async(_,_)=>{await _vm.RefreshAsync();App.Log.Write("WINDOW_VISIBLE",new{hwnd=new WindowInteropHelper(this).Handle.ToInt64(),title=Title});};
        Closed+=(_,_)=>{_devTimer.Stop();_practice?.Close();_vm.Close();try{File.Delete(_statePath);}catch(IOException){};Application.Current.Shutdown();};
    }
    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(_closing)return;
        if(!_vm.IsBusy){if(_scene is not null){e.Cancel=true;await StopSceneAsync();_closing=true;_ = Dispatcher.BeginInvoke(Close);}return;}
        e.Cancel=true;
        if(_vm.Session is not (Core.RecordingState.Recording or Core.RecordingState.Paused)){MessageBox.Show(this,"会话正在准备、导出或收尾，请结束当前任务后关闭。","请稍候");return;}
        var choice=MessageBox.Show(this,"录制尚未结束。是：停止保存并退出；否：收至托盘继续；取消：返回工作台。","结束录制",MessageBoxButton.YesNoCancel);
        if(choice==MessageBoxResult.No){_tray?.Hide();return;}if(choice!=MessageBoxResult.Yes)return;
        await _vm.StopAsync();_closing=true;_ = Dispatcher.BeginInvoke(Close);
    }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd,uint flags);
}

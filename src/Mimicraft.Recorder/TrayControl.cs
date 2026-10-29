using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Mimicraft.Recorder;

public sealed class TrayControl:IDisposable
{
    private const int Message=0x804d;private readonly Window _window;private readonly MainViewModel _vm;private NotifyData _data;private HwndSource? _source;
    private bool _registered;private readonly uint _taskbarCreated=RegisterWindowMessage("TaskbarCreated");
    public TrayControl(Window window,MainViewModel vm)
    {
        _window=window;_vm=vm;
        window.SourceInitialized+=(_,_)=>
        {
            var hwnd=new WindowInteropHelper(window).Handle;_data=new(){Size=Marshal.SizeOf<NotifyData>(),Window=hwnd,Id=1,Flags=7,Callback=Message,Icon=LoadIcon(0,new(32512)),Tip="Mimicraft Recorder",Info="",InfoTitle=""};
            _registered=ShellNotifyIcon(0,ref _data);if(!_registered)App.Log.Write("TRAY_UNAVAILABLE");
            _source=HwndSource.FromHwnd(hwnd);_source?.AddHook(Hook);
        };
    }
    public void Hide(){if(!_registered){_vm.SetStatus("托盘不可用，工作台保持显示。");return;}_window.Hide();_vm.SetStatus("应用在托盘继续运行。双击图标恢复，右键可暂停/停止/退出。");}
    public void Restore(){_window.Show();_window.WindowState=WindowState.Normal;_window.Activate();}
    private nint Hook(nint hwnd,int message,nint w,nint l,ref bool handled)
    {
        if((uint)message==_taskbarCreated){_registered=ShellNotifyIcon(0,ref _data);if(!_registered)Restore();return 0;}
        if(message!=Message)return 0;handled=true;
        if((int)l==0x203)Restore();
        if((int)l is 0x205 or 0x7b)
        {
            var menu=new ContextMenu{Placement=PlacementMode.MousePoint};
            void Add(string label,Action action){var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}
            Add("显示工作台",Restore);Add("暂停 / 继续",()=>{if(_vm.PauseCommand.CanExecute(null))_vm.PauseCommand.Execute(null);});Add("停止并保存",()=>{if(_vm.StopCommand.CanExecute(null))_vm.StopCommand.Execute(null);});Add("退出",()=>{Restore();_window.Close();});menu.IsOpen=true;
        }
        return 0;
    }
    public void Dispose(){if(_source is not null){ShellNotifyIcon(2,ref _data);_source.RemoveHook(Hook);_source=null;}}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct NotifyData
    {
        public int Size;public nint Window;public uint Id,Flags,Callback;public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;
        public uint InfoFlags;public Guid Guid;public nint BalloonIcon;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,EntryPoint="Shell_NotifyIconW")]private static extern bool ShellNotifyIcon(uint action,ref NotifyData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint LoadIcon(nint instance,nint id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string message);
}

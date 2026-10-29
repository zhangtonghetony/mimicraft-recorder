using System.Windows;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public partial class App : Application
{
    public static readonly EventLog Log=new();
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Write("APP_START",new { version="0.3.2",args=e.Args, audio="user-selected",session=System.Diagnostics.Process.GetCurrentProcess().SessionId });
        DispatcherUnhandledException += (_,ev)=> { Log.Write("UI_EXCEPTION",error:ev.Exception); MessageBox.Show("程序遇到错误。详细日志："+Log.FilePath+"\n"+ev.Exception.Message,"Mimicraft Recorder"); ev.Handled=true; };
        var window=new MainWindow(e.Args.Contains("--verification")); MainWindow=window; window.Show();
    }
}

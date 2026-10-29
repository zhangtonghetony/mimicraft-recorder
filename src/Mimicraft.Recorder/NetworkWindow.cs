using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class NetworkWindow:Window
{
    private readonly List<NetworkProcess> _pushers=[];private readonly MainViewModel _vm;private CancellationTokenSource? _stop;private readonly ObservableCollection<string> _files=[];
    public NetworkWindow(MainViewModel vm)
    {
        _vm=vm;Title="Mimicraft · 网络工作台";Width=780;Height=800;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SourceInitialized+=(_,_)=>{if(!DesktopSources.Exclude(new System.Windows.Interop.WindowInteropHelper(this).Handle))MessageBox.Show(this,"当前系统无法排除本窗口的录屏捕获，开始录屏前请关闭含推流密钥的网络窗口。","捕获提示");};
        var panel=new StackPanel{Margin=new(24)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="网络工作台",FontSize=26,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock{Text="地址仅供本次会话使用，不保存密钥。桌面推流与本地录制同步；本地直播采用UDP接收，不需要云服务。真实平台推流地址由你提供。",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,12)});
        var addresses=UiForms.Field(panel,"推流目的端（每行一个，最多3个，RTMP/RTMPS/UDP/TCP）",string.Join(Environment.NewLine,vm.Options.Destinations),"StreamAddresses");addresses.AcceptsReturn=true;addresses.Height=95;
        UiForms.Button(panel,"填入本地直播地址",()=>addresses.Text="udp://127.0.0.1:24780?pkt_size=1316");
        string[] Destinations(){var values=addresses.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);if(values.Length is <1 or >3)throw new ArgumentException("请输入1～3个目的端。");foreach(var value in values)StreamAddress.Validate(value);return values;}
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,10)};System.Windows.Automation.AutomationProperties.SetAutomationId(status,"NetworkStatus");panel.Children.Add(status);
        UiForms.Button(panel,"应用到下一次桌面录制",()=>{try{if(!vm.CanConfigure)throw new InvalidOperationException("请先结束当前任务。");vm.Options.Destinations=Destinations();vm.ApplyOptions();status.Text="已设置。返回主界面开始录制，即同时推流并保存本地。";}catch(Exception e){UiForms.Error(this,e);}},"ApplyLiveDestinations");
        UiForms.Button(panel,"关闭桌面推流",()=>{if(vm.CanConfigure){vm.Options.Destinations=[];vm.ApplyOptions();status.Text="下一次录制不推流。";}});
        panel.Children.Add(new Separator{Margin=new(0,12,0,8)});
        var list=new ListBox{ItemsSource=_files,Height=110};panel.Children.Add(list);
        var row=new WrapPanel();panel.Children.Add(row);
        UiForms.Button(row,"添加直播视频",()=>{var d=new OpenFileDialog{Multiselect=true,Filter="视频|*.mp4;*.mkv;*.mov;*.avi;*.webm"};if(d.ShowDialog(this)==true)foreach(var file in d.FileNames)_files.Add(file);});UiForms.Button(row,"上移",()=>{int i=list.SelectedIndex;if(i>0)_files.Move(i,i-1);});UiForms.Button(row,"下移",()=>{int i=list.SelectedIndex;if(i>=0&&i+1<_files.Count)_files.Move(i,i+1);});UiForms.Button(row,"移除",()=>{if(list.SelectedItem is string file)_files.Remove(file);});
        var repeat=new CheckBox{Content="循环播放列表",IsChecked=false,Margin=new(0,10,0,10)};panel.Children.Add(repeat);
        UiForms.Button(panel,"开始文件推流",async()=>
        {
            if(_stop is not null||vm.IsBusy)return;string? prepared=null;
            try
            {
                var targets=Destinations();if(_files.Count==0)throw new ArgumentException("请添加视频。");_stop=new();vm.SetMediaBusy(true);status.Text="正在统一文件格式…";
                prepared=Path.Combine(vm.DataDirectory,"playlist-"+Guid.NewGuid()+".mp4");var inputs=new List<MediaInput>();foreach(var file in _files){var p=await ToolPaths.Discover().ProbeAsync(file);inputs.Add(new(file,new(0,p.Duration)));}
                await new MediaJobs(App.Log).ExportAsync(inputs,prepared,new(),_stop.Token);
                foreach(var destination in targets)_pushers.Add(NetworkJobs.PushFile(prepared,destination,repeat.IsChecked==true));
                while(!_stop.IsCancellationRequested&&_pushers.Any(p=>!p.HasExited)){status.Text=string.Join("\n",_pushers.Select((p,i)=>$"目的端{i+1}："+(p.HasExited?$"已停止 / 代码{p.ExitCode}":"正在推送")));await Task.Delay(500,_stop.Token);}
                status.Text=string.Join("\n",_pushers.Select((p,i)=>$"目的端{i+1}退出代码 {p.ExitCode} "+p.Diagnostic));
            }
            catch(OperationCanceledException){status.Text="已停止文件推流。";}
            catch(Exception e){status.Text=e.Message;App.Log.Write("FILE_PUSH_FAILED",error:new IOException("文件推流失败，详见本地窗口。"));}
            finally{foreach(var p in _pushers)await p.DisposeAsync();_pushers.Clear();_stop?.Dispose();_stop=null;vm.SetMediaBusy(false);if(prepared is not null&&File.Exists(prepared))File.Delete(prepared);}
        },"PushPlaylist");
        panel.Children.Add(new Separator{Margin=new(0,12,0,8)});
        var input=UiForms.Field(panel,"串流存盘 / 本地直播接收地址","udp://127.0.0.1:24780","NetworkInput");
        UiForms.Button(panel,"接收并存盘",async()=>
        {
            if(_stop is not null||vm.IsBusy)return;
            var d=new SaveFileDialog{Filter="MKV|*.mkv|MP4|*.mp4",FileName="网络录制_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")};if(d.ShowDialog(this)!=true)return;
            try{_stop=new();vm.SetMediaBusy(true);var p=await new NetworkJobs(App.Log).IngestAsync(input.Text,d.FileName,_stop.Token,new Progress<string>(s=>status.Text=s));status.Text=$"已保存 {p.Duration:0.0}秒：{d.FileName}";await vm.RefreshAsync();}
            catch(Exception e){status.Text=e.Message;}
            finally{_stop?.Dispose();_stop=null;vm.SetMediaBusy(false);}
        },"StartIngest");
        UiForms.Button(panel,"停止当前网络任务",()=>_stop?.Cancel(),"StopNetwork");
        Closing+=(_,e)=>{if(_stop is not null){e.Cancel=true;_stop.Cancel();status.Text="正在收尾，请稍后关闭。";}};
    }
}

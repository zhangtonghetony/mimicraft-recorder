using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class RecoveryWindow:Window
{
    private CancellationTokenSource? _stop;
    public RecoveryWindow(MainViewModel vm)
    {
        Title="Mimicraft · 诊断与恢复";Width=720;Height=550;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new(24)};Content=panel;
        panel.Children.Add(new TextBlock{Text="诊断与恢复",FontSize=26,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock{Text="从未完成录制中恢复可读媒体，另存副本并检查完整解码。不能恢复不存在的画面，也不保证任意损坏MP4可修复。",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,12)});
        var candidates=new ListBox{ItemsSource=RecoveryService.Candidates(vm.OutputDirectory),Height=110};panel.Children.Add(candidates);
        var input=UiForms.Field(panel,"输入文件","","RecoveryInput");candidates.SelectionChanged+=(_,_)=>{if(candidates.SelectedItem is string file)input.Text=file;};
        UiForms.Button(panel,"选择待检查文件",()=>{var d=new OpenFileDialog{Filter="媒体或临时文件|*.*"};if(d.ShowDialog(this)==true)input.Text=d.FileName;});
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,12)};System.Windows.Automation.AutomationProperties.SetAutomationId(status,"RecoveryStatus");panel.Children.Add(status);
        UiForms.Button(panel,"恢复并另存副本",async()=>
        {
            if(_stop is not null||vm.IsBusy)return;var d=new SaveFileDialog{Filter="MKV|*.mkv|MP4|*.mp4",FileName="恢复副本_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")};if(d.ShowDialog(this)!=true)return;
            try{_stop=new();vm.SetMediaBusy(true);status.Text="检查并恢复中…";var p=await new RecoveryService(App.Log).RecoverAsync(input.Text,d.FileName,_stop.Token);status.Text=$"已恢复并通过解码：{p.Duration:0.0}秒。";await vm.RefreshAsync();}
            catch(OperationCanceledException){status.Text="已取消，原文件保留。";}catch(Exception e){status.Text=e.Message;}
            finally{_stop?.Dispose();_stop=null;vm.SetMediaBusy(false);}
        },"RecoverMedia");
        UiForms.Button(panel,"取消检查",()=>_stop?.Cancel());Closing+=(_,e)=>{if(_stop is not null){e.Cancel=true;_stop.Cancel();}};
    }
}

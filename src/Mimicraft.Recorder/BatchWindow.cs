using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class BatchWindow:Window
{
    private CancellationTokenSource? _cancel;
    private string[] _files=[];
    public BatchWindow(MainViewModel main)
    {
        Title="Mimicraft · 批量画面标记";Width=590;Height=430;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new(24)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="批量画面标记",FontSize=25,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock{Text="使用“画面与保存”中的文字/图片标记设置，统一导出720p MP4，保留原视频和已有声音。",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,8)});
        var status=new TextBlock{Text="尚未选择视频。",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,8)};panel.Children.Add(status);
        var choose=UiForms.Button(panel,"选择多个视频",()=>{var d=new OpenFileDialog{Multiselect=true,Filter="视频|*.mp4;*.mkv;*.mov;*.avi;*.webm"};if(d.ShowDialog(this)==true){_files=d.FileNames;status.Text=$"已选择{_files.Length}个文件。";}},"BatchChoose");
        var export=UiForms.Button(panel,"另存到新文件夹",async()=>
        {
            if(_cancel is not null)return;
            if(main.IsBusy){status.Text="请先结束其他录制或导出任务。";return;}
            if(_files.Length==0){status.Text="请先选择文件。";return;}
            if(string.IsNullOrWhiteSpace(main.Options.WatermarkText)&&!File.Exists(main.Options.WatermarkImage)){status.Text="请先在“画面与保存”设置文字或图片标记。";return;}
            var d=new OpenFolderDialog();if(d.ShowDialog(this)!=true)return;
            string output=Path.Combine(d.FolderName,"画面标记_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            string? mark=null;int completed=0;
            try
            {
                _cancel=new();choose.IsEnabled=false;main.SetMediaBusy(true);
                var bitmap=AnnotationWindow.Render(new AnnotationDocument(),main.Options,1280,720);
                mark=Path.Combine(main.DataDirectory,"batch-mark-"+Guid.NewGuid()+".png");
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(bitmap.Width,bitmap.Height,96,96,PixelFormats.Bgra32,null,bitmap.Pixels,bitmap.Width*4)));using(var stream=File.Create(mark))encoder.Save(stream);
                var done=await new MediaJobs(App.Log).BatchMarkAsync(_files,output,mark,_cancel.Token,new Progress<string>(s=>status.Text=s));completed=done.Files.Count;
                status.Text=$"已完成{completed}个，失败{done.Errors.Count}个：{output}"+(done.Errors.Count>0?"\n"+string.Join("\n",done.Errors.Select(e=>e.Length>180?e[..180]+"…":e)):"");
            }
            catch(OperationCanceledException){completed=Directory.Exists(output)?Directory.GetFiles(output,"*.mp4").Length:0;status.Text=$"已取消，保留已完成的{completed}个结果；原件未改动。";}
            catch(Exception e){completed=Directory.Exists(output)?Directory.GetFiles(output,"*.mp4").Length:0;status.Text=$"已完成{completed}个，后续失败：{e.Message}";App.Log.Write("BATCH_FAILED",error:e);}
            finally{_cancel?.Dispose();_cancel=null;choose.IsEnabled=true;main.SetMediaBusy(false);if(mark is not null&&File.Exists(mark))File.Delete(mark);}
        },"BatchExport");
        UiForms.Button(panel,"取消剩余任务",()=>_cancel?.Cancel());
        Closing+=(_,e)=>{if(_cancel is not null){e.Cancel=true;_cancel.Cancel();status.Text="正在取消，请稍后关闭。";}};
    }
}

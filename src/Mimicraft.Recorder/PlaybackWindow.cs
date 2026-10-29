using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mimicraft.Recorder;

public sealed class PlaybackWindow:Window
{
    public PlaybackWindow(string path)
    {
        Title="Mimicraft · 静音播放";Width=1000;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var media=new MediaElement{LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,IsMuted=true,Volume=0,Stretch=Stretch.Uniform};
        System.Windows.Automation.AutomationProperties.SetAutomationId(media,"MutedPlayer");
        grid.Children.Add(media);
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(16)};Grid.SetRow(bar,1);
        var label=new TextBlock{Text="静音播放 · "+System.IO.Path.GetFileName(path),VerticalAlignment=VerticalAlignment.Center};
        var restart=new Button{Content="从头播放"};restart.Click+=(_,_)=>{media.Position=TimeSpan.Zero;media.Play();};
        var pause=new Button{Content="暂停 / 继续"};bool paused=false;pause.Click+=(_,_)=>{if(paused)media.Play();else media.Pause();paused=!paused;};
        bar.Children.Add(restart);bar.Children.Add(pause);bar.Children.Add(label);grid.Children.Add(bar);Content=grid;
        var sound=new CheckBox{Content="启用本窗口声音",Margin=new(14,0,0,0),VerticalAlignment=VerticalAlignment.Center};System.Windows.Automation.AutomationProperties.SetAutomationId(sound,"PlayerAudio");bar.Children.Add(sound);sound.Checked+=(_,_)=>{media.Volume=.25;media.IsMuted=false;Title="Mimicraft · 播放";};sound.Unchecked+=(_,_)=>{media.IsMuted=true;media.Volume=0;Title="Mimicraft · 静音播放";};
        media.MediaOpened+=(_,_)=>App.Log.Write("MUTED_PLAYBACK_OPENED",new{isMuted=media.IsMuted,volume=media.Volume,naturalWidth=media.NaturalVideoWidth,naturalHeight=media.NaturalVideoHeight});
        media.MediaEnded+=(_,_)=>App.Log.Write("MUTED_PLAYBACK_ENDED");
        media.MediaFailed+=(_,e)=>{App.Log.Write("PLAYBACK_FAILED",error:e.ErrorException);label.Text="无法播放："+e.ErrorException.Message;};
        Loaded+=(_,_)=>{media.Source=new Uri(path);media.Play();};Closed+=(_,_)=>media.Close();
    }
}

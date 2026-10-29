using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class AudioWindow:Window
{
    public AudioWindow(MainViewModel vm)
    {
        Title="Mimicraft · 声音与音频";Width=570;Height=740;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new(24)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var settings=vm.Options.Audio;
        var mode=UiForms.Select(panel,"录制声音",["不录声音","系统声音","麦克风","系统声音 + 麦克风"],Array.IndexOf(new[]{"none","system","microphone","both"},settings.Mode));System.Windows.Automation.AutomationProperties.SetAutomationId(mode,"AudioMode");
        AudioDevice[] system=[new("","跟随默认播放设备"),..WasapiAudioSession.Devices(false)],microphones=[new("","跟随默认麦克风"),..WasapiAudioSession.Devices(true)];
        var output=UiForms.Select(panel,"系统声音设备",system.Select(d=>d.Name).ToArray(),Math.Max(0,Array.FindIndex(system,d=>d.Id==settings.SystemDevice)));
        var input=UiForms.Select(panel,"麦克风设备",microphones.Select(d=>d.Name).ToArray(),Math.Max(0,Array.FindIndex(microphones,d=>d.Id==settings.MicrophoneDevice)));
        var systemGain=UiForms.Field(panel,"系统声增益（0=此路静音，1=原声，最大4）",settings.SystemGain.ToString(CultureInfo.InvariantCulture),"SystemGain");
        var micGain=UiForms.Field(panel,"麦克风增益（0～4）",settings.MicrophoneGain.ToString(CultureInfo.InvariantCulture),"MicGain");
        var rate=UiForms.Select(panel,"导出采样率",["44100","48000"],settings.SampleRate==44100?0:1);
        var bitrate=UiForms.Select(panel,"音频码率 / kbps",["64","128","192","256"],Array.IndexOf(new[]{64,128,192,256},settings.BitrateKbps));
        var enhance=new CheckBox{Content="麦克风增强：高通滤除低频振动",IsChecked=settings.Enhance,Margin=new(0,10,0,10)};panel.Children.Add(enhance);
        var denoise=new CheckBox{Content="降噪：轻度噪声门与FFT降噪（可能影响细节）",IsChecked=settings.Denoise,Margin=new(0,0,0,15)};panel.Children.Add(denoise);
        var threshold=UiForms.Field(panel,"麦克风噪声门阈值（0～0.05）",settings.NoiseThreshold.ToString(CultureInfo.InvariantCulture));
        var reduction=UiForms.Field(panel,"FFT降噪强度 / dB（3～24，处理合成音轨）",settings.ReductionDb.ToString(CultureInfo.InvariantCulture));
        var only=new CheckBox{Content="仅录音频（不采集屏幕）",IsChecked=settings.AudioOnly};System.Windows.Automation.AutomationProperties.SetAutomationId(only,"AudioOnly");panel.Children.Add(only);
        var format=UiForms.Select(panel,"纯音频格式",["wav","m4a","mp3"],Array.IndexOf(new[]{"wav","m4a","mp3"},settings.Format));
        panel.Children.Add(new TextBlock{Text="只改变应用内音量，不改变系统总音量。选择声音后，开始录制会使用相应设备；设备异常会停止会话并保留临时材料。",TextWrapping=TextWrapping.Wrap});
        UiForms.Button(panel,"保存声音设置",()=>
        {
            try
            {
                var next=new AudioSettings(new[]{"none","system","microphone","both"}[mode.SelectedIndex],system[output.SelectedIndex].Id,microphones[input.SelectedIndex].Id,double.Parse(systemGain.Text,CultureInfo.InvariantCulture),double.Parse(micGain.Text,CultureInfo.InvariantCulture),int.Parse((string)rate.SelectedItem),int.Parse((string)bitrate.SelectedItem),enhance.IsChecked==true,denoise.IsChecked==true,only.IsChecked==true,(string)format.SelectedItem,double.Parse(threshold.Text,CultureInfo.InvariantCulture),double.Parse(reduction.Text,CultureInfo.InvariantCulture));next.Validate();vm.Options.Audio=next;vm.ApplyOptions();vm.SetStatus("声音设置已保存，下次录制使用。");DialogResult=true;
            }
            catch(Exception e){UiForms.Error(this,e);}
        },"SaveAudio");
    }
}

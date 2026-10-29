using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;

namespace Mimicraft.Recorder;

public sealed class PracticeWindow:Window
{
    private readonly Pattern _pattern=new();
    private NAudio.Wave.WasapiOut? _tone;
    public PracticeWindow()
    {
        Title="Mimicraft · 动态练习画面（无声）";Width=1024;Height=650;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Content=_pattern;Closed+=(_,_)=>{_tone?.Stop();_tone?.Dispose();_tone=null;};
    }
    public void PlayAvMarker()
    {
        _tone?.Stop();_tone?.Dispose();_tone=new(NAudio.CoreAudioApi.AudioClientShareMode.Shared,50);_tone.Init(new NAudio.Wave.SampleProviders.SignalGenerator(48000,2){Frequency=880,Gain=.015,Type=NAudio.Wave.SampleProviders.SignalGeneratorType.Sin});_pattern.Flash();_tone.Play();
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};timer.Tick+=(_,_)=>{timer.Stop();_tone?.Stop();};timer.Start();App.Log.Write("AV_MARKER_TRIGGERED",new{frequency=880,gain=.015,durationMs=500});
    }
    private sealed class Pattern:FrameworkElement
    {
        private readonly Stopwatch _clock=Stopwatch.StartNew();
        private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(33)};
        private double _flashUntil;
        public void Flash(){_flashUntil=_clock.Elapsed.TotalSeconds+.5;InvalidateVisual();}
        public Pattern(){_timer.Tick+=(_,_)=>InvalidateVisual();Loaded+=(_,_)=>_timer.Start();Unloaded+=(_,_)=>_timer.Stop();}
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);var seconds=_clock.Elapsed.TotalSeconds;
            dc.DrawRectangle(seconds<_flashUntil?Brushes.White:new SolidColorBrush(Color.FromRgb(29,59,48)),null,new Rect(RenderSize));
            Text(dc,"MIMICRAFT / PRACTICE",24,40,40,Color.FromRgb(163,193,164));
            Text(dc,"用一段画面，检验一次记录。",38,40,100,Colors.White);
            Text(dc,$"{_clock.Elapsed:hh\\:mm\\:ss\\.fff}",62,40,185,Color.FromRgb(243,219,163));
            for(int i=0;i<8;i++)dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)(80+i*15),(byte)(125+i*9),(byte)(100+i*10))),null,new Rect(40+i*90,330,76,80));
            var x=40+Math.Max(0,ActualWidth-140)*(0.5+0.5*Math.Sin(seconds));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(232,160,97)),null,new Point(x,470),27,27);
            Text(dc,"原创测试图 · 动态计时与运动标记 · 不播放声音",17,40,545,Color.FromRgb(170,195,172));
        }
        private void Text(DrawingContext dc,string text,double size,double x,double y,Color color)=>dc.DrawText(new FormattedText(text,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,new SolidColorBrush(color),VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
    }
}

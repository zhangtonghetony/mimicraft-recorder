using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class RecordingEffects:IDisposable
{
    private readonly MainViewModel _vm;private readonly HookProc _mouseProc,_keyProc;private nint _mouseHook,_keyHook;
    private readonly List<(int X,int Y,bool Right,DateTimeOffset At)> _clicks=[];private readonly object _gate=new();
    private OverlayBitmap? _keyImage;private DateTimeOffset _keyUntil;private byte[]? _original;
    private volatile bool _spotlight,_magnifier;
    public bool Spotlight { get=>_spotlight;set{_spotlight=value;App.Log.Write("EFFECT_CHANGED",new{effect="spotlight",enabled=value});} }
    public bool Magnifier { get=>_magnifier;set{_magnifier=value;App.Log.Write("EFFECT_CHANGED",new{effect="magnifier",enabled=value});} }
    public bool MouseClicks { get;private set; }public bool Keys { get;private set; }
    public DateTimeOffset? CountdownEnd { get;set; }public DateTimeOffset? StopwatchStart { get;set; }
    private string _clockText="";private OverlayBitmap? _clockImage;
    public string ClockLabel=>_clockText;
    public RecordingEffects(MainViewModel vm){_vm=vm;_mouseProc=MouseCallback;_keyProc=KeyCallback;}
    public void SetInput(bool clicks,bool keys)
    {
        RemoveHooks();MouseClicks=clicks;Keys=keys;
        if(!clicks)lock(_gate)_clicks.Clear();
        if(!keys){_keyImage=null;_keyUntil=DateTimeOffset.MinValue;}
        if(clicks)_mouseHook=SetWindowsHookEx(14,_mouseProc,GetModuleHandle(null),0);
        if(keys)_keyHook=SetWindowsHookEx(13,_keyProc,GetModuleHandle(null),0);
        if(clicks&&_mouseHook==0||keys&&_keyHook==0){RemoveHooks();MouseClicks=Keys=false;throw new IOException("输入效果监听未能初始化。");}
    }
    public void Tick()
    {
        if(CountdownEnd is {} end)_clockText="倒计时 "+TimeSpan.FromSeconds(Math.Max(0,Math.Ceiling((end-DateTimeOffset.Now).TotalSeconds))).ToString(@"hh\:mm\:ss");
        else if(StopwatchStart is {} start)_clockText="计时 "+(DateTimeOffset.Now-start).ToString(@"hh\:mm\:ss");else{_clockImage=null;_clockText="";return;}
        _clockImage=Text(_clockText);
    }
    public void Compose(byte[] frame,int width,int height)
    {
        if(_vm.SelectedSource is not {} source)return;var bounds=DesktopSources.Bounds(source);GetCursorPos(out var cursor);int x=cursor.X-bounds.X,y=cursor.Y-bounds.Y;
        if(Spotlight)
        {
            for(int py=0;py<height;py++)for(int px=0;px<width;px++)if((px-x)*(px-x)+(py-y)*(py-y)>140*140){int p=(py*width+px)*4;frame[p]=(byte)(frame[p]*.28);frame[p+1]=(byte)(frame[p+1]*.28);frame[p+2]=(byte)(frame[p+2]*.28);}
        }
        if(Magnifier)
        {
            if(_original?.Length!=frame.Length)_original=new byte[frame.Length];Buffer.BlockCopy(frame,0,_original,0,frame.Length);
            const int size=360,half=size/2;
            int left=Math.Clamp(x-half,0,Math.Max(0,width-size)),top=Math.Clamp(y-half,0,Math.Max(0,height-size));
            for(int py=0;py<size&&top+py<height;py++)for(int px=0;px<size&&left+px<width;px++){int sx=Math.Clamp(x+(px-half)/2,0,width-1),sy=Math.Clamp(y+(py-half)/2,0,height-1);int p=((top+py)*width+left+px)*4,q=(sy*width+sx)*4;for(int c=0;c<4;c++)frame[p+c]=_original[q+c];}
        }
        lock(_gate)
        {
            _clicks.RemoveAll(c=>(DateTimeOffset.Now-c.At).TotalSeconds>.6);
            if(MouseClicks)foreach(var click in _clicks){int cx=click.X-bounds.X,cy=click.Y-bounds.Y;for(int dy=-24;dy<=24;dy++)for(int dx=-24;dx<=24;dx++){int xx=cx+dx,yy=cy+dy;if(xx<0||yy<0||xx>=width||yy>=height||dx*dx+dy*dy>24*24||dx*dx+dy*dy<18*18)continue;int p=(yy*width+xx)*4;frame[p]=(byte)(click.Right?240:50);frame[p+1]=80;frame[p+2]=(byte)(click.Right?70:255);}}
        }
        if(Keys&&DateTimeOffset.Now<_keyUntil&&_keyImage is {} key)PixelBlit.Draw(frame,width,height,key.Pixels,key.Width,key.Height,new("image","keys",.03,.82,.42,.12));
        if(_clockImage is {} clock)PixelBlit.Draw(frame,width,height,clock.Pixels,clock.Width,clock.Height,new("image","timer",.56,.03,.4,.1));
    }
    private nint MouseCallback(int code,nint message,nint data)
    {
        if(code>=0&&_vm.Session==RecordingState.Recording&&((int)message is 0x201 or 0x204)){var click=Marshal.PtrToStructure<MouseData>(data);lock(_gate){if(_clicks.Count>=8)_clicks.RemoveAt(0);_clicks.Add((click.Point.X,click.Point.Y,(int)message==0x204,DateTimeOffset.Now));}}
        return CallNextHookEx(0,code,message,data);
    }
    private nint KeyCallback(int code,nint message,nint data)
    {
        if(code>=0&&_vm.Session==RecordingState.Recording&&((int)message is 0x100 or 0x104))
        {
            uint key=(uint)Marshal.ReadInt32(data);bool ctrl=(GetAsyncKeyState(0x11)&0x8000)!=0,alt=(GetAsyncKeyState(0x12)&0x8000)!=0,shift=(GetAsyncKeyState(0x10)&0x8000)!=0;
            if(key is not (0x10 or 0x11 or 0x12 or 0x5b or 0x5c)&&((ctrl||alt)||key is >=0x70 and <=0x7b))
            {
                string label=(ctrl?"Ctrl + ":"")+(alt?"Alt + ":"")+(shift?"Shift + ":"")+KeyInterop.KeyFromVirtualKey((int)key);_keyImage=Text(label);_keyUntil=DateTimeOffset.Now.AddSeconds(1.2);
            }
        }
        return CallNextHookEx(0,code,message,data);
    }
    private static OverlayBitmap Text(string value)
    {
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210,20,35,30)),null,new Rect(0,0,500,90),10,10);dc.DrawText(new FormattedText(value,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),30,Brushes.White,1),new(18,20));}
        var bmp=new RenderTargetBitmap(500,90,96,96,PixelFormats.Pbgra32);bmp.Render(drawing);var pixels=new byte[500*90*4];bmp.CopyPixels(pixels,2000,0);for(int p=0;p<pixels.Length;p+=4){int a=pixels[p+3];if(a is >0 and <255)for(int c=0;c<3;c++)pixels[p+c]=(byte)Math.Min(255,pixels[p+c]*255/a);}return new(500,90,pixels);
    }
    private void RemoveHooks(){if(_mouseHook!=0)UnhookWindowsHookEx(_mouseHook);if(_keyHook!=0)UnhookWindowsHookEx(_keyHook);_mouseHook=_keyHook=0;}
    public void Dispose()=>RemoveHooks();
    private delegate nint HookProc(int code,nint w,nint l);
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct MouseData{public Point Point;public uint Data,Flags,Time;public nuint Extra;}
    [DllImport("user32.dll")]private static extern nint SetWindowsHookEx(int id,HookProc callback,nint module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nint w,nint l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class RegionMoveWindow:Window
{
    public RegionMoveWindow(MainViewModel vm,CaptureSource source,PixelRegion region)
    {
        var bounds=DesktopSources.Bounds(source);Title="Mimicraft · 移动取景框";WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=false;
        Content=new Border{BorderBrush=Brushes.Orange,BorderThickness=new(2),Child=new TextBlock{Text="拖动此条移动取景框 · Esc关闭",Background=Brushes.DarkGreen,Foreground=Brushes.White,Height=28,VerticalAlignment=VerticalAlignment.Top,Padding=new(7)}};
        SourceInitialized+=(_,_)=>{var hwnd=new WindowInteropHelper(this).Handle;DesktopSources.Exclude(hwnd);SetWindowPos(hwnd,new(-1),bounds.X+region.X,bounds.Y+region.Y,region.Width,region.Height,0x40);};
        MouseLeftButtonDown+=(_,_)=>
        {
            try
            {
                DragMove();GetWindowRect(new WindowInteropHelper(this).Handle,out var rect);
                int x=Math.Clamp(rect.Left-bounds.X,0,bounds.Width-region.Width),y=Math.Clamp(rect.Top-bounds.Y,0,bounds.Height-region.Height);
                vm.MoveRegion(new(x,y,region.Width,region.Height));SetWindowPos(new WindowInteropHelper(this).Handle,new(-1),bounds.X+x,bounds.Y+y,region.Width,region.Height,0x40);
            }
            catch(Exception error){UiForms.Error(this,error);}
        };
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)Close();};
    }
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
}

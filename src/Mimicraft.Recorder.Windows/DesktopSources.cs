using System.Runtime.InteropServices;
using System.Text;
using Mimicraft.Recorder.Core;

namespace Mimicraft.Recorder.Windows;

public static class DesktopSources
{
    public static IReadOnlyList<CaptureSource> GetWindows()
    {
        var list=new List<CaptureSource>();
        EnumWindows((hwnd,_)=>
        {
            if(!IsWindowVisible(hwnd)||IsIconic(hwnd)||GetWindowTextLength(hwnd)==0)return true;
            GetWindowThreadProcessId(hwnd,out var pid);
            if(pid==Environment.ProcessId)return true;
            var title=new StringBuilder(1024);GetWindowText(hwnd,title,title.Capacity);
            GetWindowRect(hwnd,out var r);
            if(r.Right-r.Left>=16&&r.Bottom-r.Top>=16)list.Add(new(title.ToString(),hwnd,true));
            return true;
        },0);
        return list;
    }
    public static PixelRegion Bounds(CaptureSource source)
    {
        Rect r;
        if(source.IsWindow)
        {
            if(DwmGetWindowAttribute(source.Handle,9,out r,Marshal.SizeOf<Rect>())!=0&&!GetWindowRect(source.Handle,out r))throw new IOException("窗口已关闭。");
        }
        else
        {
            var m=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            if(!GetMonitorInfo(source.Handle,ref m))throw new IOException("显示器已断开。");r=m.Monitor;
        }
        // Desktop origins may be negative; PixelRegion validation is used only for source-relative crops.
        return new(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);
    }
    public static bool Exclude(nint hwnd)=>SetWindowDisplayAffinity(hwnd,0x11);
    private delegate bool EnumCallback(nint hwnd,nint param);
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumCallback callback,nint param);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")]private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(nint hwnd,StringBuilder text,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint hwnd,out int pid);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(nint hwnd,uint attr,out Rect rect,int size);
    [DllImport("user32.dll")]private static extern bool SetWindowDisplayAffinity(nint hwnd,uint affinity);
}

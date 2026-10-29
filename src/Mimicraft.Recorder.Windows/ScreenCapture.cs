using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinRT;

namespace Mimicraft.Recorder.Windows;

public sealed record CaptureSource(string Name, nint Handle, bool IsWindow = false);

public sealed class ScreenCapture : IDisposable
{
    private readonly object _gate=new();
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11Texture2D? _staging;
    private IDirect3DDevice? _winrtDevice;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private byte[]? _latest;
    private bool _disposed;
    private readonly TaskCompletionSource _first=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _failure;
    private CaptureSource? _source;
    private uint _sourceProcess;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public long CapturedFrames { get; private set; }
    public Task FirstFrame => _first.Task;

    public static IReadOnlyList<CaptureSource> GetMonitors()
    {
        var sources=new List<CaptureSource>();
        MonitorEnum callback=(monitor,_,_,_)=> { sources.Add(new($"显示器 {sources.Count+1}",monitor));return true; };
        EnumDisplayMonitors(0,0,callback,0);
        return sources;
    }

    public void Start(CaptureSource source)
    {
        _source=source;
        if(source.IsWindow)GetWindowThreadProcessId(source.Handle,out _sourceProcess);
        if(!GraphicsCaptureSession.IsSupported())throw new NotSupportedException("WGC_UNSUPPORTED：当前桌面不支持Windows屏幕捕获。");
        _item=CreateItem(source);
        Width=_item.Size.Width;Height=_item.Size.Height;
        if(Width<1||Height<1)throw new IOException("SOURCE_EMPTY：来源窗口没有可录制画面。");
        Vortice.Direct3D11.D3D11.D3D11CreateDevice(null,DriverType.Hardware,DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_0],out _device,out _context).CheckError();
        using var dxgi=_device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer,out var abi));
        try { _winrtDevice=MarshalInterface<IDirect3DDevice>.FromAbi(abi); } finally { Marshal.Release(abi); }
        _pool=Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,new SizeInt32(Width,Height));
        _pool.FrameArrived+=OnFrame;
        _item.Closed+=OnClosed;
        _session=_pool.CreateCaptureSession(_item);
        _session.StartCapture();
    }

    private void OnClosed(GraphicsCaptureItem sender,object args) { lock(_gate){ _failure=new IOException("SOURCE_CLOSED：录制来源已经关闭。"); _first.TrySetException(_failure); } }

    private void OnFrame(Direct3D11CaptureFramePool sender,object args)
    {
        lock(_gate)
        {
            if(_disposed)return;
            try
            {
                using var frame=sender.TryGetNextFrame();
                if(frame is null)return;
                if(frame.ContentSize.Width!=Width||frame.ContentSize.Height!=Height)
                    throw new IOException("SOURCE_RESIZED：来源尺寸改变，请停止后重新录制。");
                // Query IDirect3DDxgiInterfaceAccess without a runtime COM wrapper.
                var surfaceRef=MarshalInspectable<IDirect3DSurface>.FromManaged(frame.Surface);
                var accessId=new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
                nint access;
                try { Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfaceRef,in accessId,out access)); }
                finally { Marshal.Release(surfaceRef); }
                nint texture=0;
                try
                {
                    var function=Marshal.GetDelegateForFunctionPointer<GetInterface>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(access),3*IntPtr.Size));
                    var textureId=typeof(ID3D11Texture2D).GUID;
                    Marshal.ThrowExceptionForHR(function(access,ref textureId,out texture));
                }
                finally { Marshal.Release(access); }
                using var native=new ID3D11Texture2D(texture);
                if(_staging is null)
                {
                    var desc=native.Description; desc.Usage=ResourceUsage.Staging;desc.BindFlags=BindFlags.None;desc.CPUAccessFlags=CpuAccessFlags.Read;desc.MiscFlags=ResourceOptionFlags.None;
                    _staging=_device!.CreateTexture2D(desc);
                }
                _context!.CopyResource(_staging,native);
                var mapped=_context.Map(_staging,0,MapMode.Read,Vortice.Direct3D11.MapFlags.None);
                try
                {
                    _latest ??=new byte[checked(Width*Height*4)];
                    for(int y=0;y<Height;y++)Marshal.Copy(mapped.DataPointer+(int)(y*mapped.RowPitch),_latest,y*Width*4,Width*4);
                }
                finally { _context.Unmap(_staging,0); }
                CapturedFrames++;_first.TrySetResult();
            }
            catch(Exception e) { _failure=e;_first.TrySetException(e); }
        }
    }

    public bool CopyLatest(byte[] target)
    {
        lock(_gate)
        {
            if(_source is {IsWindow:true} source)
            {
                GetWindowThreadProcessId(source.Handle,out var owner);
                if(!IsWindow(source.Handle)||owner!=_sourceProcess)throw new IOException("SOURCE_CLOSED：录制来源已经关闭。");
            }
            if(_failure is not null)throw new IOException("CAPTURE_FAILED："+_failure.Message,_failure);
            if(_latest is null)return false;
            _latest.CopyTo(target,0);return true;
        }
    }

    [DllImport("user32.dll")]private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint hwnd,out uint process);

    public void Dispose()
    {
        // Detach before closing to avoid reentry from a driver callback.
        lock(_gate){if(_disposed)return;_disposed=true;}
        if(_pool is not null)_pool.FrameArrived-=OnFrame;
        if(_item is not null)_item.Closed-=OnClosed;
        _session?.Dispose();_pool?.Dispose();
        lock(_gate){_staging?.Dispose();_context?.Dispose();_device?.Dispose();_latest=null;}
        (_winrtDevice as IDisposable)?.Dispose();
    }

    private static GraphicsCaptureItem CreateItem(CaptureSource source)
    {
        var name="Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name,name.Length,out var hstring));
        nint factory=0,item=0;
        try
        {
            var iid=new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring,ref iid,out factory));
            int slot=source.IsWindow?3:4;
            var create=Marshal.GetDelegateForFunctionPointer<CreateCapture>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory),slot*IntPtr.Size));
            var itemId=new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            Marshal.ThrowExceptionForHR(create(factory,source.Handle,ref itemId,out item));
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally { if(item!=0)Marshal.Release(item);if(factory!=0)Marshal.Release(factory);WindowsDeleteString(hstring); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateCapture(nint self,nint source,ref Guid iid,out nint item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInterface(nint self,ref Guid iid,out nint obj);
    private delegate bool MonitorEnum(nint monitor,nint hdc,nint rect,nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc,nint clip,MonitorEnum callback,nint data);
    [DllImport("combase.dll",CharSet=CharSet.Unicode)] private static extern int WindowsCreateString(string value,int length,out nint result);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name,ref Guid iid,out nint result);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device,out nint result);
}

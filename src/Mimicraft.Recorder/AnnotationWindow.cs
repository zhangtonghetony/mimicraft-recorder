using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class AnnotationWindow:Window
{
    private readonly AnnotationSurface _surface;
    private readonly PixelRegion _bounds;
    public AnnotationWindow(AnnotationDocument document,RecorderOptions options,PixelRegion bounds)
    {
        _bounds=bounds;Title="Mimicraft · 屏幕批注";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;
        _surface=new(document,options,bounds.Width,bounds.Height);Content=_surface;
        SourceInitialized+=(_,_)=>
        {
            var hwnd=new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd,new(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x40);
            if(!DesktopSources.Exclude(hwnd))App.Log.Write("ANNOTATION_CAPTURE_EXCLUSION_UNAVAILABLE");
        };
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
    }
    public void SetTool(string tool,string color,double width,string text)
    {
        _surface.SetTool(tool,color,width,text);
        var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==0)return;
        long style=GetWindowLongPtr(hwnd,-20).ToInt64();style=tool=="穿透"?style|0x20:style&~0x20L;SetWindowLongPtr(hwnd,-20,new(style));
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);

    public static OverlayBitmap Render(AnnotationDocument document,RecorderOptions options,int width,int height)
    {
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())Draw(dc,document,options,width,height);
        var bmp=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bmp.Render(visual);
        var pixels=new byte[checked(width*height*4)];bmp.CopyPixels(pixels,width*4,0);
        // WPF renders premultiplied alpha, while the frame compositor expects straight alpha.
        for(int i=0;i<pixels.Length;i+=4){int a=pixels[i+3];if(a is >0 and <255)for(int c=0;c<3;c++)pixels[i+c]=(byte)Math.Min(255,(pixels[i+c]*255+a/2)/a);}
        return new(width,height,pixels);
    }
    private static void Draw(DrawingContext dc,AnnotationDocument document,RecorderOptions options,double width,double height,Annotation? preview=null,int movingIndex=-1,InkPoint? offset=null)
    {
        dc.PushOpacity(options.MarkOpacity);
        double markX=width*options.MarkX,markY=height*options.MarkY;
        if(System.IO.File.Exists(options.WatermarkImage))
        {
            try
            {
                var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(options.WatermarkImage);image.EndInit();image.Freeze();
                double w=Math.Min(width,options.MarkSize*5),h=w*image.PixelHeight/image.PixelWidth;
                if(h>height){w*=height/h;h=height;}
                double x=Math.Clamp(markX,0,Math.Max(0,width-w)),y=Math.Clamp(markY,0,Math.Max(0,height-h));dc.DrawImage(image,new(x,y,w,h));markY=y+h+8;
            }
            catch(Exception e){App.Log.Write("WATERMARK_IMAGE_UNREADABLE",error:e);}
        }
        if(!string.IsNullOrWhiteSpace(options.WatermarkText))
        {
            var text=new FormattedText(options.WatermarkText,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),options.MarkSize,Brushes.White,1);
            text.MaxTextWidth=Math.Max(1,width-12);double x=Math.Clamp(markX,6,Math.Max(6,width-text.Width-6)),y=Math.Clamp(markY,3,Math.Max(3,height-text.Height-3));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(100,0,0,0)),null,new Rect(x-5,y-3,text.Width+10,text.Height+6));
            dc.DrawText(text,new(x,y));
        }
        dc.Pop();
        var items=document.Items.Select((a,i)=>i==movingIndex&&offset is {} delta?a with {Points=a.Points.Select(p=>new InkPoint(p.X+delta.X,p.Y+delta.Y)).ToArray()}:a);
        foreach(var a in preview is null?items:items.Append(preview))
        {
            if(a.Points.Length==0)continue;
            var color=(Color)ColorConverter.ConvertFromString(a.Color);if(a.Tool=="荧光")color.A=90;
            var brush=new SolidColorBrush(color);var pen=new Pen(brush,a.Width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
            Point p=new(a.Points[0].X*width,a.Points[0].Y*height),q=new(a.Points[^1].X*width,a.Points[^1].Y*height);
            var rect=new Rect(p,q);
            switch(a.Tool)
            {
                case "贴图":try{var img=new BitmapImage();img.BeginInit();img.CacheOption=BitmapCacheOption.OnLoad;img.UriSource=new Uri(a.Text);img.EndInit();dc.DrawImage(img,rect);}catch(Exception e){App.Log.Write("STAMP_UNREADABLE",error:e);}break;
                case "矩形":dc.DrawRectangle(null,pen,rect);break;
                case "圆":dc.DrawEllipse(null,pen,new((p.X+q.X)/2,(p.Y+q.Y)/2),rect.Width/2,rect.Height/2);break;
                case "文字":dc.DrawText(new FormattedText(a.Text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),a.Width*5,brush,1),p);break;
                case "线":case "箭头":
                    dc.DrawLine(pen,p,q);
                    if(a.Tool=="箭头"){var v=p-q;if(v.Length>0){v.Normalize();var normal=new Vector(-v.Y,v.X);dc.DrawLine(pen,q,q+v*18+normal*9);dc.DrawLine(pen,q,q+v*18-normal*9);}}
                    break;
                default:
                    if(a.Points.Length==1)dc.DrawEllipse(brush,null,p,a.Width/2,a.Width/2);
                    for(int i=1;i<a.Points.Length;i++)dc.DrawLine(pen,new(a.Points[i-1].X*width,a.Points[i-1].Y*height),new(a.Points[i].X*width,a.Points[i].Y*height));break;
            }
        }
    }
    private sealed class AnnotationSurface:FrameworkElement
    {
        private readonly AnnotationDocument _document;private readonly RecorderOptions _options;private readonly double _width,_height;
        private string _tool="笔",_color="#FF394A",_text="重点";private double _lineWidth=4;
        private readonly List<InkPoint> _points=[];private int _selected=-1;private InkPoint? _origin;
        public AnnotationSurface(AnnotationDocument doc,RecorderOptions options,double width,double height)
        {
            _document=doc;_options=options;_width=width;_height=height;Focusable=true;Cursor=Cursors.Pen;
            doc.Changed+=Changed;Unloaded+=(_,_)=>doc.Changed-=Changed;
            MouseLeftButtonDown+=Down;MouseMove+=Move;MouseLeftButtonUp+=Up;
        }
        private void Changed()=>InvalidateVisual();
        public void SetTool(string tool,string color,double width,string text){if(IsMouseCaptured)ReleaseMouseCapture();_selected=-1;_origin=null;_points.Clear();_tool=tool;_color=color;_lineWidth=width;_text=text;Cursor=tool=="移动"?Cursors.SizeAll:Cursors.Pen;InvalidateVisual();}
        protected override void OnRender(DrawingContext dc)
        {
            // A zero-alpha layered window is click-through at the Win32 hit-test
            // layer, even when WPF hit testing is enabled. Alpha=1 keeps drawing
            // input reachable; the overlay HWND is excluded from capture.
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1,0,0,0)),null,new Rect(RenderSize));
            Annotation? preview=IsMouseCaptured&&_tool is not ("橡皮" or "移动" or "穿透")?new(_tool,_points.ToArray(),_color,_tool=="荧光"?_lineWidth*4:_lineWidth,_text):null;
            InkPoint? offset=_tool=="移动"&&IsMouseCaptured&&_origin is {} origin&&_points.Count>1?new(_points[^1].X-origin.X,_points[^1].Y-origin.Y):null;
            dc.PushTransform(new ScaleTransform(ActualWidth/_width,ActualHeight/_height));Draw(dc,_document,_options,_width,_height,preview,offset is null?-1:_selected,offset);dc.Pop();
        }
        private InkPoint Position(MouseEventArgs e){var p=e.GetPosition(this);return new(Math.Clamp(p.X/ActualWidth,0,1),Math.Clamp(p.Y/ActualHeight,0,1));}
        private void Down(object sender,MouseButtonEventArgs e)
        {
            Focus();CaptureMouse();var p=Position(e);_points.Clear();_points.Add(p);_origin=p;
            if(_tool is "橡皮" or "移动")
            {
                _selected=Enumerable.Range(0,_document.Items.Count).Reverse().FirstOrDefault(i=>Near(_document.Items[i],p),-1);
                if(_tool=="橡皮"&&_selected>=0)_document.Remove(_selected);
            }
        }
        private bool Near(Annotation a,InkPoint p)
        {
            var xs=a.Points.Select(v=>v.X).ToArray();var ys=a.Points.Select(v=>v.Y).ToArray();
            return p.X>=xs.Min()-.02&&p.X<=xs.Max()+.02&&p.Y>=ys.Min()-.02&&p.Y<=ys.Max()+.02;
        }
        private void Move(object sender,MouseEventArgs e)
        {
            if(!IsMouseCaptured||e.LeftButton!=MouseButtonState.Pressed)return;
            if(_tool is "笔" or "荧光"){if(_points.Count<10000)_points.Add(Position(e));}
            else if(_points.Count==1)_points.Add(Position(e));else _points[^1]=Position(e);
            InvalidateVisual();
        }
        private void Up(object sender,MouseButtonEventArgs e)
        {
            if(!IsMouseCaptured)return;ReleaseMouseCapture();var end=Position(e);
            if(_tool=="移动"){if(_selected>=0&&_origin is {} origin)_document.Move(_selected,end.X-origin.X,end.Y-origin.Y);_selected=-1;_origin=null;InvalidateVisual();return;}
            if(_tool=="橡皮")return;
            _points.Add(end);_document.Add(new(_tool,_points.ToArray(),_color,_tool=="荧光"?_lineWidth*4:_lineWidth,_text));
        }
    }
}

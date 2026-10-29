namespace Mimicraft.Recorder.Core;

public sealed record ScenePreset(string Name,string Layout,SceneLayer[] Layers,bool FloatingCamera=true)
{
    public void Validate(){if(string.IsNullOrWhiteSpace(Name)||Name.Length>80||Layout is not ("overlay" or "split")||Layers.Length>8)throw new ArgumentException("场景预设无效。");foreach(var layer in Layers)layer.Validate();}
}

public sealed record SceneLayer(string Kind,string Source,double X=.65,double Y=.65,double Width=.3,double Height=.3,double Opacity=1,bool Visible=true)
{
    public void Validate()
    {
        if(Kind is not ("camera" or "screen" or "image" or "text")||string.IsNullOrWhiteSpace(Source)||!double.IsFinite(X+Y+Width+Height+Opacity)||X<0||Y<0||Width<=0||Height<=0||X+Width>1.001||Y+Height>1.001||Opacity is <0 or >1)throw new ArgumentException("场景层类型或位置/大小无效。");
    }
}
public static class PixelBlit
{
    public static void Draw(byte[] target,int width,int height,byte[] source,int sourceWidth,int sourceHeight,SceneLayer layer)
    {
        layer.Validate();if(target.Length!=width*height*4||source.Length!=sourceWidth*sourceHeight*4)throw new ArgumentException("画面缓冲尺寸错误。");
        int left=(int)(layer.X*width),top=(int)(layer.Y*height),w=Math.Max(1,(int)(layer.Width*width)),h=Math.Max(1,(int)(layer.Height*height));
        var input=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,uint>(source.AsSpan());var output=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,uint>(target.AsSpan());var columns=new int[w];for(int x=0;x<w;x++)columns[x]=x*sourceWidth/w;
        for(int y=0;y<h&&y+top<height;y++)for(int x=0;x<w&&x+left<width;x++)
        {
            int targetPixel=(top+y)*width+left+x,sourcePixel=(y*sourceHeight/h)*sourceWidth+columns[x];uint value=input[sourcePixel];
            if((value>>24)==0)continue;
            if(layer.Opacity==1&&(value>>24)==255){output[targetPixel]=value;continue;}
            int p=targetPixel*4,q=sourcePixel*4;double a=layer.Opacity*source[q+3]/255;
            for(int c=0;c<3;c++)target[p+c]=(byte)(source[q+c]*a+target[p+c]*(1-a));target[p+3]=255;
        }
    }
}

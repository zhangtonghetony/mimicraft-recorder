using System.Windows;
using System.Windows.Controls;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class RegionFieldsWindow:Window
{
    public RegionFieldsWindow(MainViewModel vm)
    {
        Title="Mimicraft · 精确取景";Width=410;Height=490;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new(24)};Content=panel;var region=vm.Options.Region;
        panel.Children.Add(new TextBlock{Text="相对来源左上角的物理像素",TextWrapping=TextWrapping.Wrap});
        var x=UiForms.Field(panel,"X",(region?.X??0).ToString(),"RegionX");var y=UiForms.Field(panel,"Y",(region?.Y??0).ToString(),"RegionY");
        var w=UiForms.Field(panel,"宽",(region?.Width??640).ToString(),"RegionWidth");var h=UiForms.Field(panel,"高",(region?.Height??360).ToString(),"RegionHeight");
        UiForms.Button(panel,"应用范围",()=>
        {
            try
            {
                if(vm.SelectedSource is not {} source)throw new ArgumentException("请先选择来源。");
                var bounds=DesktopSources.Bounds(source);var r=new PixelRegion(int.Parse(x.Text),int.Parse(y.Text),int.Parse(w.Text),int.Parse(h.Text));r.Validate(bounds.Width,bounds.Height);
                vm.Options.Region=r;vm.ApplyOptions();vm.SetStatus($"已选择区域 {r.X},{r.Y} / {r.Width}×{r.Height} 像素。");DialogResult=true;
            }
            catch(Exception e){UiForms.Error(this,e);}
        },"ApplyRegion");
    }
}

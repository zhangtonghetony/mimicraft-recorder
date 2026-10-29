using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Mimicraft.Recorder.Core;
using Mimicraft.Recorder.Windows;

namespace Mimicraft.Recorder;

public sealed class SceneWindow:Window
{
    private readonly ObservableCollection<SceneLayer> _layers;
    public SceneWindow(MainViewModel vm,Func<Task> preview)
    {
        _layers=new(vm.Options.Layers);Title="Mimicraft · 画面布局";Width=700;Height=800;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new(24)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="画面布局",FontSize=26,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock{Text="底图来自主界面所选来源。图层按列表顺序从下到上叠加；摄像头仅在预览或录制时开启。",TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,10)});
        var list=new ListBox{ItemsSource=_layers,Height=130};System.Windows.Automation.AutomationProperties.SetAutomationId(list,"SceneLayers");panel.Children.Add(list);
        var order=new WrapPanel();panel.Children.Add(order);
        UiForms.Button(order,"上移",()=>{int i=list.SelectedIndex;if(i>0)_layers.Move(i,i-1);});UiForms.Button(order,"下移",()=>{int i=list.SelectedIndex;if(i>=0&&i+1<_layers.Count)_layers.Move(i,i+1);});UiForms.Button(order,"移除",()=>{if(list.SelectedItem is SceneLayer layer)_layers.Remove(layer);});
        var layout=UiForms.Select(panel,"布局",["叠加 / 画中画","左右分屏（底图左半）"],vm.Options.Layout=="split"?1:0);
        var kind=UiForms.Select(panel,"新增图层类型",["摄像头","第二画面来源","图片","文字"]);
        var source=UiForms.Field(panel,"来源：摄像头名称 / 来源名称 / 图片路径 / 文字","","SceneSource");
        var choices=new ComboBox{MinWidth=250};panel.Children.Add(choices);
        UiForms.Button(panel,"刷新可选来源",async()=>
        {
            try{choices.ItemsSource=kind.SelectedIndex==0?await CameraCapture.DevicesAsync():ScreenCapture.GetMonitors().Concat(DesktopSources.GetWindows()).Select(s=>s.Name).ToArray();}catch(Exception e){UiForms.Error(this,e);}
        });choices.SelectionChanged+=(_,_)=>{if(choices.SelectedItem is string value)source.Text=value;};
        UiForms.Button(panel,"选择本地图片",()=>{var d=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp"};if(d.ShowDialog(this)==true){kind.SelectedIndex=2;source.Text=d.FileName;}});
        var x=UiForms.Field(panel,"X / Y / 宽 / 高（0～1，分屏建议 0.5 / 0 / 0.5 / 1）","0.65","LayerX");var y=UiForms.Field(panel,"Y","0.65","LayerY");var w=UiForms.Field(panel,"宽","0.3","LayerWidth");var h=UiForms.Field(panel,"高","0.3","LayerHeight");var alpha=UiForms.Field(panel,"透明度（0～1）","1","LayerOpacity");
        var visible=new CheckBox{Content="显示图层",IsChecked=true,Margin=new(0,10,0,8)};panel.Children.Add(visible);
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is not SceneLayer layer)return;kind.SelectedIndex=Array.IndexOf(new[]{"camera","screen","image","text"},layer.Kind);source.Text=layer.Source;x.Text=N(layer.X);y.Text=N(layer.Y);w.Text=N(layer.Width);h.Text=N(layer.Height);alpha.Text=N(layer.Opacity);visible.IsChecked=layer.Visible;};
        SceneLayer Read(){var layer=new SceneLayer(new[]{"camera","screen","image","text"}[kind.SelectedIndex],source.Text,P(x),P(y),P(w),P(h),P(alpha),visible.IsChecked==true);layer.Validate();return layer;}
        var edits=new WrapPanel();panel.Children.Add(edits);UiForms.Button(edits,"新增图层",()=>{try{if(_layers.Count>=8)throw new ArgumentException("最多8层。");_layers.Add(Read());}catch(Exception e){UiForms.Error(this,e);}},"AddLayer");UiForms.Button(edits,"更新选中层",()=>{try{if(list.SelectedIndex>=0)_layers[list.SelectedIndex]=Read();}catch(Exception e){UiForms.Error(this,e);}});
        var floating=new CheckBox{Content="显示摄像头浮窗",IsChecked=vm.Options.FloatingCamera,Margin=new(0,12,0,12)};panel.Children.Add(floating);
        var presets=UiForms.Select(panel,"本地场景预设（加载后应用于下次录制）",vm.Options.Presets.Select(p=>p.Name).ToArray());
        var presetName=UiForms.Field(panel,"预设名称","我的布局");
        UiForms.Button(panel,"载入选中预设",()=>{if(presets.SelectedItem is not string name)return;var preset=vm.Options.Presets.First(p=>p.Name==name);_layers.Clear();foreach(var layer in preset.Layers)_layers.Add(layer);layout.SelectedIndex=preset.Layout=="split"?1:0;floating.IsChecked=preset.FloatingCamera;});
        UiForms.Button(panel,"保存 / 更新同名预设",()=>{try{var preset=new ScenePreset(presetName.Text,layout.SelectedIndex==1?"split":"overlay",_layers.ToArray(),floating.IsChecked==true);preset.Validate();if(vm.Options.Presets.Count>=20&&!vm.Options.Presets.Any(p=>p.Name==preset.Name))throw new ArgumentException("已达20个预设，请先删除不需要的预设。");vm.Options.Presets.RemoveAll(p=>p.Name==preset.Name);vm.Options.Presets.Add(preset);Save();presets.ItemsSource=vm.Options.Presets.Select(p=>p.Name).ToArray();presets.SelectedItem=preset.Name;}catch(Exception e){UiForms.Error(this,e);}});
        UiForms.Button(panel,"删除选中预设",()=>{if(presets.SelectedItem is string name){vm.Options.Presets.RemoveAll(p=>p.Name==name);Save();presets.ItemsSource=vm.Options.Presets.Select(p=>p.Name).ToArray();}});
        void Save(){vm.Options.Layers=_layers.ToList();vm.Options.Layout=layout.SelectedIndex==1?"split":"overlay";vm.Options.FloatingCamera=floating.IsChecked==true;vm.ApplyOptions();}
        UiForms.Button(panel,"保存布局",()=>{try{Save();DialogResult=true;}catch(Exception e){UiForms.Error(this,e);}},"SaveScene");
        UiForms.Button(panel,"保存并开启预览",async()=>{try{Save();await preview();}catch(Exception e){UiForms.Error(this,e);}},"PreviewScene");
    }
    private static double P(TextBox b)=>double.Parse(b.Text,CultureInfo.InvariantCulture);
    private static string N(double n)=>n.ToString(CultureInfo.InvariantCulture);
}

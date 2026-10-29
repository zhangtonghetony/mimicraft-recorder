using System.Windows;
using System.Windows.Controls;

namespace Mimicraft.Recorder;

internal static class UiForms
{
    public static TextBox Field(Panel parent,string label,string value,string id="")
    {
        parent.Children.Add(new TextBlock{Text=label,Margin=new(0,12,0,5),TextWrapping=TextWrapping.Wrap});
        var box=new TextBox{Text=value,Padding=new(8),MinWidth=100};
        if(id!="")System.Windows.Automation.AutomationProperties.SetAutomationId(box,id);
        parent.Children.Add(box);return box;
    }
    public static Button Button(Panel parent,string label,Action action,string id="")
    {
        var button=new Button{Content=label,Margin=new(0,8,6,0)};
        if(id!="")System.Windows.Automation.AutomationProperties.SetAutomationId(button,id);
        button.Click+=(_,_)=>action();parent.Children.Add(button);return button;
    }
    public static ComboBox Select(Panel parent,string label,string[] values,int index=0)
    {
        parent.Children.Add(new TextBlock{Text=label,Margin=new(0,12,0,5),TextWrapping=TextWrapping.Wrap});var combo=new ComboBox{ItemsSource=values,SelectedIndex=index};parent.Children.Add(combo);return combo;
    }
    public static void Error(Window owner,Exception e){App.Log.Write("DIALOG_ERROR",error:e);MessageBox.Show(owner,e.Message,"请检查输入");}
}

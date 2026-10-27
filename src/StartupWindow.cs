using System;
using System.Windows;
using System.Windows.Controls;
namespace DailyDiaryCard;
public sealed partial class MainWindow
{
    public StartupPanel CreateStartupDialog(IStartupStore? store=null)=>new(config,Dialog("开机自启动",610,360),store);
}
public sealed class StartupPanel
{
    private readonly CardConfig config;
    private readonly IStartupStore store;
    public Window Window{get;}
    public CheckBox Choice{get;}=new();
    public Button ApplyButton{get;}=new(){Content="保存设置"};
    public TextBlock Message{get;}=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
    public StartupPanel(CardConfig config,Window window,IStartupStore? store=null)
    {
        this.config=config;Window=window;this.store=store??new WindowsStartupStore();
        var layout=new StackPanel{Margin=new Thickness(24)};
        layout.Children.Add(new TextBlock{Text="按你的习惯启动",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,14)});
        var row=new StackPanel{Orientation=Orientation.Horizontal};Choice.Margin=new Thickness(0,0,7,0);row.Children.Add(Choice);row.Children.Add(new TextBlock{Text="登录 Windows 后自动打开此版本卡片",VerticalAlignment=VerticalAlignment.Center});layout.Children.Add(row);
        System.Windows.Automation.AutomationProperties.SetName(Choice,"开机自启动");
        var note=new TextBlock{Text="默认关闭，仅对当前 Windows 账号生效。两版共用一个启动入口；开启当前版本后，其他版本不会重复启动。移动程序后需在新位置重新开启。",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,12,0,0)};note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");layout.Children.Add(note);layout.Children.Add(Message);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var close=new Button{Content="关闭"};close.Click+=(_,_)=>window.Close();buttons.Children.Add(close);buttons.Children.Add(ApplyButton);layout.Children.Add(buttons);window.Content=layout;
        try{Choice.IsChecked=StartupService.EnabledHere(Environment.ProcessPath!,config.ConfigPath,this.store);Message.Text=Choice.IsChecked==true?"当前版本已开启开机自启动。":this.store.Read()!=null?"当前版本未开启；另一个版本或位置已设置启动，开启此项会切换到当前版本。":"当前未开启，仍由你手动启动。";}catch(Exception e){Message.Text="无法读取启动设置："+e.Message;Message.SetResourceReference(TextBlock.ForegroundProperty,"Danger");}
        ApplyButton.Click+=(_,_)=>Apply();
    }
    public bool Apply()
    {
        try{StartupService.SetEnabled(Environment.ProcessPath!,config.ConfigPath,Choice.IsChecked==true,store);Message.Text=Choice.IsChecked==true?"已开启：下次登录 Windows 时打开当前卡片。":"当前版本的开机自启动已关闭。";Message.SetResourceReference(TextBlock.ForegroundProperty,"Accent");return true;}
        catch(Exception e){Message.Text="设置尚未生效，请重试："+e.Message;Message.SetResourceReference(TextBlock.ForegroundProperty,"Danger");return false;}
    }
}
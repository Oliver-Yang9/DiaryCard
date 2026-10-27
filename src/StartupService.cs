using System;
using System.IO;
using System.Threading;
using Microsoft.Win32;
namespace DailyDiaryCard;

public interface IStartupStore
{
    string? Read();
    void Write(string command);
    void Delete();
}
public sealed class WindowsStartupStore:IStartupStore
{
    private const string RunPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName="DiaryCard.Desktop";
    private readonly string path;
    public WindowsStartupStore():this(RunPath){}
    internal WindowsStartupStore(string testPath)
    {
        if(testPath!=RunPath&&!testPath.StartsWith(@"Software\DiaryCard\Tests\",StringComparison.Ordinal))throw new IOException("启动项测试地址无效。");
        path=testPath;
    }
    public string? Read(){using var key=Registry.CurrentUser.OpenSubKey(path,false);return key?.GetValue(ValueName,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;}
    public void Write(string command){using var key=Registry.CurrentUser.CreateSubKey(path,true)??throw new IOException("无法写入当前用户的启动设置。");key.SetValue(ValueName,command,RegistryValueKind.String);key.Flush();}
    public void Delete(){using var key=Registry.CurrentUser.OpenSubKey(path,true);key?.DeleteValue(ValueName,false);}
}
public static class StartupService
{
    public static string Command(string exe,string config)
    {
        exe=Path.GetFullPath(exe);config=Path.GetFullPath(config);
        if(exe.Contains('"')||config.Contains('"')||exe.Contains('\r')||config.Contains('\n'))throw new IOException("启动地址含无效字符。");
        return "\""+exe+"\" --config \""+config+"\"";
    }
    public static bool EnabledHere(string exe,string config,IStartupStore? store=null)=>string.Equals((store??new WindowsStartupStore()).Read(),Command(exe,config),StringComparison.OrdinalIgnoreCase);
    public static void SetEnabled(string exe,string config,bool enabled,IStartupStore? store=null)
    {
        string command=Command(exe,config);store??=new WindowsStartupStore();
        using var mutex=new Mutex(false,@"Local\DiaryCard.StartupRegistration");
        bool acquired=false;
        try
        {
            try{acquired=mutex.WaitOne(TimeSpan.FromSeconds(3));}catch(AbandonedMutexException){acquired=true;}
            if(!acquired)throw new IOException("启动设置正在被另一张卡片修改，请稍后重试。");
            string? current=store.Read();
            if(enabled){if(!File.Exists(Path.GetFullPath(exe))||!File.Exists(Path.GetFullPath(config)))throw new IOException("请先完成首次设置，再开启开机自启动。");store.Write(command);if(!string.Equals(store.Read(),command,StringComparison.OrdinalIgnoreCase))throw new IOException("启动设置写入后验证失败。");}
            else if(string.Equals(current,command,StringComparison.OrdinalIgnoreCase)){store.Delete();if(string.Equals(store.Read(),command,StringComparison.OrdinalIgnoreCase))throw new IOException("启动设置未能关闭，请重试。");}
        }
        finally{if(acquired)mutex.ReleaseMutex();}
    }
    public static void ApplyFirstChoice(string config,bool requested)
    {
        string exe=Environment.ProcessPath??throw new IOException("无法确定程序位置。");
        if(requested||EnabledHere(exe,config))SetEnabled(exe,config,requested);
    }
    public static System.Windows.FrameworkElement FirstChoiceRow(string config,out System.Windows.Controls.CheckBox choice)
    {
        choice=new(){VerticalAlignment=System.Windows.VerticalAlignment.Center,Margin=new System.Windows.Thickness(0,0,6,0)};
        try{choice.IsChecked=EnabledHere(Environment.ProcessPath!,config);}catch{choice.IsChecked=false;}
        var row=new System.Windows.Controls.StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,Margin=new System.Windows.Thickness(0,5,0,9)};row.Children.Add(choice);
        var text=new System.Windows.Controls.TextBlock{Text="登录 Windows 后自动打开卡片（开机自启动）",VerticalAlignment=System.Windows.VerticalAlignment.Center,TextWrapping=System.Windows.TextWrapping.Wrap,FontSize=12};row.Children.Add(text);
        System.Windows.Automation.AutomationProperties.SetName(choice,"开机自启动");return row;
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DailyDiaryCard;
public static class ModeUiTests
{
    private static void Pump()
    {
        var frame=new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));
        Dispatcher.PushFrame(frame);
    }
    private static void Capture(Window window,string file)
    {
        window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(file);encoder.Save(stream);
    }
    private static IEnumerable<TextBlock> TextBlocks(DependencyObject node)
    {
        if(node is TextBlock text)yield return text;
        for(int index=0;index<VisualTreeHelper.GetChildrenCount(node);index++)
            foreach(var child in TextBlocks(VisualTreeHelper.GetChild(node,index)))yield return child;
    }
    public static int Run(string root)
    {
        root=Path.GetFullPath(root);Directory.CreateDirectory(root);
        var results=new List<string>();Exception? failure=null;
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Themes.Initialize();
        ModeSetupWindow? mode=null;StandaloneSetupWindow? local=null;VaultSetupWindow? obs=null;MainWindow? main=null;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);results.Add(name);}
        string Install(string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
        try
        {
            string first=Install("first-choice"),config=Path.Combine(first,"diary-card.json");
            mode=new ModeSetupWindow(config,_=>true);mode.Show();Pump();
            Check(mode.ObsidianChoice.IsChecked!=true&&mode.StandaloneChoice.IsChecked!=true&&!mode.ContinueButton.IsEnabled,"首次使用不预选模式，必须明确二选一");
            Check(!mode.ContinueSelection()&&!File.Exists(config)&&!Directory.Exists(Path.Combine(first,"Diary"))&&!Directory.Exists(Path.Combine(first,"data")),"未选择不能继续，首屏不预创建文件");
            Check(TextBlocks(mode).Any(t=>t.Text=="使用 Obsidian")&&TextBlocks(mode).Any(t=>t.Text=="独立使用 DiaryCard"),"两种模式的说明实际显示在选择卡片中");
            Capture(mode,Path.Combine(root,"01-choose-mode.png"));
            mode.ObsidianChoice.IsChecked=true;obs=(VaultSetupWindow)mode.CreateSelectedWindow();obs.Show();Pump();
            Check(obs.ExistingChoice.IsChecked==true&&obs.Owner==mode,"Obsidian分支继续使用原V2.02日记库向导");
            obs.Close();obs=null;
            Check(mode.IsVisible&&mode.ContinueButton.IsEnabled&&!mode.Accepted,"子向导取消后保留模式选择窗口");
            mode.StandaloneChoice.IsChecked=true;local=(StandaloneSetupWindow)mode.CreateSelectedWindow();local.Show();Pump();
            Check(local.Owner==mode&&local.UseDefault.IsChecked==true&&local.LibraryInput.Text==Path.Combine(first,"Diary"),"独立分支使用本地资料夹向导和内置复盘模板");
            Check(!TextBlocks(local).Any(t=>t.Text.Contains("Obsidian",StringComparison.OrdinalIgnoreCase)),"独立配置界面不要求Obsidian配置字段");
            Check(local.DefaultTemplateLabel.ActualWidth>40&&!Directory.Exists(local.LibraryInput.Text)&&!File.Exists(config),"独立模板说明可见且预览不创建目录");
            Capture(local,Path.Combine(root,"02-standalone-setup.png"));
            local.Close();local=null;mode.Close();mode=null;
            Check(!File.Exists(config)&&!Directory.Exists(Path.Combine(first,"Diary"))&&!Directory.Exists(Path.Combine(first,"data")),"取消所有向导不创建资料夹或程序数据");

            string invalid=Install("invalid"),invalidConfig=Path.Combine(invalid,"diary-card.json");
            string foreign=Path.Combine(invalid,"已有其他文档");Directory.CreateDirectory(foreign);File.WriteAllText(Path.Combine(foreign,"保留.txt"),"已有文件");
            local=new StandaloneSetupWindow(invalidConfig,_=>true);local.Show();local.LibraryInput.Text=foreign;Pump();
            Check(!local.ApplySelection()&&local.IsVisible&&!local.Accepted&&local.LibraryInput.Text==foreign&&local.ErrorText.Text.Length>0,"非空陌生目录保存被拒绝，保留窗口和原输入");
            Check(File.ReadAllText(Path.Combine(foreign,"保留.txt"))=="已有文件"&&!File.Exists(invalidConfig),"拒绝错误目录时不改已有文件和配置");
            local.Close();local=null;

            string refused=Install("callback-refused"),refusedConfig=Path.Combine(refused,"diary-card.json");int calls=0;
            local=new StandaloneSetupWindow(refusedConfig,_=>{calls++;return false;});local.Show();Pump();
            string refusedPath=local.LibraryInput.Text;
            Check(!local.ApplySelection()&&calls==1&&local.IsVisible&&!local.Accepted&&local.LibraryInput.Text==refusedPath&&local.ErrorText.Text.Length>0,"保存回调失败留在向导并保留资料夹输入");
            Check(!File.Exists(refusedConfig)&&!Directory.Exists(refusedPath),"回调拒绝时没有提前初始化独立资料夹");
            local.Close();local=null;

            string success=Install("success"),successConfig=Path.Combine(success,"diary-card.json");CardConfig? saved=null;
            local=new StandaloneSetupWindow(successConfig,plan=>{saved=SetupService.Complete(plan);return true;});local.Show();Pump();
            Check(local.ApplySelection()&&local.Accepted&&saved?.IsStandalone==true,"独立向导确认后实际创建资料夹并保存模式");
            local=null;var current=CardConfig.Load(successConfig);
            Check(current.IsStandalone&&current.SetupVersion==3&&File.Exists(Path.Combine(current.VaultPath,SetupService.StandaloneMarker))&&!Directory.Exists(Path.Combine(current.VaultPath,".obsidian")),"重读配置保持独立模式且不创建Obsidian目录");
            var today=DateOnly.FromDateTime(DateTime.Now);main=new MainWindow(current,()=>today){WeeklyTimerEnabled=false};main.Show();Pump();
            Check(main.Editors.Count==TemplateReconciler.Parse(DefaultAssets.Template).Count,"独立模式卡片按真实默认模板生成栏目");
            string firstTitle=main.Session.Template[0].Title;
            main.Editors[firstTitle].Append("\n独立模式中文多行\n第二行记录\n- [x] 新版已完成任务\n");
            Check(main.TrySave()&&File.ReadAllText(current.PathFor(today)).Contains("第二行记录")&&File.ReadAllText(current.PathFor(today)).Contains("- [x] 新版已完成任务"),"无需其他应用即可通过卡片保存中文和Markdown复选框");
            var resumed=new DiarySession(CardConfig.Load(successConfig),today);
            Check(resumed.Bodies[firstTitle].Contains("独立模式中文多行"),"重新载入独立当天日记恢复已保存内容");
            main.Capture(Path.Combine(root,"03-standalone-card.png"));main.ExitCard();main=null;

            mode=new ModeSetupWindow(successConfig,_=>true,current,"night");mode.Show();Pump();
            Check(mode.StandaloneChoice.IsChecked==true&&mode.ContinueButton.IsEnabled,"设置中预选已使用模式，不丢失独立模式选择");
            var dark=(SolidColorBrush)mode.Background;
            Check(dark.Color.R<50&&dark.Color.G<50&&dark.Color.B<50&&mode.WindowStyle==WindowStyle.ToolWindow&&mode.ResizeMode!=ResizeMode.CanMinimize,"深色模式选择窗口黑底且没有最小化样式");
            local=(StandaloneSetupWindow)mode.CreateSelectedWindow();local.Show();Pump();
            var localDark=(SolidColorBrush)local.Background;
            Check(local.LibraryInput.Text==current.VaultPath&&localDark.Color.R<50&&local.WindowStyle==WindowStyle.ToolWindow&&local.ResizeMode!=ResizeMode.CanMinimize,"独立设置恢复当前地址并跟随深色窗口样式");
            Capture(local,Path.Combine(root,"04-dark-standalone-settings.png"));local.Close();local=null;mode.Close();mode=null;

            string repair=Install("repair"),repairConfig=Path.Combine(repair,"diary-card.json");const string invalidText="{ broken config";File.WriteAllText(repairConfig,invalidText);
            mode=new ModeSetupWindow(repairConfig,_=>true,startupError:"无法读取旧配置");mode.Show();Pump();
            Check(mode.ErrorText.Text.Contains("无法读取旧配置"),"配置修复首屏清楚展示原配置错误");
            mode.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));mode=null;
            Check(File.ReadAllText(repairConfig)==invalidText,"取消配置修复保留原配置原文");

            string obsInstall=Install("obsidian-flow"),obsConfig=Path.Combine(obsInstall,"diary-card.json");
            mode=new ModeSetupWindow(obsConfig,plan=>{SetupService.Complete(plan);return true;});mode.Show();mode.ObsidianChoice.IsChecked=true;
            obs=(VaultSetupWindow)mode.CreateSelectedWindow();obs.Show();obs.NewChoice.IsChecked=true;Pump();
            Check(obs.ApplySelection()&&obs.Accepted&&CardConfig.Load(obsConfig).StorageMode=="obsidian"&&File.Exists(Path.Combine(obsInstall,"DailyNote",".obsidian","daily-notes.json")),"Obsidian分支仍可按V2.02流程新建标准仓库");
            obs=null;mode.Close();mode=null;
        }
        catch(Exception e){failure=e;}
        finally
        {
            try{local?.Close();obs?.Close();mode?.Close();if(main!=null){main.WeeklyTimerEnabled=false;main.ExitCard();}}catch{}
            DiarySession.AtomicText(Path.Combine(root,"mode-ui-result.json"),JsonSerializer.Serialize(new{passed=failure==null,results,error=failure?.ToString()},CardConfig.Json));
            app.Shutdown();
        }
        return failure==null?0:1;
    }
}
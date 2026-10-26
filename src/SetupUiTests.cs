using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DailyDiaryCard;

public static class SetupUiTests
{
    private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    public static int Run(string root)
    {
        root=Path.GetFullPath(root);Directory.CreateDirectory(root);var results=new List<string>();Exception? failure=null;
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Themes.Initialize();Themes.Apply("fluent");
        void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add(label);}
        MainWindow? main=null;VaultSetupWindow? dialog=null;
        try
        {
            string cancelRoot=Path.Combine(root,"cancel");Directory.CreateDirectory(cancelRoot);var config=Path.Combine(cancelRoot,"diary-card.json");
            dialog=new VaultSetupWindow(config,p=>{SetupService.Complete(p);return true;});dialog.Show();dialog.UpdateLayout();
            Check(dialog.DefaultTemplateLabel.ActualWidth>50&&dialog.DefaultTemplateLabel.Text=="使用内置复盘模板","内置模板复选框说明文字实际呈现，不受任务复选框样式隐藏");
            Check(dialog.IsVisible&&dialog.ExistingChoice.IsChecked==true&&DockNative.SettingsCaptionValid(dialog),"首次向导真实显示，提供已有／新建选择且无最小化按钮");UiSmoke.Capture(dialog,Path.Combine(root,"01-first-use.png"));
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(dialog.IsVisible&&!dialog.Accepted&&dialog.ErrorText.Text.Length>0&&!File.Exists(config),"首次未选择库地址时提示选择，不默用程序目录");
            dialog.NewChoice.IsChecked=true;dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!File.Exists(config)&&!Directory.Exists(Path.Combine(cancelRoot,"DailyNote"))&&!Directory.Exists(Path.Combine(cancelRoot,"data")),"取消首次设置不创建仓库、配置或数据");
            string install=Path.Combine(root,"new-install");Directory.CreateDirectory(install);config=Path.Combine(install,"diary-card.json");CardConfig? current=null;
            dialog=new VaultSetupWindow(config,p=>{current=SetupService.Complete(p);return true;});dialog.Show();dialog.NewChoice.IsChecked=true;dialog.UpdateLayout();UiSmoke.Capture(dialog,Path.Combine(root,"02-create-local-vault.png"));
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(dialog.Accepted&&current!=null&&current.VaultPath==Path.Combine(install,"DailyNote"),"新建选择通过实际保存按钮创建程序目录中的DailyNote");
            main=new MainWindow(current!);main.Show();main.UpdateLayout();Pump();
            Check(main.Editors.Count==TemplateReconciler.Parse(DefaultAssets.Template).Count&&main.HasPictureBackground&&main.BackgroundFile==DefaultAssets.BackgroundName,"初始卡片使用当前默认模板栏目及指定背景");main.Capture(Path.Combine(root,"03-default-card.png"));
            var first=main.Session.Template[0].Title;main.Editors[first].TextInputs[0].AppendText("\n第二版真实控件中文输入\n");main.Editors[first].TaskInputs[0].IsChecked=true;
            Check(main.TrySave()&&DiarySession.ReadText(main.Session.Target).Contains("第二版真实控件中文输入")&&DiarySession.ReadText(main.Session.Target).Contains("- [x]"),"新库中文与复选框由真实编辑控件保存");
            string oldTarget=main.Session.Target;var vault=SetupTests.MakeVault(install,"existing-vault");
            var today=Path.Combine(vault,"records",DateTime.Now.ToString("yyyy-MM-dd")+".md");Directory.CreateDirectory(Path.GetDirectoryName(today)!);const string already="---\ncustom: 已有属性\n---\n## 今天\n用户原有日记\n## 明天\n- [x] 已有任务\n";File.WriteAllText(today,already);
            var oldConfig=File.ReadAllBytes(config);main.ApplyTheme("night");dialog=main.CreateVaultSettingsDialog();dialog.Show();dialog.VaultInput.Text=vault;dialog.DetectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));dialog.UpdateLayout();
            Check(dialog.TemplateInput.Text==Path.Combine(vault,"templates","custom.md")&&dialog.FolderInput.Text=="records"&&dialog.FormatInput.Text=="YYYY-MM-DD"&&dialog.UseDefault.IsChecked==false,"设置界面实际读取已有仓库模板和记录位置");
            var bg=((System.Windows.Media.SolidColorBrush)dialog.Background).Color;
            Check(bg.R<65&&bg.G<65&&bg.B<65&&DockNative.SettingsCaptionValid(dialog),"日记库设置随深色主题呈现黑底白字，无最小化按钮");UiSmoke.Capture(dialog,Path.Combine(root,"04-dark-vault-settings.png"));
            main.Editors[first].Append("\n切库前必须保存的内容\n");
            using(var locked=new FileStream(oldTarget,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(dialog.IsVisible&&!dialog.Accepted&&main.IsVisible&&main.Session.Dirty&&File.ReadAllBytes(config).SequenceEqual(oldConfig),"旧日记占用导致保存失败时拒绝切库，保留窗口、输入和配置");
            }
            Check(dialog.ErrorText.Text.Length>0&&main.TrySave(),"失败原因可见，释放文件占用后可保存重试");var oldSaved=DiarySession.ReadText(oldTarget);
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(dialog.Accepted&&main.RestartRequested&&!main.IsVisible&&oldSaved.Contains("切库前必须保存"),"成功切库前旧内容落盘，并关闭旧卡片请求自动重启");
            Check(DiarySession.ReadText(today)==already,"设置切换不覆盖目标仓库当天已有笔记");
            current=CardConfig.Load(config);main=new MainWindow(current);main.Show();main.UpdateLayout();Pump();
            Check(main.Editors.Count==2&&main.Session.Target==today&&main.Editors["今天"].Body.Contains("用户原有日记"),"新卡片按目标模板重建两栏并载入目标当天内容");
            Check(main.CurrentTheme=="night"&&main.BackgroundFile==DefaultAssets.BackgroundName,"切库后的卡片保留主题和背景");main.Capture(Path.Combine(root,"05-existing-vault-card.png"));
            main.Editors["今天"].Append("\n仅写入第二个仓库\n");Check(main.TrySave()&&DiarySession.ReadText(today).Contains("仅写入第二个仓库")&&DiarySession.ReadText(oldTarget)==oldSaved,"切库后编辑只更新新日记，旧库内容不变");
            main.ExitCard();main=new MainWindow(CardConfig.Load(config));main.Show();main.UpdateLayout();Pump();
            Check(main.Editors["今天"].Body.Contains("仅写入第二个仓库"),"再次打开使用已保存的新仓库并恢复内容");main.ExitCard();
            string invalidRoot=Path.Combine(root,"invalid");Directory.CreateDirectory(invalidRoot);dialog=new VaultSetupWindow(Path.Combine(invalidRoot,"diary-card.json"),p=>{SetupService.Complete(p);return true;});dialog.Show();dialog.VaultInput.Text=Path.Combine(invalidRoot,"missing");dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(dialog.IsVisible&&!dialog.Accepted&&dialog.ErrorText.Text.Length>0&&!File.Exists(Path.Combine(invalidRoot,"diary-card.json")),"无效地址提示留在向导中，不写入错误配置");dialog.Close();
        }
        catch(Exception e){failure=e;}
        finally
        {
            try{dialog?.Close();if(main?.IsVisible==true){main.LoadDiskKeepingDraft();main.ExitCard();}}catch{}
            DiarySession.AtomicText(Path.Combine(root,"setup-ui-result.json"),JsonSerializer.Serialize(new{passed=failure==null,results,error=failure?.ToString()},CardConfig.Json));app.Shutdown();
        }
        return failure==null?0:1;
    }
}

public static class SettingsSwitchProbe
{
    public static void Attach(MainWindow main,CardConfig config,string target)
    {
        if(!config.ConfigPath.Contains(Path.DirectorySeparatorChar+"local"+Path.DirectorySeparatorChar+"tests"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("自动切库验证只允许独立测试目录。");
        main.ContentRendered+=(_,_)=>
        {
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(450)};
            timer.Tick+=(_,_)=>
            {
                timer.Stop();var first=main.Session.Template[0].Title;main.Editors[first].Append("\n进程切库前必须保存的中文\n");var dialog=main.CreateVaultSettingsDialog();dialog.Show();dialog.VaultInput.Text=target;dialog.DetectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!dialog.Accepted){DiarySession.AtomicText(Path.Combine(config.UserDataPath,"switch-failure.txt"),dialog.ErrorText.Text);dialog.Close();main.ExitCard();}
            };timer.Start();
        };
    }
}
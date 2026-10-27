using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace DailyDiaryCard;
public static class LibraryUiTests
{
    private static void Pump()
    {
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
    }
    private static bool Wait(Task<bool> task)
    {
        DateTime limit=DateTime.UtcNow.AddSeconds(30);
        while(!task.IsCompleted)
        {
            if(DateTime.UtcNow>limit)throw new Exception("资料库界面操作超时。");
            var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(15)};
            timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }
    private static void Capture(Window window,string path)
    {
        window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Themes.Initialize();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var results=new List<string>();Exception? failure=null;MainWindow? main=null;LibraryPanel? panel=null;
        void Check(bool value,string name){if(!value)throw new Exception(name);results.Add(name);}
        try
        {
            string install=Path.Combine(root,"install");Directory.CreateDirectory(install);
            var c=SetupService.Complete(SetupService.PlanStandalone(Path.Combine(install,"diary-card.json"),Path.Combine(install,"Diary"),"",true,"日记","YYYY/MM/YYYY-MM-DD"));
            DateOnly today=DateOnly.FromDateTime(DateTime.Now),history=today.AddDays(-1);
            var older=new DiarySession(c,history);string first=older.Template[0].Title;older.Edit(first,older.Bodies[first]+"\n昨天保留的中文记录\n- [ ] 审核历史任务\n");older.Save();
            main=new MainWindow(c){WeeklyTimerEnabled=false};main.Show();Pump();
            Check(main.MoreMenu.Items.OfType<MenuItem>().Any(i=>(string?)i.Header=="日记资料库…"),"独立模式提供内置资料库入口");
            Check(main.CreateStorageSettingsDialog() is StandaloneSetupWindow,"独立模式设置进入本地资料夹界面");
            main.Editors[first].Append("\n今天卡片上的中文内容\n");Check(main.TrySave(),"当前主卡片仍能可靠保存中文");
            panel=main.CreateLibraryDialog();panel.Window.Show();Pump();Check(Wait(panel.RefreshAsync()),"内置资料库实际列出本地记录");
            Check(panel.ItemsList.Items.Count==2,"资料库显示今天和历史两篇日记");
            Check(panel.OpenPath(older.Target)&&panel.Editors.ContainsKey(first),"历史日记以栏目表单在软件内打开");
            panel.Editors[first].Append("\n在内置界面继续完善历史日记\n");
            panel.Editors[first].TaskInputs.Last().IsChecked=true;
            Check(panel.SaveCurrent()&&File.ReadAllText(older.Target).Contains("- [x] 审核历史任务")&&File.ReadAllText(older.Target).Contains("继续完善历史日记"),"历史中文及任务勾选从真实控件写入原文件");
            Capture(panel.Window,Path.Combine(root,"01-history-library.png"));
            panel.SearchBox.Text="继续完善历史日记";Check(Wait(panel.RefreshAsync())&&panel.ItemsList.Items.Count==1,"按中文正文搜索历史日记");
            panel.SearchBox.Clear();Check(Wait(panel.RefreshAsync()),"清除搜索后恢复全部记录");
            panel.Editors[first].Append("\n被占用时不能丢掉的输入\n");
            using(var hold=new FileStream(older.Target,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                Check(!panel.SaveCurrent()&&panel.Dirty&&panel.CurrentText.Contains("不能丢掉的输入"),"保存失败时保留资料库输入和错误提示");
                Check(!main.ExitCard()&&main.IsVisible,"历史内容保存失败阻止主卡片退出");
                panel.Window.Close();Check(panel.Window.IsVisible,"历史内容保存失败阻止关闭资料库窗口");
                var weekly=main.CreateWeeklyDialog();weekly.Window.Show();weekly.WeekPicker.SelectedDate=history.ToDateTime(TimeOnly.MinValue);
                Check(!Wait(weekly.PreviewAsync())&&weekly.Message.Text.Contains("资料库")&&panel.Dirty,"历史保存失败时阻止周报汇总，避免遗漏输入");
                weekly.Window.Close();
            }
            Check(panel.SaveCurrent(),"占用解除后可以重试保存");
            panel.Editors[first].Append("\n资料库本地改动\n");
            var disk=new MarkdownDoc(File.ReadAllText(older.Target));string other=disk.Apply(new(){{first,disk.Unique(first)!.Body+"\n其他程序的修改\n"}},new DiarySession(c,history,false).Template);
            File.WriteAllText(older.Target,other,DiarySession.Utf8);
            Check(!panel.SaveCurrent()&&panel.CurrentText.Contains("资料库本地改动")&&File.ReadAllText(older.Target).Contains("其他程序的修改"),"历史同栏外部冲突保留双方内容");
            Check(panel.ReloadKeepingDraft()&&panel.CurrentText.Contains("其他程序的修改"),"冲突后保留草稿并重新读取磁盘");
            Check(panel.OpenPath(c.TemplatePath)&&panel.AddColumn("生活点滴")&&panel.SaveCurrent(),"无需外部编辑器即可添加模板栏目并保存");
            Check(!panel.AddColumn("生活点滴"),"内置模板编辑拒绝重复栏目");
            Check(main.RefreshTemplateNow()&&main.Editors.ContainsKey("生活点滴"),"内置模板修改同步到主卡片");
            Capture(panel.Window,Path.Combine(root,"02-template-editor.png"));
            var report=WeeklyReport.Preview(c,WeeklyWindow.ForDate(history,DateTimeOffset.Now));WeeklyReport.Save(report);
            Check(panel.OpenPath(report.Path)&&panel.SourceButtons.Count>0,"周报在内置阅读界面打开并显示来源入口");
            Capture(panel.Window,Path.Combine(root,"03-weekly-reader.png"));
            byte[] before=File.ReadAllBytes(report.Path);var autoBefore=WeeklyReport.Markers(before)!;
            Check(panel.ShowManualEditor(),"软件内提供周报手工补充编辑");
            panel.RawText.AppendText("\n独立模式的手工周报反馈 😀\n");Check(panel.SaveCurrent(),"周报手工补充从内置界面保存");
            var autoAfter=WeeklyReport.Markers(File.ReadAllBytes(report.Path))!;
            Check(autoBefore.Prefix.SequenceEqual(autoAfter.Prefix)&&autoBefore.Body.SequenceEqual(autoAfter.Body)&&File.ReadAllText(report.Path).Contains("手工周报反馈"),"编辑周报补充不改变自动区");
            panel.ShowReader();var source=panel.SourceButtons.FirstOrDefault(b=>(b.Content as string)?.Contains(history.ToString("yyyy-MM-dd"))==true);
            Check(source!=null,"周报包含历史日记来源按钮");source!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(panel.CurrentPath==older.Target,"点击周报来源在软件内跳转历史日记");
            string export=Path.Combine(root,"导出历史日记.md");panel.ExportTo(export);Check(File.Exists(export)&&File.ReadAllText(export)==panel.CurrentText,"可以导出本地副本，原件继续保留");
            main.ApplyTheme("night");Pump();Capture(panel.Window,Path.Combine(root,"04-dark-library.png"));
            var bg=((SolidColorBrush)panel.Window.Background).Color;Check(bg.R<50&&bg.G<50&&bg.B<50,"资料库跟随深色主题黑底白字");
            Check(!Directory.Exists(Path.Combine(c.VaultPath,".obsidian")),"完整独立流程不创建 Obsidian 配置");
            Check(!panel.OpenPath(c.PathFor(today))&&!panel.Window.IsVisible&&main.IsVisible,"打开今天回到主卡片，避免两个可写会话");
            main.ExitCard();main=null;
            DateOnly clock=today;main=new MainWindow(c,()=>clock){WeeklyTimerEnabled=false};main.Show();Pump();
            var future=new DiarySession(c,today.AddDays(1));future.Edit(first,future.Bodies[first]+"\n下一日预先填写的内容\n");future.Save();
            panel=main.CreateLibraryDialog();panel.Window.Show();Check(panel.OpenPath(future.Target),"可以在资料库打开已经存在的下一日记录");
            panel.Editors[first].Append("\n跨天前资料库尚未保存的输入\n");clock=today.AddDays(1);
            Check(main.CheckDay()&&main.Session.Date==clock&&!panel.Window.IsVisible&&File.ReadAllText(future.Target).Contains("跨天前资料库尚未保存的输入"),"跨天先保存资料库并回到唯一主卡片会话");
        }
        catch(Exception e){failure=e;}
        finally
        {
            try{if(panel?.Window.IsVisible==true){if(panel.Dirty)panel.ReloadKeepingDraft();panel.Window.Close();}if(main?.IsVisible==true)main.ExitCard();}catch{}
            DiarySession.AtomicText(Path.Combine(root,"library-ui-result.json"),JsonSerializer.Serialize(new{passed=failure==null,results,error=failure?.ToString()},CardConfig.Json));app.Shutdown();
        }
        return failure==null?0:1;
    }
}
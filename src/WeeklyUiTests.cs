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
public static class WeeklyUiTests
{
    private static void Pump()
    {
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
    }
    private static bool Wait(Task<bool> task)
    {
        var limit=DateTime.UtcNow.AddSeconds(30);
        while(!task.IsCompleted)
        {
            if(DateTime.UtcNow>limit)throw new Exception("周报界面操作超时。");
            var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(10)};
            timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }
    private static void Capture(Window window,string file)
    {
        window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);var bmp=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bmp.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var stream=File.Create(file);encoder.Save(stream);
    }
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);var results=new List<string>();Exception? failure=null;
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Themes.Initialize();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        MainWindow? main=null;WeeklyPanel? panel=null;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add(label);}
        try
        {
            var c=WeeklyTests.Fixture(Path.Combine(root,"install"));
            string diary=WeeklyTests.Note(c,"2026-10-26","## 每日任务完成情况\n- [x] 交付合成验收报告\n- [ ] 核对接口\n## 今天推进了什么（项目）\n形成第一份对比结果\n## 今日学习内容\n掌握周报归档方式\n## 卡在哪里\n- 可能原因（还未确认也可以）：可能权限不足\n## 第二日任务规划\n- [ ] 核对后续参数\n");
            main=new MainWindow(c,()=>new DateOnly(2026,10,26)){WeeklyTimerEnabled=false};main.Show();Pump();
            Check(main.MoreMenu.Items.OfType<MenuItem>().Any(i=>(string?)i.Header=="周报汇总…"),"更多菜单提供周报汇总入口");
            panel=main.CreateWeeklyDialog();panel.Window.Show();Pump();
            Check(panel.DayChoice.Items.Count==7&&panel.HourChoice.Items.Count==25&&panel.MinuteChoice.Items.Count==60,"可选择周一到周日及00至24点和分钟");
            Check(panel.AutoCheck.IsChecked==false&&panel.DayChoice.SelectedIndex==4&&panel.HourChoice.SelectedIndex==12,"默认关闭自动汇总，时间沿用周五12点");
            panel.DayChoice.SelectedIndex=6;panel.MinuteChoice.SelectedIndex=37;panel.HourChoice.SelectedIndex=24;
            Check(panel.MinuteChoice.SelectedIndex==0&&!panel.MinuteChoice.IsEnabled,"选择24点时分钟自动归零并锁定");
            panel.AutoCheck.IsChecked=true;Check(panel.SaveSettings(),"周日24点自动设置成功保存");
            var persisted=WeeklySettings.Load(c);Check(persisted.Weekday==6&&persisted.Hour==24&&persisted.Minute==0&&persisted.AutoEnabled,"按库持久保存完整定时设置");
            panel.WeekPicker.SelectedDate=new DateTime(2026,10,30);Check(panel.Summary.Text.Contains("2026-10-24")&&panel.Summary.Text.Contains("2026-10-30"),"界面明确显示周六至下周五七天范围");
            main.Editors["今天推进了什么（项目）"].Append("\n卡片尚未保存的中文成果\n");
            var previewTask=panel.PreviewAsync();Check(main.WeeklyBusy&&!main.ExitCard(),"汇总运行时阻止退出避免半途中断");
            Check(Wait(previewTask)&&panel.PreviewBox.Text.Contains("卡片尚未保存的中文成果")&&File.ReadAllText(diary).Contains("卡片尚未保存的中文成果"),"预览前先保存卡片输入，再读取日记");
            Check(panel.PreviewBox.IsReadOnly&&panel.GenerateButton.IsEnabled&&!Directory.Exists(panel.CurrentPreview!.OutputRoot),"预览可滚动只读且不提前创建周报目录");
            Capture(panel.Window,Path.Combine(root,"01-weekly-preview.png"));
            Check(Wait(panel.GenerateAsync())&&File.Exists(panel.CurrentPreview!.Path)&&panel.OpenButton.IsEnabled,"从界面实际生成周报并提供打开入口");
            string report=panel.CurrentPreview!.Path;File.AppendAllText(report,"\n手写补充需要保留\n");
            Check(Wait(panel.PreviewAsync()),"可以重新预览已存在的周报");
            File.AppendAllText(report,"外部新改动\n");
            Check(!Wait(panel.GenerateAsync())&&panel.Message.Text.Contains("外部修改")&&panel.PreviewBox.Text.Contains("交付合成验收报告")&&File.ReadAllText(report).Contains("外部新改动"),"预览后外部修改被检测，双方内容均保留");
            Check(Wait(panel.PreviewAsync()),"外部修改后可重新预览");
            WeeklyTests.Note(c,"2026-10-27","## 今日学习内容\n新增实写学习记录");
            Check(!Wait(panel.GenerateAsync())&&panel.Message.Text.Contains("发生变化")&&panel.PreviewBox.Text.Contains("新增实写学习记录"),"预览后日记改变时更新预览并要求再次生成");
            Check(Wait(panel.GenerateAsync())&&File.ReadAllText(report).Contains("手写补充需要保留"),"确认更新后保留周报手工补充");
            Check(Wait(panel.PreviewAsync()),"文件占用前有完整预览");
            using(var hold=new FileStream(report,FileMode.Open,FileAccess.Read,FileShare.None))
                Check(!Wait(panel.GenerateAsync())&&panel.PreviewBox.Text.Contains("新增实写学习记录")&&!panel.IsBusy,"保存失败显示错误，预览及操作能力保留");
            panel.FolderInput.Text="..";Check(!panel.SaveSettings()&&WeeklySettings.Load(c).Folder=="周报"&&!panel.GenerateButton.IsEnabled,"无效目录阻止保存并使旧预览失效");
            panel.FolderInput.Text="周报";main.ApplyTheme("night");Pump();
                        panel.WeekPicker.ApplyTemplate();
            var dateText=(System.Windows.Controls.Primitives.DatePickerTextBox)panel.WeekPicker.Template.FindName("PART_TextBox",panel.WeekPicker);
            var dateBg=((SolidColorBrush)dateText.Background).Color;
            Check(dateBg.R<60&&dateBg.G<60&&dateBg.B<60,"深色日期输入框没有白底且文字可读");
            panel.WeekPicker.IsDropDownOpen=true;Pump();
            var popup=(System.Windows.Controls.Primitives.Popup)panel.WeekPicker.Template.FindName("PART_Popup",panel.WeekPicker);
            var calendar=popup.Child as Calendar;
            Check(calendar!=null&&((SolidColorBrush)calendar.Background).Color.R<60,"日期弹出日历跟随深色主题");
            panel.WeekPicker.IsDropDownOpen=false;Capture(panel.Window,Path.Combine(root,"02-dark-weekly-settings.png"));
            var color=((SolidColorBrush)panel.Window.Background).Color;Check(color.R<50&&color.G<50&&color.B<50&&panel.Window.ResizeMode!=ResizeMode.CanMinimize,"深色周报窗口黑底且没有最小化按钮");
            panel.Window.Close();panel=main.CreateWeeklyDialog();panel.Window.Show();Pump();
            Check(panel.DayChoice.SelectedIndex==6&&panel.HourChoice.SelectedIndex==24&&panel.AutoCheck.IsChecked==true,"重新打开设置恢复周日24点选择");
            panel.Window.Close();panel=null;
            var due=DateTimeOffset.Parse("2026-11-02T00:00:00+08:00");
            Check(Wait(main.RunWeeklyAutoAsync(due)),"周日24点即周一零点真实异步自动汇总");
            Check(!Wait(main.RunWeeklyAutoAsync(due.AddMinutes(1)))&&WeeklySettings.Load(c).LastCompletedWeek=="2026-W44","同一期自动任务完成后不重复运行");
            Check(File.ReadAllText(report).Contains("手写补充需要保留")&&main.WeeklyMessage.Contains("2026-W44"),"自动重跑保留手工区并显示结果状态");
            main.Capture(Path.Combine(root,"03-card-weekly-status.png"));
        }
        catch(Exception e){failure=e;}
        finally
        {
            panel?.Window.Close();if(main!=null){main.WeeklyTimerEnabled=false;main.ExitCard();}
            DiarySession.AtomicText(Path.Combine(root,"weekly-ui-result.json"),JsonSerializer.Serialize(new{passed=failure==null,results,error=failure?.ToString()},CardConfig.Json));app.Shutdown();
        }
        return failure==null?0:1;
    }
}
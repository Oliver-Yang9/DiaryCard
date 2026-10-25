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
public static class UiSmoke
{
    private static DateOnly day=DateOnly.FromDateTime(DateTime.Now);
    public static DateOnly Today()=>day;
    public static void Attach(MainWindow window,CardConfig config,string? taskFixture=null,string? mediaFixtures=null)
    {
        int phase=0,saveWaits=0;var results=new List<string>();var output=Path.Combine(config.UserDataPath,"ui-verification");Directory.CreateDirectory(output);
        double originalWidth=window.Width,originalHeight=window.Height,originalLeft=window.Left,originalTop=window.Top;
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(650)};
        void Assert(bool ok,string name){if(!ok)throw new Exception(name);results.Add(name);}
        void Report(bool passed,Exception? error=null)=>DiarySession.AtomicText(Path.Combine(output,"ui-result.json"),JsonSerializer.Serialize(new{passed,results,effects=window.Effects,systemVersion=Environment.OSVersion.Version.ToString(),error=error?.ToString()},CardConfig.Json));
        window.ContentRendered+=(_,_)=>timer.Start();
        timer.Tick+=(_,_)=>
        {
            try
            {
                var first=window.Session.Template[0].Title;
                switch(phase++)
                {
                    case 0: originalLeft=window.Left;originalTop=window.Top;Assert(window.Editors.Count==window.Session.Template.Count,"真实模板栏目映射到 WPF 控件");Assert(window.Editors[first].TextInputs.Count==2,"空白段落不生成多余大输入框");window.ApplyTheme("fluent");window.Capture(Path.Combine(output,"01-fluent.png"));break;
                    case 1: window.Editors[first].TextInputs[0].AppendText("\n真实控件中文输入验证：今天顺利推进。\n");Assert(window.Session.Dirty,"真实 TextBox 编辑触发待保存");break;
                    case 2: if(window.Session.Dirty){if(saveWaits++>8)throw new Exception("自动保存计时器未完成保存");phase--;break;}Assert(DiarySession.ReadText(window.Session.Target).Contains("今天顺利推进"),"停顿后计时器自动保存中文，无需点击保存");window.Editors[first].TaskInputs[0].IsChecked=true;Assert(window.Session.Bodies[first].Contains("- [x]"),"CheckBox 转换为 Markdown 完成标记");break;
                    case 3: Assert(window.TrySave(),"任务状态已保存");Assert(window.HideCard()&&!window.IsVisible,"隐藏卡片成功且文件已保存");break;
                    case 4: window.ShowCard();Assert(window.IsVisible,"通过同一恢复入口恢复卡片");window.ApplyTheme("paper");window.Capture(Path.Combine(output,"02-paper.png"));break;
                    case 5: window.ApplyTheme("night");Assert(window.Session.Bodies[first].Contains("今天顺利推进"),"主题切换保留正文");window.Capture(Path.Combine(output,"03-night.png"));break;
                    case 6:
                        window.ApplyTheme("fluent");var library=window.CreateThemeLibrary();library.Show();library.UpdateLayout();Capture(library,Path.Combine(output,"04-theme-library.png"));library.Close();Assert(Preferences.Load(config.UserDataPath).Theme=="fluent","主题偏好已持久保存");break;
                    case 7:
                        window.Editors[first].Append("\n锁定文件时输入保留。\n");using(var file=new FileStream(window.Session.Target,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){Assert(!window.TrySave(),"文件占用时 UI 显示保存失败");Assert(window.Session.Dirty&&File.Exists(window.Session.RecoveryPath),"失败时内存与草稿均保留");window.Capture(Path.Combine(output,"05-save-failure.png"));}break;
                    case 8: Assert(window.TrySave(),"释放占用后重试保存成功");window.Editors[first].Append("\n本地冲突内容。\n");var disk=new MarkdownDoc(window.Session.Baseline!).Apply(new(){{first,window.Session.Bodies[first]+"\n外部冲突内容。\n"}},window.Session.Template);File.WriteAllText(window.Session.Target,disk,DiarySession.Utf8);Assert(!window.TrySave()&&window.Session.IsConflict,"同栏外部修改触发冲突提示");Assert(DiarySession.ReadText(window.Session.Target)==disk,"冲突时磁盘未被覆盖");window.Capture(Path.Combine(output,"06-conflict.png"));break;
                    case 9: Assert(!window.ExitCard()&&window.IsVisible,"冲突未处理时正常退出保留窗口和输入");window.Session.ReloadKeepingDraft();day=day.AddDays(1);Assert(window.CheckDay(),"模拟系统本地日期跨天切换成功");Assert(File.Exists(window.Session.Target),"跨天后未填写也自动创建并载入当天日记");window.Editors[window.Session.Template[1].Title].Append("新一天独有输入。\n");Assert(window.TrySave(),"新一天写入新日期文件");Assert(!DiarySession.ReadText(window.Session.Target).Contains("今天顺利推进"),"旧日正文没有误写入新日");window.Capture(Path.Combine(output,"07-next-day.png"));break;
                    case 10:
                        var title=window.Session.Template[1].Title;var original=window.Editors[title].TextInputs[0].Text;window.Editors[title].TextInputs[0].AppendText("临时修改待撤销");window.Editors[title].TextInputs[0].Text=original;Assert(!window.Session.Dirty,"改回原文后不再标记为待保存");Assert(window.TrySave(),"撤销后的草稿状态可刷新");var draft=JsonSerializer.Deserialize<Draft>(File.ReadAllText(window.Session.RecoveryPath),CardConfig.Json)!;Assert(!draft.Pending,"撤销后不会在重启时恢复已放弃的输入");break;
                    case 11:
                        var document=window.Session.LocalDocument();window.ApplyTheme("glass");Assert(Themes.All.Length==4&&window.CurrentTheme=="glass","主题库包含第四套雾透玻璃主题");Assert(window.Session.LocalDocument()==document,"玻璃主题切换不改变正文和任务");
                        if(Environment.OSVersion.Version.Build>=22621&&window.Effects.CompositionEnabled&&!SystemParameters.HighContrast)Assert(window.Effects.AcrylicConfigured,"当前 Windows 的原生 Acrylic 请求成功");
                        else Assert(!window.Effects.AcrylicConfigured,"不支持磨砂时使用可读的回退背景");
                        DiarySession.AtomicText(Path.Combine(output,"glass-effects.json"),JsonSerializer.Serialize(window.Effects,CardConfig.Json));window.Capture(Path.Combine(output,"08-glass-wpf-layer.png"));break;
                    case 12:
                        window.ApplyTheme("fluent");window.PinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(window.Topmost&&Preferences.Load(config.UserDataPath).Topmost,"顶部置顶按钮生效并记住状态");window.PinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(!window.Topmost,"再次点击取消置顶");
                        Assert(WindowEffects.HitTest(window,new Point(26,25))==2,"顶部拖动区域原生命中为标题栏");
                        var handlePoint=window.ResizeHandle.TranslatePoint(new Point(12,12),window);Assert(WindowEffects.HitTest(window,handlePoint)==17,"右下角手柄原生命中为缩放区域");break;
                    case 13:
                        window.Width=460;window.Height=640;window.Left+=18;window.Top+=12;window.UpdateLayout();Assert(Math.Abs(window.ActualWidth-460)<2&&Math.Abs(window.ActualHeight-640)<2,"卡片可调整为较小尺寸且仍有正文区域");window.Capture(Path.Combine(output,"09-compact-controls.png"));
                        window.HideToTrayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(!window.IsVisible,"顶部隐藏至托盘按钮使用保存后隐藏流程");var pref=Preferences.Load(config.UserDataPath);Assert(Math.Abs(pref.Width-460)<2&&Math.Abs(pref.Height-640)<2&&Math.Abs(pref.Left-window.Left)<2&&Math.Abs(pref.Top-window.Top)<2,"调整的窗口大小与位置均持久保存");break;
                    case 14:
                        var second=window.Session.Template[1].Title;var edited=new MarkdownDoc(window.Session.Baseline!).Apply(new(){{second,"\nObsidian 隐藏期间更新的当天正文。\n\n"}},window.Session.Template);File.WriteAllText(window.Session.Target,edited,DiarySession.Utf8);window.ShowCard();Assert(window.Session.Bodies[second].Contains("隐藏期间更新"),"恢复卡片时立即同步当天已有笔记");Assert(window.Editors[second].Body==window.Session.Bodies[second],"同步结果立即反映到可编辑栏目");using(var occupied=new FileStream(window.Session.Target,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){Assert(!window.RefreshFromDisk()&&window.Status.Contains("同步失败"),"外部文件占用时同步失败会清楚提示");}Assert(window.RefreshFromDisk()&&window.Status.Contains("已保存"),"占用解除后同步重试会清除过时的失败状态");break;
                    case 15:
                        window.Editors[first].Append("\n本地仍在填写的内容。\n");var conflict=new MarkdownDoc(window.Session.Baseline!).Apply(new(){{first,window.Session.Bodies[first]+"外部同时修改。\n"}},window.Session.Template);File.WriteAllText(window.Session.Target,conflict,DiarySession.Utf8);Assert(!window.RefreshFromDisk()&&window.Session.IsConflict,"同步遇到同栏未保存输入时提示冲突");Assert(DiarySession.ReadText(window.Session.Target)==conflict&&window.Session.Bodies[first].Contains("本地仍在填写"),"打开时同步也保留双方修改");window.LoadDiskKeepingDraft();break;
                    case 16:
                        window.Editors[first].Append("\n本地不同栏目编辑。\n");var otherTitle=window.Session.Template[1].Title;var other=new MarkdownDoc(window.Session.Baseline!).Apply(new(){{otherTitle,"\n外部另一个栏目更新。\n\n"}},window.Session.Template);File.WriteAllText(window.Session.Target,other,DiarySession.Utf8);Assert(window.RefreshFromDisk(),"同步能合并不同栏目的本地与外部修改");Assert(DiarySession.ReadText(window.Session.Target).Contains("本地不同栏目编辑")&&window.Editors[otherTitle].Body.Contains("外部另一个栏目"),"合并后磁盘和界面均保留正确内容");break;
                    case 17:
                        window.Editors[first].Append("\n关闭按钮失败保护。\n");using(var locked=new FileStream(window.Session.Target,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){window.CloseProgramButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(window.IsVisible&&window.Session.Dirty,"顶部关闭按钮遇到保存失败时保留窗口与输入");}Assert(window.TrySave(),"关闭失败后仍可重试保存");break;
                    case 18:
                        TaskRegression.Run(config,taskFixture,output,Assert);break;
                    case 19:
                        FeatureRegression.Run(window,config,output,Assert,value=>day=value);break;
                    case 20:
                        window.Docking.Arm(DockEdge.Top);Assert(window.Docking.Collapse(),"跨天计时器检查前卡片处于贴边收起状态");day=day.AddDays(1);break;
                    case 21:
                        Assert(window.Session.Date==day&&File.Exists(window.Session.Target),"实际计时器在收起期间自动跨天并创建当天文件");Assert(!window.IsVisible&&window.Docking.Collapsed,"后台跨天不强行展开卡片");window.ShowCard();Assert(window.Editors.Count==window.Session.Template.Count&&!window.Session.Dirty,"展开后匹配新一天模板栏目并处于已保存状态");window.Capture(Path.Combine(output,"15-auto-new-day.png"));break;
                    case 22:
                        timer.Stop();AnimationRegression.Start(window,config,output,Assert,error=>{if(error!=null){Report(false,error);window.ExitCard();}else timer.Start();});break;
                    case 23:
                        timer.Stop();MediaRegression.Start(window,config,mediaFixtures??Path.Combine(AppContext.BaseDirectory,"test-assets"),output,Assert,error=>{if(error!=null){Report(false,error);window.ExitCard();}else timer.Start();});break;
                    case 24:
                        SettingsThemeRegression.Run(window,output,Assert);break;
                    case 25:
                        timer.Stop();MenuThemeRegression.Start(window,output,Assert,error=>{if(error!=null){Report(false,error);window.ExitCard();}else timer.Start();});break;
                    case 26:
                        timer.Stop();TemplateRefreshRegression.Start(window,config,output,Assert,error=>{if(error!=null){Report(false,error);window.ExitCard();}else timer.Start();});break;
                    case 27:
                        window.Docking.AutomaticPolling=true;
                        window.Width=originalWidth;window.Height=originalHeight;window.Left=originalLeft;window.Top=originalTop;window.ApplyTheme("fluent");timer.Stop();window.CloseProgramButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(!window.IsVisible,"顶部关闭按钮保存成功后退出窗口");Report(true);break;
                }
            }
            catch(Exception e){timer.Stop();Report(false,e);try{window.Session.ReloadKeepingDraft();window.ExitCard();}catch{Application.Current.Shutdown(1);}}
        };
    }
    internal static void Capture(Window w,string file)
    {
        w.UpdateLayout();var visual=(FrameworkElement)w.Content;var dpi=VisualTreeHelper.GetDpi(w);var height=visual.ActualHeight+visual.Margin.Top+visual.Margin.Bottom;var bmp=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(height*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bmp.Render(w);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(file);encoder.Save(f);
    }
}

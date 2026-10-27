using System;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace DailyDiaryCard;
// 仅 --ui-probe 验证模式启用：只报告本程序状态，不枚举或读取任何其他桌面窗口。
public static class UiProbe
{
    public static void Attach(MainWindow window,CardConfig config)
    {
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};
        timer.Tick+=(_,_)=>DiarySession.AtomicText(Path.Combine(config.UserDataPath,"probe.json"),JsonSerializer.Serialize(new{
            mode=config.StorageMode,vault=config.VaultPath,target=window.Session.Target,template=config.TemplatePath,setupVersion=config.SetupVersion,
            visible=window.IsVisible,window.ShowRequests,theme=window.CurrentTheme,date=window.Session.Date.ToString("yyyy-MM-dd"),dirty=window.Session.Dirty,
            documentHash=DiarySession.Hash(window.Session.LocalDocument()),sections=window.Editors.Count,pid=Environment.ProcessId,
            topmost=window.Topmost,width=window.ActualWidth,height=window.ActualHeight,left=window.Left,top=window.Top,effects=window.Effects,
            background=window.BackgroundFile,hasBackground=window.HasPictureBackground,backgroundOpacity=window.BackgroundOpacity,edgeAutoHide=window.Docking.Enabled,edge=window.Docking.Edge.ToString(),edgeCollapsed=window.Docking.Collapsed
            ,sectionTransparency=window.SectionTransparency,backgroundKind=window.BackgroundPlayback.Kind,backgroundPlaying=window.BackgroundPlayback.IsPlaying
        },CardConfig.Json));
        window.ContentRendered+=(_,_)=>timer.Start();window.Closed+=(_,_)=>timer.Stop();
    }
}

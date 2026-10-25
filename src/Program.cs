using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace DailyDiaryCard;
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? Value(string key){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        if(args.Contains("--make-icon")){using var icon=MakeIcon();using var output=File.Create(Value("--make-icon")!);icon.Save(output);return 0;}
        if(args.Contains("--self-test"))return SelfTests.Run(Value("--test-root")??Path.Combine(Path.GetTempPath(),"DailyDiaryCardTests"),Value("--template")??@"E:\DailyNote\模板\03-工作学习复盘.md");
        CardConfig? config=null;
        try
        {
            config=CardConfig.Load(Value("--config"));
            if(args.Contains("--check-config")){var report=Value("--report")??throw new IOException("需要 --report 输出路径。");DiarySession.AtomicText(report,System.Text.Json.JsonSerializer.Serialize(new{config.VaultPath,config.TemplatePath,config.DiaryFolder,config.UserDataPath,Today=config.PathFor(DateOnly.FromDateTime(DateTime.Now)),Sections=new MarkdownDoc(DiarySession.ReadText(config.TemplatePath)).Sections.Select(h=>h.Title)},CardConfig.Json));return 0;}
            var key=DiarySession.Hash(config.VaultPath+"|"+config.DiaryFolder)[..24];
            using var mutex=new Mutex(true,"Local\\DailyDiaryCard-"+key,out bool first);
            using var restore=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DailyDiaryCard-Show-"+key);
            using var closeRequest=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DailyDiaryCard-Close-"+key);
            if(!first){if(args.Contains("--request-exit"))closeRequest.Set();else restore.Set();return 0;}
            if(args.Contains("--request-exit"))return 0;
            var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};Themes.Initialize();
            var window=new MainWindow(config,args.Contains("--ui-smoke")?UiSmoke.Today:null);app.MainWindow=window;
            var registration=ThreadPool.RegisterWaitForSingleObject(restore,(_,_)=>window.Dispatcher.BeginInvoke(window.ShowCard),null,Timeout.Infinite,false);
            var closeRegistration=ThreadPool.RegisterWaitForSingleObject(closeRequest,(_,_)=>window.Dispatcher.BeginInvoke(()=>window.ExitCard()),null,Timeout.Infinite,false);
            app.DispatcherUnhandledException+=(_,e)=>{try{window.Session.ArchiveDraft("未处理错误："+e.Exception.Message);}catch{}System.Windows.MessageBox.Show(window,"发生错误，已尝试保留恢复草稿：\n"+e.Exception.Message,"日记卡片");e.Handled=true;};
            if(args.Contains("--ui-smoke"))UiSmoke.Attach(window,config,Value("--task-fixture"),Value("--media-fixtures"));
            if(args.Contains("--ui-probe"))UiProbe.Attach(window,config);
            int result=app.Run(window);registration.Unregister(null);closeRegistration.Unregister(null);mutex.ReleaseMutex();return result;
        }
        catch(Exception e)
        {
            var path=Path.Combine(config?.UserDataPath??Path.GetTempPath(),"startup-error.txt");
            try{DiarySession.AtomicText(path,e.ToString());}catch{}
            System.Windows.MessageBox.Show("启动失败：\n"+e.Message+"\n\n诊断文件："+path,"日记卡片",MessageBoxButton.OK,MessageBoxImage.Error);return 1;
        }
    }
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr handle);
    public static Icon MakeIcon()
    {
        using var bmp=new Bitmap(64,64);using var g=Graphics.FromImage(bmp);g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(System.Drawing.Color.Transparent);
        using var blue=new SolidBrush(System.Drawing.Color.FromArgb(40,101,176));using var shape=new GraphicsPath();shape.AddArc(2,2,20,20,180,90);shape.AddArc(42,2,20,20,270,90);shape.AddArc(42,42,20,20,0,90);shape.AddArc(2,42,20,20,90,90);shape.CloseFigure();g.FillPath(blue,shape);
        using var pen=new Pen(System.Drawing.Color.White,4){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawLine(pen,18,18,18,47);g.DrawLine(pen,25,21,47,21);g.DrawLine(pen,25,32,47,32);g.DrawLine(pen,25,43,40,43);
        var handle=bmp.GetHicon();try{return (Icon)Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace DailyDiaryCard;

public static class VaultUse
{
    public static string Key(CardConfig c)=>DiarySession.Hash(c.VaultPath+"|"+c.DiaryFolder)[..24];
    public static bool InUse(CardConfig c){if(Mutex.TryOpenExisting("Local\\DailyDiaryCard-"+Key(c),out var mutex)){mutex.Dispose();return true;}return false;}
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? Value(string key){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        if(args.Contains("--wait-for-exit")&&int.TryParse(Value("--wait-for-exit"),out var pid))try{using var prior=Process.GetProcessById(pid);if(!prior.WaitForExit(12000)){System.Windows.MessageBox.Show("上一张卡片尚未退出，请稍后重新打开。","日记卡片");return 1;}}catch(ArgumentException){}
        if(args.Contains("--make-icon")){using var icon=MakeIcon();using var output=File.Create(Value("--make-icon")!);icon.Save(output);return 0;}
        var testRoot=Value("--test-root")??Path.Combine(Path.GetTempPath(),"DiaryCard2Tests");
        if(args.Contains("--self-test"))
        {
            var template=Value("--template");if(template==null){Directory.CreateDirectory(testRoot);template=Path.Combine(testRoot,"default-template.md");File.WriteAllText(template,DefaultAssets.Template,DiarySession.Utf8);}
            return SelfTests.Run(testRoot,template);
        }
        if(args.Contains("--setup-tests"))return SetupTests.Run(testRoot);
        if(args.Contains("--setup-ui-smoke"))return SetupUiTests.Run(testRoot);
        CardConfig? config=null;bool restart=false;int result=0;
        var file=Path.GetFullPath(Value("--config")??Path.Combine(AppContext.BaseDirectory,"diary-card.json"));
        try
        {
            if(args.Contains("--check-config")){config=CardConfig.Load(file);DiarySession.AtomicText(Value("--report")??throw new IOException("需要 --report 输出路径。"),System.Text.Json.JsonSerializer.Serialize(new{config.VaultPath,config.TemplatePath,config.DiaryFolder,config.UserDataPath,config.UseBundledTemplate,Today=config.PathFor(DateOnly.FromDateTime(DateTime.Now)),Sections=new MarkdownDoc(DiarySession.ReadText(config.TemplatePath)).Sections.Select(h=>h.Title)},CardConfig.Json));return 0;}
            var configKey=DiarySession.Hash(file.ToUpperInvariant())[..24];
            using(var configMutex=new Mutex(true,"Local\\DiaryCard2-Config-"+configKey,out bool configFirst))
            using(var configShow=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DiaryCard2-Show-"+configKey))
            using(var configClose=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DiaryCard2-Close-"+configKey))
            {
                if(!configFirst){if(args.Contains("--request-exit"))configClose.Set();else configShow.Set();return 0;}
                try
                {
                    if(args.Contains("--request-exit"))return 0;
                    var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Themes.Initialize();Themes.Apply("fluent");Window? active=null;
                    var showRegistration=ThreadPool.RegisterWaitForSingleObject(configShow,(_,_)=>app.Dispatcher.BeginInvoke(()=>{if(active is MainWindow card)card.ShowCard();else{active?.Show();active?.Activate();}}),null,Timeout.Infinite,false);
                    var closeRegistration=ThreadPool.RegisterWaitForSingleObject(configClose,(_,_)=>app.Dispatcher.BeginInvoke(()=>{if(active is MainWindow card)card.ExitCard();else active?.Close();}),null,Timeout.Infinite,false);
                    try
                    {
                        string? error=null;
                        try{config=CardConfig.Load(file);}catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException){error=e.Message;}
                        if(config==null)
                        {
                            var wizard=new VaultSetupWindow(file,plan=>{if(VaultUse.InUse(plan.Config))throw new IOException("该日记库正在另一张卡片中打开，请先保存并正常退出那张卡片。");config=SetupService.Complete(plan);return true;},startupError:File.Exists(file)?error:null);active=wizard;if(args.Contains("--ui-probe"))wizard.ContentRendered+=(_,_)=>DiarySession.AtomicText(Path.Combine(Path.GetDirectoryName(file)!,"startup-probe.json"),System.Text.Json.JsonSerializer.Serialize(new{state="setup",pid=Environment.ProcessId,visible=wizard.IsVisible},CardConfig.Json));wizard.ShowDialog();
                            if(!wizard.Accepted){app.Shutdown();return 0;}
                        }
                        var key=VaultUse.Key(config!);
                        using var mutex=new Mutex(true,"Local\\DailyDiaryCard-"+key,out bool first);
                        using var restore=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DailyDiaryCard-Show-"+key);
                        using var closeRequest=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DailyDiaryCard-Close-"+key);
                        if(!first){restore.Set();System.Windows.MessageBox.Show("这个日记库已经由另一张卡片打开。请先正常退出已有卡片，再使用第二版。","日记库已打开");app.Shutdown();return 0;}
                        try
                        {
                            var window=new MainWindow(config!,args.Contains("--ui-smoke")?UiSmoke.Today:null);active=window;app.MainWindow=window;app.ShutdownMode=ShutdownMode.OnMainWindowClose;
                            var registration=ThreadPool.RegisterWaitForSingleObject(restore,(_,_)=>window.Dispatcher.BeginInvoke(window.ShowCard),null,Timeout.Infinite,false);
                            var exitRegistration=ThreadPool.RegisterWaitForSingleObject(closeRequest,(_,_)=>window.Dispatcher.BeginInvoke(()=>window.ExitCard()),null,Timeout.Infinite,false);
                            app.DispatcherUnhandledException+=(_,e)=>{try{window.Session.ArchiveDraft("未处理错误："+e.Exception.Message);}catch{}System.Windows.MessageBox.Show(window,"发生错误，已尝试保留恢复草稿：\n"+e.Exception.Message,"日记卡片");e.Handled=true;};
                            if(args.Contains("--ui-smoke"))UiSmoke.Attach(window,config!,Value("--task-fixture"),Value("--media-fixtures"));
                            if(args.Contains("--ui-probe"))UiProbe.Attach(window,config!);
                            if(args.Contains("--settings-switch-test"))SettingsSwitchProbe.Attach(window,config!,Value("--settings-switch-test")!);
                            result=app.Run(window);restart=window.RestartRequested;registration.Unregister(null);exitRegistration.Unregister(null);
                        }
                        finally{mutex.ReleaseMutex();}
                    }
                    finally{showRegistration.Unregister(null);closeRegistration.Unregister(null);}
                }
                finally{configMutex.ReleaseMutex();}
            }
            if(restart)
            {
                var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
                foreach(var item in new[]{"--config",file,"--wait-for-exit",Environment.ProcessId.ToString()})start.ArgumentList.Add(item);
                if(args.Contains("--ui-probe"))start.ArgumentList.Add("--ui-probe");
                _=Process.Start(start)??throw new IOException("新卡片未能启动，请重新打开程序。");
            }
            return result;
        }
        catch(Exception e)
        {
            var path=Path.Combine(config?.UserDataPath??Path.GetDirectoryName(file)!,"startup-error.txt");try{DiarySession.AtomicText(path,e.ToString());}catch{}
            System.Windows.MessageBox.Show("未能打开卡片：\n"+e.Message+"\n\n可以把软件解压到可写入的文件夹后重试。诊断文件："+path,"日记卡片",MessageBoxButton.OK,MessageBoxImage.Error);return 1;
        }
    }
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr handle);
    public static Icon MakeIcon()
    {
        using var bmp=new Bitmap(64,64);using var g=Graphics.FromImage(bmp);g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(System.Drawing.Color.Transparent);
        using var blue=new SolidBrush(System.Drawing.Color.FromArgb(40,101,176));using var shape=new GraphicsPath();shape.AddArc(2,2,20,20,180,90);shape.AddArc(42,2,20,20,270,90);shape.AddArc(42,42,20,20,0,90);shape.CloseFigure();g.FillPath(blue,shape);
        using var pen=new Pen(System.Drawing.Color.White,4){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawLine(pen,18,18,18,47);g.DrawLine(pen,25,21,47,21);g.DrawLine(pen,25,32,47,32);g.DrawLine(pen,25,43,40,43);
        var handle=bmp.GetHicon();try{return (Icon)Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}
    }
}
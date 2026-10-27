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
public static class StartupUiTests
{
    private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Themes.Initialize();var results=new List<string>();Exception? error=null;MainWindow? main=null;StartupPanel? panel=null;
        void Check(bool value,string name){if(!value)throw new Exception(name);results.Add(name);}
        try
        {
            var config=SetupService.Complete(SetupService.Plan(Path.Combine(root,"diary-card.json"),true,"","",true,"日记","YYYY/MM/YYYY-MM-DD"));
            main=new MainWindow(config);main.Show();Pump();var store=new MemoryStartupStore();panel=main.CreateStartupDialog(store);panel.Window.Show();Pump();
            Check(main.MoreMenu.Items.OfType<MenuItem>().Any(i=>(string?)i.Header=="开机自启动…"),"卡片菜单提供开机自启动选择入口");
            Check(panel.Choice.IsChecked==false&&store.Value==null,"开机自启动默认关闭且打开设置不写入");
            panel.Choice.IsChecked=true;Check(panel.Apply()&&StartupService.EnabledHere(Environment.ProcessPath!,config.ConfigPath,store),"用户明确保存后开启当前版本");
            panel.Choice.IsChecked=false;Check(panel.Apply()&&store.Value==null,"用户可以关闭当前版本");
            store.FailWrites=true;panel.Choice.IsChecked=true;Check(!panel.Apply()&&store.Value==null&&panel.Message.Text.Contains("尚未生效"),"设置失败清晰提示且不伪报开启");
            store.FailWrites=false;Check(panel.Apply(),"失败后可以重试");
            main.ApplyTheme("night");Pump();var color=((SolidColorBrush)panel.Window.Background).Color;Check(color.R<50&&color.G<50&&color.B<50,"开机自启动窗口跟随深色主题");
            panel.Window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(panel.Window);var bmp=new RenderTargetBitmap((int)Math.Ceiling(panel.Window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(panel.Window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bmp.Render(panel.Window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(Path.Combine(root,"startup-settings.png")))encoder.Save(file);
            panel.Window.Close();panel=main.CreateStartupDialog(store);panel.Window.Show();Pump();Check(panel.Choice.IsChecked==true,"重新打开设置读取实际登记状态");
            Check(!main.Session.Dirty&&!File.Exists(main.Session.Target),"修改启动设置不改日记内容");
        }
        catch(Exception e){error=e;}
        finally{panel?.Window.Close();main?.ExitCard();DiarySession.AtomicText(Path.Combine(root,"startup-ui-result.json"),JsonSerializer.Serialize(new{passed=error==null,results,error=error?.ToString(),testUsedMemoryStore=true},CardConfig.Json));app.Shutdown();}
        return error==null?0:1;
    }
}
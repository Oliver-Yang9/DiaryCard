using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
namespace DailyDiaryCard;
// Explicit test flag only; never attached during ordinary startup.
public static class ModeSwitchProbe
{
    public static void Attach(MainWindow main,CardConfig config,string mode,string target)
    {
        if(!config.ConfigPath.Contains(Path.DirectorySeparatorChar+"local"+Path.DirectorySeparatorChar+"tests"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("模式切换验收只允许隔离测试目录。");
        main.ContentRendered+=(_,_)=>{
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(450)};
            timer.Tick+=(_,_)=>{
                timer.Stop();
                try
                {
                    string first=main.Session.Template[0].Title;main.Editors[first].Append("\n切换使用方式前保留的中文输入\n");
                    if(mode=="standalone")
                    {
                        var panel=new StandaloneSetupWindow(config.ConfigPath,main.ApplyVaultPlan,config,main.CurrentTheme){Owner=main};
                        panel.Show();panel.LibraryInput.Text=target;
                        if(File.Exists(Path.Combine(target,SetupService.StandaloneMarker)))panel.Detect();
                        if(!panel.ApplySelection())throw new IOException(panel.ErrorText.Text);
                    }
                    else if(mode=="obsidian")
                    {
                        var panel=main.CreateVaultSettingsDialog();panel.Show();panel.VaultInput.Text=target;panel.Detect();
                        if(!panel.ApplySelection())throw new IOException(panel.ErrorText.Text);
                    }
                    else throw new IOException("无法识别模式切换测试参数。");
                }
                catch(Exception e){DiarySession.AtomicText(Path.Combine(config.UserDataPath,"mode-switch-failure.txt"),e.ToString());main.ExitCard();}
            };timer.Start();
        };
    }
}
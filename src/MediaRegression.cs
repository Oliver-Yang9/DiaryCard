using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DailyDiaryCard;

internal static class MediaRegression
{
    public static void Start(MainWindow window,CardConfig config,string fixtures,string output,Action<bool,string> assert,Action<Exception?> done)
    {
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};var watch=Stopwatch.StartNew();
        int phase=0;long frame=0;TimeSpan position=TimeSpan.Zero;Task<bool>? request=null;MainWindow? reopened=null;
        string gifName="",videoName="";var original=window.Session.LocalDocument();var results=new List<string>();
        void Check(bool ok,string name){assert(ok,name);results.Add(name);}
        void Next(){phase++;watch.Restart();}
        void Stop(Exception? error)
        {
            timer.Stop();reopened?.ExitCard();
            DiarySession.AtomicText(Path.Combine(output,"media-result.json"),JsonSerializer.Serialize(new{passed=error==null,results,error=error?.ToString()},CardConfig.Json));done(error);
        }
        timer.Tick+=(_,_)=>
        {
            try
            {
                if(watch.ElapsedMilliseconds>18000)throw new Exception("动态背景验证等待超时。"+window.BackgroundFailure);
                switch(phase)
                {
                    case 0:
                        var settings=window.CreateThemeLibrary();settings.Show();settings.UpdateLayout();
                        Check(DockNative.SettingsCaptionValid(settings),"主题库顶部没有最小化按钮，仍保留边框缩放");
                        Check(window.BlockEdgeCollapse,"设置窗口打开时卡片保持展开");
                        UiSmoke.Capture(settings,Path.Combine(output,"18-new-background-settings.png"));settings.Close();
                        var transparency=window.CreateTransparencyDialog();transparency.Show();
                        Check(DockNative.SettingsCaptionValid(transparency),"栏目透明度窗口也没有最小化按钮");
                        var slider=((StackPanel)transparency.Content).Children.OfType<Slider>().Single();slider.Value=0.65;transparency.Close();
                        Check(Math.Abs(window.SectionTransparency-0.65)<0.001&&Math.Abs(Preferences.Load(config.UserDataPath).SectionTransparency-0.65)<0.001,"栏目透明度按钮及滑块实时生效并持久保存");
                        Check(((SolidColorBrush)Application.Current.Resources["SectionSurface"]).Color.A<110&&((SolidColorBrush)Application.Current.Resources["Text"]).Color.A==255,"仅栏目底色变透明，文字不变淡");
                        window.ApplyTheme("paper");Check(Math.Abs(window.SectionTransparency-0.65)<0.001,"换主题保留栏目透明度");window.ApplyTheme("fluent");
                        var selected=Path.Combine(output,"selected-motion.gif");File.Copy(Path.Combine(fixtures,"motion.gif"),selected,true);
                        Check(window.ImportBackground(selected)&&window.BackgroundPlayback.Kind=="GIF","GIF 作为动态背景成功导入");File.Delete(selected);gifName=window.BackgroundFile;
                        window.SetDynamicBackgroundPlaying(true);frame=window.BackgroundPlayback.FrameCounter;Next();break;
                    case 1:
                        if(window.BackgroundPlayback.FrameCounter-frame<3||window.BackgroundPlayback.LoopCounter<1)break;
                        Check(true,"GIF 实际切换多帧并循环播放，删除原图后仍可使用");
                        window.Capture(Path.Combine(output,"19-gif-transparent-sections.png"));
                        using(var unlocked=new FileStream(LocalBackground.Resolve(config.UserDataPath,gifName),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(unlocked.Length>0,"GIF 播放不锁住本机副本");
                        window.SetDynamicBackgroundPlaying(false);frame=window.BackgroundPlayback.FrameCounter;Next();break;
                    case 2:
                        if(watch.ElapsedMilliseconds<350)break;
                        Check(window.BackgroundPlayback.FrameCounter==frame&&!window.BackgroundPlayback.IsPlaying,"暂停按钮冻结 GIF 帧，不丢失背景");
                        window.SetDynamicBackgroundPlaying(true);window.HideCard();frame=window.BackgroundPlayback.FrameCounter;Next();break;
                    case 3:
                        if(watch.ElapsedMilliseconds<350)break;
                        Check(window.BackgroundPlayback.FrameCounter==frame&&!window.BackgroundPlayback.IsPlaying,"隐藏卡片后 GIF 自动暂停");window.ShowCard();Next();break;
                    case 4:
                        if(window.BackgroundPlayback.FrameCounter==frame)break;
                        Check(window.BackgroundPlayback.IsPlaying,"恢复卡片后 GIF 继续播放");
                        reopened=new MainWindow(config,()=>window.Session.Date);reopened.Show();
                        Check(reopened.BackgroundPlayback.Kind=="GIF"&&Math.Abs(reopened.SectionTransparency-0.65)<0.001,"重新打开后恢复 GIF 及栏目透明度");reopened.ExitCard();reopened=null;
                        request=window.ImportBackgroundAsync(Path.Combine(fixtures,"flower.mp4"));Next();break;
                    case 5:
                        if(!request!.IsCompleted)break;
                        Check(request.GetAwaiter().GetResult()&&window.BackgroundPlayback.VideoReady,"MP4 在当前 Windows 上实际解码并载入："+window.BackgroundFailure);
                        videoName=window.BackgroundFile;position=window.BackgroundPlayback.Position;Next();break;
                    case 6:
                        if(window.BackgroundPlayback.Position-position<TimeSpan.FromMilliseconds(200))break;
                        Check(window.BackgroundPlayback.IsPlaying,"MP4 播放进度实际前进，使用静音循环背景");window.Capture(Path.Combine(output,"20-mp4-background.png"));
                        window.HideCard();position=window.BackgroundPlayback.Position;Next();break;
                    case 7:
                        if(watch.ElapsedMilliseconds<400)break;
                        Check(!window.BackgroundPlayback.IsPlaying&&Math.Abs((window.BackgroundPlayback.Position-position).TotalMilliseconds)<150,"隐藏卡片暂停视频，停止后台播放进度");window.ShowCard();Next();break;
                    case 8:
                        if(window.BackgroundPlayback.LoopCounter<1)break;
                        Check(true,"MP4 播放结束后实际重新循环");
                        window.Docking.Arm(DockEdge.Top);System.Windows.Input.Keyboard.ClearFocus();Check(window.Docking.Collapse(),"MP4 背景可以正常触发贴边滑出动画");window.Docking.FinishAnimation();
                        Check(window.Docking.Collapsed&&!window.BackgroundPlayback.IsPlaying,"视频随贴边收起自动暂停");window.ShowCard();
                        var invalid=Path.Combine(output,"invalid.mp4");File.WriteAllBytes(invalid,new byte[]{0,0,0,24,102,116,121,112,105,115,111,109,0,0,0,0,105,115,111,109,97,118,99,49});
                        request=window.ImportBackgroundAsync(invalid);Next();break;
                    case 9:
                        if(!request!.IsCompleted)break;
                        Check(!request.GetAwaiter().GetResult()&&window.BackgroundFile==videoName&&window.BackgroundPlayback.VideoReady,"无效或不支持的视频载入失败时保留原背景与设置");
                        File.Delete(Path.Combine(output,"invalid.mp4"));
                        reopened=new MainWindow(config,()=>window.Session.Date);reopened.Show();Next();break;
                    case 10:
                        if(!reopened!.BackgroundPlayback.VideoReady)break;
                        Check(reopened.BackgroundPlayback.IsPlaying,"重新打开窗口后恢复 MP4 播放");reopened.ExitCard();reopened=null;
                        Check(window.RemoveBackground()&&!window.HasPictureBackground,"移除视频关闭播放器并恢复主题底色");
                        Check(window.ImportBackground(Path.Combine(fixtures,"motion.gif")),"可从视频重新切换为 GIF 背景");
                        window.SetSectionTransparency(0.35);window.SetDynamicBackgroundPlaying(true);window.ApplyTheme("fluent");
                        window.Docking.Arm(DockEdge.Right);System.Windows.Input.Keyboard.ClearFocus();Check(window.Docking.Collapse(),"GIF 背景可以正常触发贴边滑出动画");frame=window.BackgroundPlayback.FrameCounter;Next();break;
                    case 11:
                        if(window.Docking.IsAnimating)break;
                        Check(window.Docking.Collapsed&&window.BackgroundPlayback.FrameCounter==frame&&!window.BackgroundPlayback.IsPlaying,"GIF 在贴边动画及收起期间停止推进帧");window.ShowCard();Next();break;
                    case 12:
                        if(window.BackgroundPlayback.FrameCounter==frame)break;
                        Check(window.BackgroundPlayback.IsPlaying,"贴边恢复后 GIF 自动继续播放");
                        Check(window.Session.LocalDocument()==original,"透明度、GIF、视频和设置操作均不改写日记内容");Stop(null);break;
                }
            }
            catch(Exception e){Stop(e);}
        };
        timer.Start();
    }
}

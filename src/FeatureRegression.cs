using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DailyDiaryCard;

public static class FeatureRegression
{
    public static void Run(MainWindow window,CardConfig config,string output,Action<bool,string> assert,Action<DateOnly> setDay)
    {
        var snapshot=window.Session.LocalDocument();var selected=Path.Combine(output,"selected-image.png");
        MakeBackground(selected);var sample=Path.Combine(output,"sample-background.png");File.Copy(selected,sample,true);
        assert(window.ImportBackground(selected)&&window.HasPictureBackground,"图片背景：本机 PNG 成功导入并显示");
        var name=window.BackgroundFile;var managed=LocalBackground.Resolve(config.UserDataPath,name);
        File.Delete(selected);
        assert(File.Exists(managed)&&window.HasPictureBackground,"原始图片删除后仍使用独立本机副本");
        using(var unlocked=new FileStream(managed,FileMode.Open,FileAccess.ReadWrite,FileShare.None))assert(unlocked.Length>0,"图片载入后不锁定本机副本文件");
        assert(Preferences.Load(config.UserDataPath).BackgroundImage==name,"图片背景选择持久保存");
        window.SetBackgroundOpacity(0.8);assert(Math.Abs(Preferences.Load(config.UserDataPath).BackgroundOpacity-0.8)<0.001,"背景显现度调节并保存");
        window.ApplyTheme("paper");assert(window.HasPictureBackground&&window.Session.LocalDocument()==snapshot,"切换主题保留图片、正文和任务");
        window.ApplyTheme("fluent");window.Capture(Path.Combine(output,"11-local-background.png"));
        var library=window.CreateThemeLibrary();library.Height=720;library.Show();library.UpdateLayout();UiSmoke.Capture(library,Path.Combine(output,"12-background-settings.png"));library.Close();
        var invalid=Path.Combine(output,"invalid.png");File.WriteAllText(invalid,"这不是图片");
        assert(!window.ImportBackground(invalid)&&window.BackgroundFile==name&&window.Session.LocalDocument()==snapshot,"无效图片不替换现有背景，不影响输入");
        File.Delete(invalid);
        var large=Path.Combine(output,"too-large.png");using(var f=File.Create(large))f.SetLength(LocalBackground.MaxBytes+1);
        assert(!window.ImportBackground(large)&&window.BackgroundFile==name,"超出 20 MB 的图片清楚拒绝且保留背景");File.Delete(large);
        bool rejected=false;try{_ =LocalBackground.Resolve(config.UserDataPath,"../outside.png");}catch(IOException){rejected=true;}
        assert(rejected,"背景设置不能指向本机副本目录以外的路径");
        var originalBytes=File.ReadAllBytes(managed);File.Delete(managed);
        var missing=new MainWindow(config,()=>window.Session.Date);missing.Show();assert(!missing.HasPictureBackground,"背景副本缺失时窗口可启动并回退主题底色");missing.ExitCard();
        File.WriteAllBytes(managed,originalBytes);
        var restored=new MainWindow(config,()=>window.Session.Date);restored.Show();assert(restored.HasPictureBackground&&Math.Abs(restored.BackgroundOpacity-0.8)<0.001,"重新打开窗口恢复图片与显现度");restored.ExitCard();
        assert(window.RemoveBackground()&&!window.HasPictureBackground&&Preferences.Load(config.UserDataPath).BackgroundImage=="","移除图片恢复主题底色并保存");
        assert(window.ImportBackground(sample),"审核截图示例图片可重新应用");

        var work=new PixelRect(-1920,-200,1920,1040);var rect=new PixelRect(-1914,100,600,700);
        assert(DockGeometry.Detect(rect,work,1)==DockEdge.Left,"负坐标副屏：识别左侧停靠");
        assert(DockGeometry.Detect(new(-1300,-180,500,500),work,2)==DockEdge.Top,"高 DPI：吸附阈值按缩放计算");
        foreach(var edge in Enum.GetValues<DockEdge>().Where(e=>e!=DockEdge.None))
        {
            var snapped=DockGeometry.Snap(rect,work,edge);var tab=DockGeometry.Tab(snapped,work,edge,1.5);
            assert(work.Contains(tab.Left,tab.Top)&&tab.Right<=work.Right&&tab.Bottom<=work.Bottom,$"{edge}：入口位于该显示器工作区，不伸到相邻屏幕或任务栏");
        }
        var docking=window.Docking;docking.AutomaticPolling=false;window.SetEdgeAutoHide(true);
        window.Width=460;window.Height=620;window.UpdateLayout();Keyboard.ClearFocus();
        var nativeWork=DockNative.Work(window);var nativeBounds=DockNative.Bounds(window);
        DockNative.Place(window,new(nativeWork.Left+5,nativeWork.Top+60,nativeBounds.Width,nativeBounds.Height));
        docking.BeginMove();docking.CompleteMove(true);
        assert(docking.Edge==DockEdge.Left&&DockNative.Bounds(window).Left==nativeWork.Left,"结束拖动时识别贴边并吸附到实际显示器左边");
        docking.BeginMove();
        assert(docking.Edge==DockEdge.None&&!docking.Collapsed,"开始拖离边缘即解除原停靠状态");
        docking.CompleteMove(false);
        var records=new List<object>();
        foreach(var edge in Enum.GetValues<DockEdge>().Where(e=>e!=DockEdge.None))
        {
            docking.Arm(edge);Keyboard.ClearFocus();
            var now=DateTime.UtcNow;var inside=DockNative.Bounds(window);
            docking.Poll(now,nativeWork.Right+100,nativeWork.Bottom+100);
            docking.Poll(now.AddMilliseconds(150),inside.Left+inside.Width/2,inside.Top+inside.Height/2);
            assert(!docking.Collapsed,$"{edge}：短暂移出后返回不会误收起");
            docking.Poll(now.AddMilliseconds(200),nativeWork.Right+100,nativeWork.Bottom+100);
            docking.Poll(now.AddMilliseconds(500),nativeWork.Right+100,nativeWork.Bottom+100);
            assert(!docking.Collapsed,$"{edge}：返回后重新计算等待时间，不立即收起");
            docking.Poll(now.AddMilliseconds(650),nativeWork.Right+100,nativeWork.Bottom+100);
            docking.FinishAnimation();
            if(!docking.Collapsed||window.IsVisible||docking.TabWindow is not {IsVisible:true})DiarySession.AtomicText(Path.Combine(output,"edge-diagnostic.json"),JsonSerializer.Serialize(new{edge,visible=window.IsVisible,collapsed=docking.Collapsed,tabVisible=docking.TabWindow?.IsVisible,blocked=window.BlockEdgeCollapse,focus=Keyboard.FocusedElement?.GetType().Name,active=window.IsActive,status=window.Status,owned=window.OwnedWindows.Count,moving=docking.Moving,state=window.WindowState.ToString(),bounds=DockNative.Bounds(window),work=nativeWork},CardConfig.Json));
            assert(docking.Collapsed&&!window.IsVisible&&docking.TabWindow is {IsVisible:true},$"{edge}：鼠标离开后自动保存并收起，保留可见入口");
            var tab=DockNative.Bounds(docking.TabWindow!);
            assert(nativeWork.Contains(tab.Left,tab.Top)&&tab.Right<=nativeWork.Right&&tab.Bottom<=nativeWork.Bottom,$"{edge}：实际窗口入口位于工作区内");
            UiSmoke.Capture(docking.TabWindow!,Path.Combine(output,$"13-edge-{edge.ToString().ToLowerInvariant()}-tab.png"));
            records.Add(new{edge=edge.ToString(),work=nativeWork,tab,scale=DockNative.Scale(window)});
            docking.Poll(now.AddSeconds(2),tab.Left+tab.Width/2,tab.Top+tab.Height/2);
            docking.Poll(now.AddMilliseconds(2070),tab.Left+tab.Width/2,tab.Top+tab.Height/2);
            assert(docking.Collapsed,$"{edge}：鼠标短暂扫过入口不立即展开");
            docking.Poll(now.AddMilliseconds(2120),tab.Left+tab.Width/2,tab.Top+tab.Height/2);
            docking.FinishAnimation();
            assert(!docking.Collapsed&&window.IsVisible&&window.Session.LocalDocument()==snapshot,$"{edge}：悬停入口自动展开，正文保持不变");
            window.UpdateLayout();if(edge==DockEdge.Right)window.Capture(Path.Combine(output,"14-edge-expanded.png"));
        }
        DiarySession.AtomicText(Path.Combine(output,"edge-result.json"),JsonSerializer.Serialize(records,CardConfig.Json));
        var first=window.Session.Template[0].Title;
        docking.Arm(DockEdge.Left);window.Activate();window.Editors[first].TextInputs[0].Focus();
        assert(window.BlockEdgeCollapse&&!docking.Collapse(),"在正文中保持输入焦点时不自动收起");Keyboard.ClearFocus();
        var modal=new Window {Owner=window,Width=240,Height=160,ShowInTaskbar=false};modal.Show();
        assert(!docking.Collapse(),"主题或其他子窗口打开期间不自动收起");modal.Close();Keyboard.ClearFocus();
        window.Editors[first].Append("\n贴边收起之前的中文输入。\n");
        using(var locked=new FileStream(window.Session.Target,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            assert(!docking.Collapse()&&window.IsVisible&&window.Session.Dirty,"贴边保存失败保留展开窗口和待保存输入");
        assert(window.TrySave()&&docking.Collapse(),"解除文件占用后可保存并收起");
        window.SetEdgeAutoHide(false);
        assert(window.IsVisible&&!docking.Collapsed&&docking.Edge==DockEdge.None&&!Preferences.Load(config.UserDataPath).EdgeAutoHide,"关闭贴边功能立即展开并记住选择");
        window.SetEdgeAutoHide(true);docking.Arm(DockEdge.Left);assert(docking.Collapse(),"贴边功能可重新启用");
        assert(window.HideCard()&&!window.IsVisible&&docking.TabWindow is {IsVisible:false},"隐藏至托盘同时移除边缘入口");
        window.ShowCard();assert(window.IsVisible&&!docking.Collapsed&&docking.Edge==DockEdge.None,"托盘恢复展开并取消本次停靠，避免立即缩回");

        var previous=window.Session.Date;var oldHash=DiarySession.Hash(DiarySession.ReadText(window.Session.Target));
        var next=previous.AddDays(1);var target=config.PathFor(next);Directory.CreateDirectory(target);setDay(next);
        assert(!window.CheckDay()&&window.Session.Date==previous&&window.IsVisible,"新一天建档失败时保留旧日期会话并显示失败");
        assert(DiarySession.Hash(DiarySession.ReadText(window.Session.Target))==oldHash,"建档失败不把新一天内容写入上一天");
        Directory.Delete(target);assert(window.CheckDay()&&File.Exists(window.Session.Target)&&!window.Session.Dirty,"解除路径阻塞后自动创建并匹配当天模板");
        var after=next.AddDays(1);var path=config.PathFor(after);var template=DiarySession.ReadText(config.TemplatePath).Replace("{{date:YYYY-MM-DD}}",after.ToString("yyyy-MM-dd"));var parsed=new MarkdownDoc(template);var second=parsed.Sections[1].Title;
        var existing=parsed.Apply(new(){{second,"\nObsidian 里已填写的新一天内容。\n\n"}},parsed.Sections);File.WriteAllText(path,existing,DiarySession.Utf8);setDay(after);
        assert(window.CheckDay()&&window.Editors[second].Body.Contains("已填写的新一天"),"跨天发现已有日记时自动匹配并载入现有栏目");
        assert(DiarySession.ReadText(path)==existing,"跨天匹配已有日记时不覆盖原文");
        window.ApplyTheme("fluent");
    }
    public static void MakeBackground(string file)
    {
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(132,175,211),Color.FromRgb(242,214,174),new Point(0,0),new Point(0,1)),null,new Rect(0,0,900,1300));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(155,255,245,213)),null,new Point(710,240),130,130);
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(130,167,171)),null,Geometry.Parse("M 0,1000 L 180,720 L 390,940 L 600,650 L 900,1000 L 900,1300 L 0,1300 Z"));
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(86,131,149)),null,Geometry.Parse("M 0,1160 L 240,920 L 500,1090 L 750,860 L 900,1040 L 900,1300 L 0,1300 Z"));
        }
        var bitmap=new RenderTargetBitmap(900,1300,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);encoder.Save(stream);
    }
}

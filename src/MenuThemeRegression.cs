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

internal static class MenuThemeRegression
{
    private static Color ColorOf(Brush brush)=>((SolidColorBrush)brush).Color;
    private static bool Dark(Brush brush){var c=ColorOf(brush);return c.A==255&&c.R<65&&c.G<65&&c.B<65;}
    public static void Start(MainWindow window,string output,Action<bool,string> assert,Action<Exception?> done)
    {
        var original=window.Session.LocalDocument();var results=new List<string>();int phase=0;
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(120)};
        void Check(bool ok,string name){assert(ok,name);results.Add(name);}
        void Finish(Exception? error)
        {
            timer.Stop();window.MoreMenu.IsOpen=false;window.ApplyTheme("fluent");
            DiarySession.AtomicText(Path.Combine(output,"menu-theme-result.json"),JsonSerializer.Serialize(new{passed=error==null,results,error=error?.ToString()},CardConfig.Json));done(error);
        }
        timer.Tick+=(_,_)=>
        {
            try
            {
                var menu=window.MoreMenu;
                switch(phase++)
                {
                    case 0: window.ApplyTheme("night");window.Activate();window.MoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));break;
                    case 1:
                        Check(menu.IsOpen&&menu.ActualWidth>0&&menu.ActualHeight>0,"点击右上角三个点，实际弹出更多菜单");menu.ApplyTemplate();menu.UpdateLayout();
                        Check(Dark(menu.Background)&&ColorOf(menu.Foreground).R>225,"更多菜单为不透明黑底白字");
                        var surface=(Border)menu.Template.FindName("MenuSurface",menu);
                        Check(Dark(surface.Background),"实际菜单模板底色已变黑，移除系统浅色底与白色侧栏");
                        var items=menu.Items.OfType<MenuItem>().ToArray();foreach(var item in items)item.ApplyTemplate();
                        Check(items.All(item=>ColorOf(item.Foreground).R>225),"菜单全部条目的文字为浅色");
                        var edge=items.Single(i=>Equals(i.Header,"贴边自动收起"));
                        var mark=(TextBlock)edge.Template.FindName("Checkmark",edge);
                        Check(mark.Visibility==Visibility.Visible&&ColorOf(mark.Foreground).R>225,"菜单中已启用项的勾选标记清晰可见");
                        menu.Focus();items[0].Focus();break;
                    case 2:
                        var first=menu.Items.OfType<MenuItem>().First();var row=(Border)first.Template.FindName("MenuRow",first);
                        Check(Dark(row.Background)&&ColorOf(first.Foreground).R>225,"键盘焦点或高亮行保持深底浅字");
                        Check(window.BlockEdgeCollapse,"更多菜单打开期间卡片保持展开");
                        Capture(menu,Path.Combine(output,"25-dark-more-menu.png"));window.ApplyTheme("paper");break;
                    case 3:
                        Check(menu.IsOpen&&ColorOf(menu.Background).R>200&&ColorOf(menu.Foreground).R<120,"切回浅色时已经打开的弹出菜单即时更新");
                        window.ApplyTheme("night");break;
                    case 4:
                        Check(Dark(menu.Background),"切回深色时弹出菜单即时恢复黑底");
                        var expand=menu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,"展开全部栏目"));expand.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        Check(window.Session.LocalDocument()==original,"菜单操作仍可执行且不改写日记内容");
                        menu.IsOpen=false;Check(!menu.IsOpen,"更多菜单可正常关闭");Finish(null);break;
                }
            }
            catch(Exception e){Finish(e);}
        };
        timer.Start();
    }
    private static void Capture(FrameworkElement visual,string path)
    {
        visual.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(visual);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(visual.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}

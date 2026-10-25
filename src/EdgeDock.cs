using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DailyDiaryCard;

public enum DockEdge { None,Left,Right,Top,Bottom }
public readonly record struct PixelRect(int Left,int Top,int Width,int Height)
{
    public int Right=>Left+Width;
    public int Bottom=>Top+Height;
    public bool Contains(int x,int y)=>x>=Left&&x<Right&&y>=Top&&y<Bottom;
}
public static class DockGeometry
{
    public static DockEdge Detect(PixelRect r,PixelRect work,double scale)
    {
        int threshold=(int)Math.Ceiling(12*scale);
        var choices=new[]{(DockEdge.Top,Math.Max(0,r.Top-work.Top)),(DockEdge.Left,Math.Max(0,r.Left-work.Left)),(DockEdge.Right,Math.Max(0,work.Right-r.Right)),(DockEdge.Bottom,Math.Max(0,work.Bottom-r.Bottom))};
        return choices.Where(c=>c.Item2<=threshold).OrderBy(c=>c.Item2).Select(c=>c.Item1).FirstOrDefault();
    }
    public static PixelRect Snap(PixelRect r,PixelRect w,DockEdge edge)
    {
        int width=Math.Min(r.Width,w.Width),height=Math.Min(r.Height,w.Height);
        int x=Math.Clamp(r.Left,w.Left,w.Right-width),y=Math.Clamp(r.Top,w.Top,w.Bottom-height);
        if(edge==DockEdge.Left)x=w.Left;if(edge==DockEdge.Right)x=w.Right-width;
        if(edge==DockEdge.Top)y=w.Top;if(edge==DockEdge.Bottom)y=w.Bottom-height;
        return new(x,y,width,height);
    }
    public static PixelRect Tab(PixelRect card,PixelRect w,DockEdge edge,double scale)
    {
        int thin=(int)Math.Ceiling(22*scale),length=(int)Math.Ceiling(104*scale);
        bool vertical=edge is DockEdge.Left or DockEdge.Right;
        int width=Math.Min(vertical?thin:length,w.Width),height=Math.Min(vertical?length:thin,w.Height);
        int x=vertical?(edge==DockEdge.Left?w.Left:w.Right-width):Math.Clamp(card.Left+(card.Width-width)/2,w.Left,w.Right-width);
        int y=vertical?Math.Clamp(card.Top+(int)Math.Ceiling(54*scale),w.Top,w.Bottom-height):(edge==DockEdge.Top?w.Top:w.Bottom-height);
        return new(x,y,width,height);
    }
}

// Reads only this app's handles, the cursor position, and the containing monitor's work area.
public static class DockNative
{
    [StructLayout(LayoutKind.Sequential)]private struct Rect {public int Left,Top,Right,Bottom;public PixelRect Value=>new(Left,Top,Right-Left,Bottom-Top);}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)]private struct CursorPoint {public int X,Y;}
    [DllImport("user32.dll",SetLastError=true)]private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    public static IntPtr Handle(Window window)=>new WindowInteropHelper(window).Handle;
    public static PixelRect Bounds(Window w){if(!GetWindowRect(Handle(w),out var r))throw new Win32Exception();return r.Value;}
    public static PixelRect Work(Window w)
    {
        var info=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromWindow(Handle(w),2),ref info))throw new Win32Exception();return info.Work.Value;
    }
    public static double Scale(Window w)=>Math.Max(96,GetDpiForWindow(Handle(w)))/96.0;
    public static (int X,int Y) Cursor(){if(!GetCursorPos(out var p))return (int.MinValue,int.MinValue);return(p.X,p.Y);}
    public static void Place(Window w,PixelRect r,bool raise=false)
    {
        // Physical pixels keep negative monitor origins and mixed DPI out of WPF's virtual coordinates.
        if(!SetWindowPos(Handle(w),w.Topmost?new IntPtr(-1):IntPtr.Zero,r.Left,r.Top,r.Width,r.Height,0x0010u|(raise?0u:0x0004u)))throw new Win32Exception();
    }
    public static void CustomDockStyle(Window w,bool enabled)
    {
        var hwnd=Handle(w);if(hwnd==IntPtr.Zero)return;
        long original=GetWindowLongPtr(hwnd,-16).ToInt64();
        long style=enabled?original&~0x00010000L:original|0x00010000L;
        if(style==original)return;
        SetWindowLongPtr(hwnd,-16,new IntPtr(style));
        _=SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x0037); // Own window only; prevents Aero Snap from overriding edge docking.
    }
    internal static void PrepareSlide(Window overlay,Window owner,PixelRect bounds,bool behindOwner)
    {
        var hwnd=Handle(overlay);
        long style=GetWindowLongPtr(hwnd,-20).ToInt64();
        SetWindowLongPtr(hwnd,-20,new IntPtr(style|0x08000020L)); // NOACTIVATE | TRANSPARENT: animation never captures clicks or keyboard focus.
        var after=behindOwner?Handle(owner):owner.Topmost?new IntPtr(-1):IntPtr.Zero;
        if(!SetWindowPos(hwnd,after,bounds.Left,bounds.Top,bounds.Width,bounds.Height,0x0010))throw new Win32Exception();
    }
    internal static void SettingsCaption(Window window)
    {
        var hwnd=Handle(window);long style=GetWindowLongPtr(hwnd,-16).ToInt64();SetWindowLongPtr(hwnd,-16,new IntPtr(style&~0x00030000L));_=SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x0037);
    }
    internal static bool SettingsCaptionValid(Window window){long style=GetWindowLongPtr(Handle(window),-16).ToInt64();return (style&0x00030000L)==0&&(style&0x00040000L)!=0;}
}

public sealed class EdgeDock : IDisposable
{
    private readonly MainWindow owner;
    private readonly DispatcherTimer timer;
    private Window? tab;
    private DateTime? outsideSince,hoverSince;
    private bool disposed;
    private EdgeSlide? slide;
    private System.Windows.Media.Imaging.BitmapSource? lastSnapshot;
    public bool Enabled {get;private set;}
    public bool Collapsed {get;private set;}
    public bool Moving {get;private set;}
    public DockEdge Edge {get;private set;}
    public Window? TabWindow=>tab;
    public bool AutomaticPolling {get;set;}=true;
    public bool IsAnimating=>slide!=null;
    internal bool? AnimationOverride {get;set;}
    internal bool MotionEnabled=>AnimationOverride??SystemParameters.ClientAreaAnimation;
    internal double AnimationProgress=>slide?.Progress??(Collapsed?1:0);
    internal Window? AnimationWindow=>slide?.Window;
    public EdgeDock(MainWindow window,bool enabled)
    {
        owner=window;Enabled=enabled;
        timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(50)};
        timer.Tick+=(_,_)=>{if(!AutomaticPolling)return;try{var p=DockNative.Cursor();Poll(DateTime.UtcNow,p.X,p.Y);}catch(Exception e){Detach();owner.NotifyDockError(e.Message);}};timer.Start();
    }
    public void SetEnabled(bool enabled){Enabled=enabled;DockNative.CustomDockStyle(owner,enabled);if(!enabled)Detach();}
    public void BeginMove(){Detach();Moving=true;}
    public void CompleteMove(bool moved)
    {
        Moving=false;
        if(!moved||!Enabled||!owner.IsVisible||owner.WindowState!=WindowState.Normal)return;
        var bounds=DockNative.Bounds(owner);var work=DockNative.Work(owner);
        Arm(DockGeometry.Detect(bounds,work,DockNative.Scale(owner)));
    }
    public void Arm(DockEdge edge)
    {
        if(!Enabled||edge==DockEdge.None)return;
        if(IsAnimating)FinishAnimation();
        Edge=edge;outsideSince=null;hoverSince=null;
        DockNative.Place(owner,DockGeometry.Snap(DockNative.Bounds(owner),DockNative.Work(owner),edge));
    }
    public void Poll(DateTime now,int cursorX,int cursorY)
    {
        if(disposed||!Enabled||Moving||Edge==DockEdge.None)return;
        if(slide!=null)
        {
            if(slide.Closing&&DockNative.Bounds(owner).Contains(cursorX,cursorY))Expand();
            return;
        }
        if(Collapsed)
        {
            if(tab?.Content is Border b&&b.ContextMenu?.IsOpen==true){hoverSince=null;return;}
            if(tab is {IsVisible:true}&&DockNative.Bounds(tab).Contains(cursorX,cursorY))
            {hoverSince??=now;if((now-hoverSince.Value).TotalMilliseconds>=100)Expand();}
            else hoverSince=null;
            return;
        }
        if(!owner.IsVisible||owner.WindowState!=WindowState.Normal||DockNative.Bounds(owner).Contains(cursorX,cursorY)||owner.BlockEdgeCollapse)
        {outsideSince=null;return;}
        outsideSince??=now;
        if((now-outsideSince.Value).TotalMilliseconds>=400){outsideSince=null;Collapse();}
    }
    public bool Collapse()
    {
        if(disposed||!Enabled||Edge==DockEdge.None||Moving||Collapsed||IsAnimating||owner.WindowState!=WindowState.Normal||owner.BlockEdgeCollapse||!owner.TrySave())return false;
        owner.StoreWindowPreferences();
        EnsureTab();PositionTab();
        try
        {
            if(MotionEnabled)
            {
                lastSnapshot=owner.DockSnapshot();
                slide=new EdgeSlide(owner,Edge,lastSnapshot,true,CompleteSlide);
                slide.Show();Collapsed=true;owner.Hide();slide.Play(true);
            }
            else{Collapsed=true;owner.Hide();ShowTab();}
        }
        catch(Exception e){CancelSlide(true);owner.NotifyDockError("收起动画未能完成："+e.Message);return false;}
        return true;
    }
    public void Expand()
    {
        if(slide!=null)
        {
            if(slide.Closing){Collapsed=false;outsideSince=null;hoverSince=null;slide.Play(false);}
            return;
        }
        if(!Collapsed)return;
        Collapsed=false;outsideSince=null;hoverSince=null;tab?.Hide();
        try
        {
            if(MotionEnabled&&lastSnapshot!=null)
            {
                // Rendering this app's content visual also works while its native window is hidden.
                lastSnapshot=owner.DockSnapshot();
                slide=new EdgeSlide(owner,Edge,lastSnapshot,false,CompleteSlide);slide.Show();slide.Play(false);
            }
            else RestoreOwner();
        }
        catch(Exception e){CancelSlide(false);RestoreOwner();owner.NotifyDockError("展开动画未能完成："+e.Message);}
    }
    private void RestoreOwner()
    {
        Collapsed=false;tab?.Hide();
        bool activate=owner.ShowActivated;owner.ShowActivated=false;owner.Show();owner.ShowActivated=activate;
        DockNative.Place(owner,DockGeometry.Snap(DockNative.Bounds(owner),DockNative.Work(owner),Edge),true);
        owner.SynchronizeAfterEdgeExpand();lastSnapshot=null;
    }
    private void ShowTab(){tab!.Show();PositionTab();tab.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(70)));}
    private void CompleteSlide(bool closed)
    {
        var completed=slide;slide=null;
        try{if(closed){Collapsed=true;ShowTab();}else RestoreOwner();}
        catch(Exception e){RestoreOwner();owner.NotifyDockError("动画恢复失败："+e.Message);}
        finally{completed?.Dispose();}
    }
    private void CancelSlide(bool restore)
    {
        bool hidden=Collapsed||slide!=null;
        var current=slide;slide=null;current?.Dispose();
        if(restore&&hidden)RestoreOwner();
    }
    internal void FinishAnimation()=>slide?.Finish();
    internal void CaptureAnimation(string file)=>slide?.Capture(file);
    public void Detach()
    {
        CancelSlide(true);
        Edge=DockEdge.None;outsideSince=null;hoverSince=null;tab?.Hide();
    }
    public void HideToTray()
    {
        CancelSlide(false);lastSnapshot=null;
        Collapsed=false;Edge=DockEdge.None;outsideSince=null;hoverSince=null;tab?.Hide();
    }
    public void Reflow()
    {
        if(Edge==DockEdge.None)return;
        if(IsAnimating)FinishAnimation();
        DockNative.Place(owner,DockGeometry.Snap(DockNative.Bounds(owner),DockNative.Work(owner),Edge));
        if(Collapsed)PositionTab();
    }
    private void EnsureTab()
    {
        if(tab!=null)return;
        tab=new Window {Title="日记卡片 · 贴边入口",Width=22,Height=104,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=true};
        var border=new Border {CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(1),Cursor=Cursors.Hand};border.SetResourceReference(Border.BackgroundProperty,"Surface");border.SetResourceReference(Border.BorderBrushProperty,"Accent");
        var text=new TextBlock {FontSize=12,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};text.SetResourceReference(TextBlock.ForegroundProperty,"Accent");border.Child=text;tab.Content=border;
        tab.ToolTip="移入展开日记卡片；单击也可展开。";
        tab.MouseLeftButtonDown+=(_,_)=>Expand();
        var menu=new ContextMenu();
        var show=new MenuItem {Header="展开并取消本次贴边"};show.Click+=(_,_)=>owner.ShowCard();menu.Items.Add(show);
        var hide=new MenuItem {Header="隐藏至托盘"};hide.Click+=(_,_)=>owner.HideCard();menu.Items.Add(hide);
        var exit=new MenuItem {Header="保存并退出"};exit.Click+=(_,_)=>owner.ExitCard();menu.Items.Add(exit);border.ContextMenu=menu;Themes.StyleContextMenu(menu,owner.CurrentTheme);menu.Opened+=(_,_)=>Themes.StyleContextMenu(menu,owner.CurrentTheme);
        new WindowInteropHelper(tab).EnsureHandle();
    }
    private void PositionTab()
    {
        if(tab==null)return;
        ((TextBlock)((Border)tab.Content).Child).Text=Edge is DockEdge.Left or DockEdge.Right?"日\n记":"日记卡片";
        var rect=DockGeometry.Tab(DockNative.Bounds(owner),DockNative.Work(owner),Edge,DockNative.Scale(owner));
        DockNative.Place(tab,rect,true);
    }
    internal void RefreshMenuTheme(){if(tab?.Content is Border {ContextMenu:{ } menu})Themes.StyleContextMenu(menu,owner.CurrentTheme);}
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();CancelSlide(false);lastSnapshot=null;tab?.Close();}
}

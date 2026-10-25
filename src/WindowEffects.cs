using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace DailyDiaryCard;

public sealed record WindowEffectState(bool CompositionEnabled, bool ShadowConfigured, bool AcrylicConfigured, int BackdropResult, string Note);
public sealed record DialogEffectState(bool Dark,int DarkResult,int CaptionResult,int TextResult,int BorderResult);

// 只设置本程序自己的 HWND；不枚举其他窗口，不修改系统透明、隐私或安全设置。
public static class WindowEffects
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left,Right,Top,Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)]out bool enabled);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);

    public static WindowEffectState Apply(Window window,bool acrylic)
    {
        var hwnd=new WindowInteropHelper(window).Handle;
        if(hwnd==IntPtr.Zero)return new(false,false,false,0,"等待窗口初始化");
        try
        {
            if(DwmIsCompositionEnabled(out bool composition)!=0||!composition)
                return new(false,false,false,-1,"系统未提供桌面合成，使用清晰实色背景");
            int enabled=2; // DWMNCRP_ENABLED：保留系统非客户区与阴影。
            _=DwmSetWindowAttribute(hwnd,2,ref enabled,sizeof(int));
            int round=2; // DWMWCP_ROUND，旧系统会返回不支持，不影响运行。
            _=DwmSetWindowAttribute(hwnd,33,ref round,sizeof(int));
            int dark=Themes.Get(PreferencesTheme(window)).Id=="night"?1:0;
            _=DwmSetWindowAttribute(hwnd,20,ref dark,sizeof(int));
            bool supported=Environment.OSVersion.Version.Build>=22621&&!SystemParameters.HighContrast;
            int kind=acrylic&&supported?3:1; // DWMSBT_TRANSIENTWINDOW / DWMSBT_NONE。
            int result=DwmSetWindowAttribute(hwnd,38,ref kind,sizeof(int));
            bool useGlass=acrylic&&supported&&result==0;
            var chrome=WindowChrome.GetWindowChrome(window);
            if(chrome!=null)chrome.GlassFrameThickness=useGlass?new Thickness(-1):new Thickness(1);
            var margins=new Margins {Left=useGlass?-1:1,Right=useGlass?-1:1,Top=useGlass?-1:1,Bottom=useGlass?-1:1};
            int shadowResult=DwmExtendFrameIntoClientArea(hwnd,ref margins);
            if(HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)target.BackgroundColor=Colors.Transparent;
            return new(true,shadowResult==0,useGlass,result,
                useGlass?"Windows 磨砂半透明；跟随系统透明效果":acrylic?"当前系统不支持磨砂，已回退清晰实色背景":"系统浮窗阴影与圆角");
        }
        catch(Exception e) when(e is DllNotFoundException or EntryPointNotFoundException)
        { return new(false,false,false,-1,"窗口效果不可用，已回退清晰实色背景"); }
    }
    private static string PreferencesTheme(Window window)=>window is MainWindow card?card.CurrentTheme:"fluent";
    public static DialogEffectState ApplyDialog(Window window,bool dark)
    {
        var hwnd=new WindowInteropHelper(window).Handle;
        if(hwnd==IntPtr.Zero)return new(dark,-1,-1,-1,-1);
        int ColorRef(string key){var c=((SolidColorBrush)window.Resources[key]).Color;return c.R|(c.G<<8)|(c.B<<16);}
        int enabled=dark?1:0,caption=ColorRef("WindowBg"),text=ColorRef("Text"),border=ColorRef("Line");
        try{return new(dark,DwmSetWindowAttribute(hwnd,20,ref enabled,4),DwmSetWindowAttribute(hwnd,35,ref caption,4),DwmSetWindowAttribute(hwnd,36,ref text,4),DwmSetWindowAttribute(hwnd,34,ref border,4));}
        catch(Exception e) when(e is DllNotFoundException or EntryPointNotFoundException){return new(dark,-1,-1,-1,-1);}
    }

    public static int HitTest(Window window,Point clientPoint)
    {
        var p=window.PointToScreen(clientPoint);
        int packed=unchecked(((int)p.Y&0xffff)<<16|((int)p.X&0xffff));
        return SendMessage(new WindowInteropHelper(window).Handle,0x0084,IntPtr.Zero,new IntPtr(packed)).ToInt32();
    }
}

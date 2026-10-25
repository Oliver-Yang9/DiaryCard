using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace DailyDiaryCard;

// A short-lived visual of this app alone. It stays within the original card bounds,
// so shared monitor edges never reveal a moving window on the adjacent screen.
internal sealed class EdgeSlide : IDisposable
{
    private readonly MainWindow owner;
    private readonly PixelRect bounds;
    private readonly TranslateTransform translation=new();
    private readonly Image image;
    private readonly double distance;
    private readonly System.Windows.DependencyProperty axis;
    private readonly Action<bool> completed;
    private bool disposed;
    private int generation;
    public Window Window {get;}
    public bool Closing {get;private set;}
    public double Progress=>Math.Clamp((axis==TranslateTransform.XProperty?translation.X:translation.Y)/distance,0,1);
    public EdgeSlide(MainWindow card,DockEdge edge,BitmapSource snapshot,bool closing,Action<bool> onCompleted)
    {
        owner=card;bounds=DockNative.Bounds(card);completed=onCompleted;Closing=closing;
        bool horizontal=edge is DockEdge.Left or DockEdge.Right;
        axis=horizontal?TranslateTransform.XProperty:TranslateTransform.YProperty;
        distance=(horizontal?card.ActualWidth:card.ActualHeight)*(edge is DockEdge.Left or DockEdge.Top?-1:1);
        if(!closing)translation.SetValue(axis,distance);
        image=new Image {Source=snapshot,Stretch=Stretch.Fill,RenderTransform=translation,IsHitTestVisible=false};
        var frame=new Grid {ClipToBounds=true,Background=Brushes.Transparent};frame.Children.Add(image);
        Window=new Window {Title="日记卡片 · 贴边动画",Width=card.ActualWidth,Height=card.ActualHeight,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=card.Topmost,Content=frame};
    }
    public void Show()
    {
        Window.Show();DockNative.PrepareSlide(Window,owner,bounds,Closing);Window.UpdateLayout();
    }
    public void Play(bool closing)
    {
        if(disposed)return;
        double current=axis==TranslateTransform.XProperty?translation.X:translation.Y;
        translation.BeginAnimation(axis,null);translation.SetValue(axis,current);
        Closing=closing;int token=++generation;
        double destination=closing?distance:0;
        double fraction=Math.Abs(destination-current)/Math.Abs(distance);
        var duration=TimeSpan.FromMilliseconds(Math.Max(60,(closing?550:500)*fraction));
        var animation=new DoubleAnimation(current,destination,duration) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.HoldEnd};
        animation.Completed+=(_,_)=>{if(!disposed&&token==generation)completed(Closing);};
        translation.BeginAnimation(axis,animation);
    }
    public void Finish()
    {
        if(disposed)return;
        generation++;translation.BeginAnimation(axis,null);translation.SetValue(axis,Closing?distance:0);completed(Closing);
    }
    public void Capture(string file)
    {
        Window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(Window);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(Window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(Window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render((Visual)Window.Content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(file);encoder.Save(f);
    }
    public void Dispose(){if(disposed)return;disposed=true;generation++;translation.BeginAnimation(axis,null);image.Source=null;Window.Close();}
}

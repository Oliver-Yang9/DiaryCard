using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DailyDiaryCard;

internal sealed class RasterBackground : IDisposable
{
    public BitmapSource Poster {get;}
    public GifBackground? Gif {get;}
    public RasterBackground(string directory,string name)
    {
        if(Path.GetExtension(name).Equals(".gif",StringComparison.OrdinalIgnoreCase))
        {
            var path=LocalBackground.Resolve(directory,name);
            if(new FileInfo(path).Length>LocalBackground.MaxBytes)throw new IOException("GIF 超过 20 MB。");
            Gif=new GifBackground(File.ReadAllBytes(path));Poster=Gif.Frame();
        }
        else Poster=LocalBackground.Load(directory,name);
    }
    public void Dispose()=>Gif?.Dispose();
}

internal sealed class BackgroundPlayer : Grid,IDisposable
{
    private readonly Image image=new() {Stretch=Stretch.UniformToFill};
    private readonly DispatcherTimer timer=new();
    private RasterBackground? raster;
    private MediaElement? video,pending;
    private TaskCompletionSource<MediaElement>? preparing;
    private bool running,disposed;
    public event Action<string>? Failed;
    public bool HasBackground=>raster!=null||video!=null;
    public bool IsDynamic=>raster?.Gif is {Count:>1}||video!=null;
    public bool IsPlaying=>running&&IsDynamic;
    public bool VideoReady=>video!=null;
    public long FrameCounter {get;private set;}
    public int LoopCounter {get;private set;}
    public TimeSpan Position=>video?.Position??TimeSpan.Zero;
    public BitmapSource? Poster=>image.Source as BitmapSource;
    public string Kind=>video!=null?"MP4":raster?.Gif is {Count:>1}?"GIF":raster!=null?"图片":"无";
    public BackgroundPlayer()
    {
        IsHitTestVisible=false;Children.Add(image);
        timer.Tick+=(_,_)=>{
            try{if(raster?.Gif is { } gif){image.Source=gif.Frame(true);FrameCounter++;if(gif.Index==0)LoopCounter++;timer.Interval=TimeSpan.FromMilliseconds(gif.Delay);}}
            catch(Exception e){timer.Stop();running=false;Failed?.Invoke("GIF 播放失败："+e.Message);}
        };
    }
    public void UseRaster(RasterBackground value)
    {
        ClearActive();raster=value;image.Source=value.Poster;image.Visibility=Visibility.Visible;UpdatePlayback();
    }
    public async Task<MediaElement> PrepareVideo(string path)
    {
        if(disposed)throw new ObjectDisposedException(nameof(BackgroundPlayer));
        if(new FileInfo(path).Length>LocalBackground.MaxVideoBytes)throw new IOException("视频超过 100 MB。");
        if(preparing!=null)throw new IOException("正在载入背景，请稍后再试。");
        Visibility=Visibility.Visible;
        var media=new MediaElement {LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Manual,Stretch=Stretch.UniformToFill,IsMuted=true,Volume=0,Opacity=0,IsHitTestVisible=false};
        pending=media;var completion=new TaskCompletionSource<MediaElement>();preparing=completion;Children.Add(media);
        var timeout=new DispatcherTimer {Interval=TimeSpan.FromSeconds(12)};
        RoutedEventHandler opened=(_,_)=>{
            if(media.NaturalVideoWidth<=0||media.NaturalVideoHeight<=0||(long)media.NaturalVideoWidth*media.NaturalVideoHeight>40_000_000)completion.TrySetException(new IOException("视频没有可播放画面，或尺寸过大。"));
            else completion.TrySetResult(media);
        };
        EventHandler<ExceptionRoutedEventArgs> failed=(_,e)=>completion.TrySetException(new IOException("MP4 无法播放，请使用 H.264 编码的视频。"+e.ErrorException.Message,e.ErrorException));
        media.MediaOpened+=opened;media.MediaFailed+=failed;
        timeout.Tick+=(_,_)=>completion.TrySetException(new IOException("载入 MP4 超时，原背景保留。"));
        bool success=false;
        try{timeout.Start();media.Source=new Uri(path,UriKind.Absolute);media.Play();var result=await completion.Task;success=true;return result;}
        finally
        {
            timeout.Stop();media.MediaOpened-=opened;media.MediaFailed-=failed;pending=null;preparing=null;
            if(!success)ReleaseVideo(media);
        }
    }
    public void UseVideo(MediaElement media)
    {
        ClearActive();video=media;image.Source=null;image.Visibility=Visibility.Collapsed;media.Opacity=1;
        media.MediaEnded+=(_,_)=>{if(video!=media)return;LoopCounter++;media.Position=TimeSpan.Zero;if(running)media.Play();};
        media.MediaFailed+=(_,e)=>{if(video!=media)return;ClearActive();Failed?.Invoke("动态背景播放失败，已回退主题底色："+e.ErrorException.Message);};UpdatePlayback();
    }
    public void SetRunning(bool value){running=value;UpdatePlayback();}
    private void UpdatePlayback()
    {
        timer.Stop();
        if(running&&raster?.Gif is {Count:>1} gif){timer.Interval=TimeSpan.FromMilliseconds(gif.Delay);timer.Start();}
        if(video!=null){if(running)video.Play();else video.Pause();}
    }
    public void CancelPreparation()=>preparing?.TrySetException(new OperationCanceledException("背景载入已取消。"));
    public void ReleaseVideo(MediaElement media){media.Close();media.Source=null;Children.Remove(media);}
    private void ClearActive()
    {
        timer.Stop();raster?.Dispose();raster=null;image.Source=null;
        var previous=video;video=null;if(previous!=null)ReleaseVideo(previous);FrameCounter=0;LoopCounter=0;
    }
    public void Clear(){CancelPreparation();ClearActive();}
    public void Dispose(){if(disposed)return;disposed=true;running=false;CancelPreparation();ClearActive();if(pending!=null)ReleaseVideo(pending);}
}

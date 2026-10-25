using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing=System.Drawing;
using Imaging=System.Drawing.Imaging;

namespace DailyDiaryCard;

internal sealed class GifBackground : IDisposable
{
    private readonly MemoryStream stream;
    private readonly Drawing.Image image;
    private readonly int[] delays;
    public int Count {get;}
    public int Index {get;private set;}
    public int Delay=>delays[Index];
    public GifBackground(byte[] bytes)
    {
        stream=new MemoryStream(bytes,false);
        try
        {
            image=Drawing.Image.FromStream(stream,false,true);
            if(image.RawFormat.Guid!=Imaging.ImageFormat.Gif.Guid)throw new IOException("文件内容不是 GIF。");
            if((long)image.Width*image.Height>40_000_000)throw new IOException("GIF 尺寸超过 4000 万像素。");
            Count=image.GetFrameCount(Imaging.FrameDimension.Time);
            if(Count>2000)throw new IOException("GIF 帧数过多，请先缩短或转为 MP4。");
            delays=new int[Count];Array.Fill(delays,100);
            if(Array.IndexOf(image.PropertyIdList,0x5100)>=0)
            {
                var timing=image.GetPropertyItem(0x5100)?.Value;
                if(timing!=null)for(int i=0;i<Count&&i*4+4<=timing.Length;i++)delays[i]=Math.Clamp(BitConverter.ToInt32(timing,i*4)*10,40,10_000);
            }
        }
        catch{image?.Dispose();stream.Dispose();throw;}
    }
    public BitmapSource Frame(bool next=false)
    {
        if(next)Index=(Index+1)%Count;
        image.SelectActiveFrame(Imaging.FrameDimension.Time,Index);
        double scale=Math.Min(1,1280.0/Math.Max(image.Width,image.Height));
        int width=Math.Max(1,(int)(image.Width*scale)),height=Math.Max(1,(int)(image.Height*scale));
        using var bitmap=new Drawing.Bitmap(width,height,Imaging.PixelFormat.Format32bppPArgb);
        using(var graphics=Drawing.Graphics.FromImage(bitmap))graphics.DrawImage(image,0,0,width,height);
        var data=bitmap.LockBits(new Drawing.Rectangle(0,0,width,height),Imaging.ImageLockMode.ReadOnly,Imaging.PixelFormat.Format32bppPArgb);
        try{var frame=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,data.Scan0,data.Stride*height,data.Stride);frame.Freeze();return frame;}
        finally{bitmap.UnlockBits(data);}
    }
    public void Dispose(){image.Dispose();stream.Dispose();}
}

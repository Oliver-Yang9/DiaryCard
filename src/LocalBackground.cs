using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace DailyDiaryCard;

public static class LocalBackground
{
    public const long MaxBytes=20*1024*1024;
    public const long MaxVideoBytes=100*1024*1024;
    public static bool IsVideo(string name)=>Path.GetExtension(name).Equals(".mp4",StringComparison.OrdinalIgnoreCase);
    public static string Resolve(string userData,string name)
    {
        if(string.IsNullOrEmpty(name))return "";
        if(Path.GetFileName(name)!=name||name.Contains(':')||name.Contains('/')||name.Contains('\\'))throw new IOException("背景记录不是本机图片文件名，请重新选择图片。");
        return Path.Combine(userData,"backgrounds",name);
    }
    public static string Import(string userData,string source)
    {
        var path=Path.GetFullPath(source);
        if(path.StartsWith(@"\\",StringComparison.Ordinal)||new DriveInfo(Path.GetPathRoot(path)!).DriveType==DriveType.Network)throw new IOException("请选择本机磁盘中的图片。");
        var extension=Path.GetExtension(path).ToLowerInvariant();
        if(!new[]{".png",".jpg",".jpeg",".bmp",".gif",".mp4"}.Contains(extension))throw new IOException("支持 PNG、JPG、BMP、GIF 和 MP4 背景。");
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        long limit=IsVideo(path)?MaxVideoBytes:MaxBytes;
        if(stream.Length>limit)throw new IOException(IsVideo(path)?"视频超过 100 MB，请先压缩。":"图片超过 20 MB，请先压缩后再选择。");
        using var buffer=new MemoryStream();stream.CopyTo(buffer);
        if(buffer.Length>limit)throw new IOException("背景文件超过大小限制。");
        var bytes=buffer.ToArray();
        if(IsVideo(path))
        {
            if(bytes.Length<12||System.Text.Encoding.ASCII.GetString(bytes,4,4)!="ftyp")throw new IOException("这不是有效的 MP4 文件。");
        }
        else if(extension==".gif"){using var gif=new GifBackground(bytes);}
        else _=Decode(bytes);
        var name=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()+extension;
        var target=Resolve(userData,name);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Never modify the selected source image. Identical imports share a local copy.
        if(!File.Exists(target))
        {
            var temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllBytes(temp,bytes);File.Move(temp,target,false);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        return name;
    }
    public static BitmapSource Load(string userData,string name)
    {
        var file=Resolve(userData,name);
        if(new FileInfo(file).Length>MaxBytes)throw new IOException("本机背景副本超过大小限制。");
        return Decode(File.ReadAllBytes(file));
    }
    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes,false);
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnLoad);
        var frame=decoder.Frames[0];
        if(frame.PixelWidth<=0||frame.PixelHeight<=0||(long)frame.PixelWidth*frame.PixelHeight>40_000_000)throw new IOException("图片尺寸过大，请使用不超过 4000 万像素的图片。");
        stream.Position=0;
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
        image.StreamSource=stream;
        if(frame.PixelWidth>=frame.PixelHeight)image.DecodePixelWidth=Math.Min(1800,frame.PixelWidth);
        else image.DecodePixelHeight=Math.Min(1800,frame.PixelHeight);
        image.EndInit();image.Freeze();return image;
    }
}

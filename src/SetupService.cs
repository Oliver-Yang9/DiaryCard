using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DailyDiaryCard;

public static class DefaultAssets
{
    public static byte[] Read(string name)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("DailyDiaryCard.Defaults."+name)??throw new IOException("默认资源缺失："+name);
        using var bytes=new MemoryStream();stream.CopyTo(bytes);return bytes.ToArray();
    }
    public static string Template=>DiarySession.Utf8.GetString(Read("template.md"));
    public static string TemplatePath(string data)=>Path.Combine(data,"defaults","默认日记.md");
    public static string BackgroundName=>Convert.ToHexString(SHA256.HashData(Read("background.jpg"))).ToLowerInvariant()+".jpg";
}

public sealed record VaultSettings(string Template,string Folder,string Format,string Source,bool HasTemplate);
public sealed record VaultPlan(CardConfig Config,string InstallRoot,bool CreateVault,string TemplateText,string SettingsSource,bool WriteTemplate=false,string? ManifestExpected=null);

public static partial class SetupService
{
    public static VaultSettings Inspect(string vault)
    {
        if(string.IsNullOrWhiteSpace(vault))throw new IOException("请选择已经存在的日记库文件夹。");
        vault=Path.GetFullPath(vault);
        if(!Directory.Exists(vault))throw new IOException("请选择已经存在的日记库文件夹。");
        string daily=Path.Combine(vault,".obsidian","daily-notes.json"),periodic=Path.Combine(vault,".obsidian","plugins","periodic-notes","data.json");
        JsonElement settings;string source;
        if(File.Exists(daily)){using var doc=JsonDocument.Parse(DiarySession.ReadText(daily).TrimStart((char)0xFEFF));settings=doc.RootElement.Clone();source="Obsidian 每日笔记";}
        else if(File.Exists(periodic))
        {
            using var doc=JsonDocument.Parse(DiarySession.ReadText(periodic).TrimStart((char)0xFEFF));
            if(doc.RootElement.TryGetProperty("daily",out var node))settings=node.Clone();else{using var empty=JsonDocument.Parse("{}");settings=empty.RootElement.Clone();}source="Periodic Notes 每日笔记";
        }
        else{using var doc=JsonDocument.Parse("{}");settings=doc.RootElement.Clone();source="未发现日记配置，使用默认值";}
        if(settings.ValueKind!=JsonValueKind.Object)throw new IOException("日记设置格式无效，请手动填写模板与保存规则。");
        string Get(string name,string fallback)=>settings.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(value.GetString())?value.GetString()!:fallback;
        var template=Get("template","");
        if(template.Length>0&&!template.EndsWith(".md",StringComparison.OrdinalIgnoreCase))template+=".md";
        bool hasSettings=File.Exists(daily)||File.Exists(periodic);
        return new(template,Get("folder",hasSettings?".":"日记"),Get("format",hasSettings?"YYYY-MM-DD":"YYYY/MM/YYYY-MM-DD"),source,template.Length>0);
    }
    public static VaultPlan Plan(string configFile,bool createVault,string vault,string template,bool useDefault,string folder,string format,string? data=null)
    {
        configFile=Path.GetFullPath(configFile);string install=Path.GetDirectoryName(configFile)!;
        if(!createVault&&string.IsNullOrWhiteSpace(vault))throw new IOException("请选择你的日记库文件夹，或选择创建新日记库。");
        vault=createVault?Path.Combine(install,"DailyNote"):Path.GetFullPath(vault.Trim(),install);
        if(createVault&&Directory.Exists(vault)&&Directory.EnumerateFileSystemEntries(vault).Any())throw new IOException("DailyNote 已有内容。请选择“已有日记库”载入它，不会覆盖现有文件。");
        if(!createVault&&!Directory.Exists(vault))throw new IOException("日记库文件夹不存在。");
        if(File.Exists(vault))throw new IOException("日记库地址是文件，请选择文件夹。");
        SafeDirectory(vault);
        var c=new CardConfig{ConfigPath=configFile,VaultPath=vault,DiaryFolder=Path.GetFullPath(string.IsNullOrWhiteSpace(folder)?"日记":folder.Trim(),vault),DateFormat=string.IsNullOrWhiteSpace(format)?"YYYY/MM/YYYY-MM-DD":format.Trim(),UserDataPath=Path.GetFullPath(data??Path.Combine(install,"data"),install),SetupVersion=2,UseBundledTemplate=useDefault&&!createVault};
        c.TemplatePath=createVault?Path.Combine(vault,"模板","03-工作学习复盘.md"):useDefault?DefaultAssets.TemplatePath(c.UserDataPath):Path.GetFullPath(template.Trim(),vault);
        if(createVault&&!IsUnder(vault,c.DiaryFolder))throw new IOException("新建库的日记目录需要位于 DailyNote 文件夹中。");
        SafeDirectory(c.DiaryFolder);SafeDirectory(c.UserDataPath);
        if(!useDefault&&!createVault&&!File.Exists(c.TemplatePath))throw new IOException("找不到所选模板，请选择 Markdown 文件，或勾选“使用内置模板”。");
        string text=createVault?DefaultAssets.Template:useDefault&&!File.Exists(c.TemplatePath)?DefaultAssets.Template:DiarySession.ReadText(c.TemplatePath);
        _=TemplateReconciler.Parse(text);var target=c.PathFor(DateOnly.FromDateTime(DateTime.Now));
        if(File.Exists(target)){var existing=new MarkdownDoc(DiarySession.ReadText(target));foreach(var h in TemplateReconciler.Parse(text))_=existing.Unique(h.Title);}
        return new(c,install,createVault,text,createVault?"新建本地 Obsidian 日记库":"已有日记库");
    }
    public static bool IsUnder(string root,string path)=>path.Equals(root,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    internal static void SafeDirectory(string path)
    {
        for(DirectoryInfo? dir=new(Path.GetFullPath(path));dir!=null;dir=dir.Parent)
            if(dir.Exists&&(dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("地址含链接或重解析目录，请选择普通本地文件夹："+dir.FullName);
    }
    public static SetupTransaction Prepare(VaultPlan plan)=>new(plan);
    public static CardConfig Complete(VaultPlan plan){using var transaction=Prepare(plan);transaction.Commit();return CardConfig.Load(plan.Config.ConfigPath);}
    internal static CardConfig Portable(CardConfig c)
    {
        var dir=Path.GetDirectoryName(c.ConfigPath)!;
        return new(){StorageMode=c.StorageMode,VaultPath=IsUnder(dir,c.VaultPath)?Path.GetRelativePath(dir,c.VaultPath):c.VaultPath,TemplatePath=c.UseBundledTemplate?"":Path.GetRelativePath(c.VaultPath,c.TemplatePath),DiaryFolder=Path.GetRelativePath(c.VaultPath,c.DiaryFolder),DateFormat=c.DateFormat,UserDataPath=Path.GetRelativePath(dir,c.UserDataPath),PathMappings=c.PathMappings,SetupVersion=c.SetupVersion,UseBundledTemplate=c.UseBundledTemplate};
    }
}

public sealed partial class SetupTransaction:IDisposable
{
    private readonly Dictionary<string,string> files=new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> directories=new();
    private bool committed;
    public VaultPlan Plan{get;}
    public CardConfig Config=>Plan.Config;
    public SetupTransaction(VaultPlan plan)
    {
        Plan=plan;
        try
        {
            if(plan.CreateVault)
            {
                if(Directory.Exists(Config.VaultPath)&&Directory.EnumerateFileSystemEntries(Config.VaultPath).Any())throw new IOException("DailyNote 已有内容，请选择已有日记库。");
                Folder(Config.VaultPath);
                Write(Config.TemplatePath,DiarySession.Utf8.GetBytes(plan.TemplateText),false);
                var settings=new{folder=Path.GetRelativePath(Config.VaultPath,Config.DiaryFolder).Replace(Path.DirectorySeparatorChar,'/'),format=Config.DateFormat,template=Path.GetRelativePath(Config.VaultPath,Config.TemplatePath).Replace(Path.DirectorySeparatorChar,'/')};
                if(!Config.IsStandalone)Write(Path.Combine(Config.VaultPath,".obsidian","daily-notes.json"),DiarySession.Utf8.GetBytes(JsonSerializer.Serialize(settings,CardConfig.Json)),false);
            }
            if(Config.IsStandalone&&!plan.CreateVault&&plan.WriteTemplate)Write(Config.TemplatePath,DiarySession.Utf8.GetBytes(plan.TemplateText),false);
            Folder(Config.UserDataPath);
            Write(DefaultAssets.TemplatePath(Config.UserDataPath),DefaultAssets.Read("template.md"),true);
            Write(Path.Combine(Config.UserDataPath,"backgrounds",DefaultAssets.BackgroundName),DefaultAssets.Read("background.jpg"),true);
            var prefs=new Preferences{BackgroundImage=DefaultAssets.BackgroundName,BackgroundOpacity=0.75,SectionTransparency=0.35};
            Write(Path.Combine(Config.UserDataPath,"settings.json"),DiarySession.Utf8.GetBytes(JsonSerializer.Serialize(prefs,CardConfig.Json)),true);
        }
        catch{Dispose();throw;}
    }
    private void Folder(string path)
    {
        SetupService.SafeDirectory(path);var missing=new Stack<string>();
        for(DirectoryInfo? dir=new(path);dir!=null&&!dir.Exists;dir=dir.Parent)missing.Push(dir.FullName);
        while(missing.Count>0){var dir=missing.Pop();Directory.CreateDirectory(dir);directories.Add(dir);}
    }
    private void Write(string path,byte[] bytes,bool keepExisting)
    {
        if(keepExisting&&File.Exists(path))return;
        Folder(Path.GetDirectoryName(path)!);
        using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes);f.Flush(true);}
        files[path]=Convert.ToHexString(SHA256.HashData(bytes));
    }
    public void Commit()
    {
        // Configuration is committed last; existing vault settings and notes are never rewritten here.
        if(File.Exists(Config.ConfigPath))DiarySession.AtomicText(Path.Combine(Config.UserDataPath,"config.previous.json"),DiarySession.ReadText(Config.ConfigPath));
        CommitStandaloneManifest();
        DiarySession.AtomicText(Config.ConfigPath,JsonSerializer.Serialize(SetupService.Portable(Config),CardConfig.Json));committed=true;
    }
    public void Dispose()
    {
        if(committed)return;
        RollbackStandaloneManifest();
        foreach(var file in files)
            try{if(File.Exists(file.Key)&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Key)))==file.Value)File.Delete(file.Key);}catch(IOException){}catch(UnauthorizedAccessException){}
        foreach(var dir in directories.AsEnumerable().Reverse())try{if(Directory.Exists(dir)&&!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir,false);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
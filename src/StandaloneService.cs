using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DailyDiaryCard;
public sealed record StandaloneLibrary(string Template,string Folder,string Format);
public static partial class SetupService
{
    public const string StandaloneMarker="diarycard-library.json";
    public sealed class LibraryDescriptor
    {
        public string Kind{get;set;}="DiaryCard";
        public int SchemaVersion{get;set;}=1;
        public string TemplatePath{get;set;}="";
        public string DiaryFolder{get;set;}="日记";
        public string DateFormat{get;set;}="YYYY/MM/YYYY-MM-DD";
    }
    internal static string StandaloneBound(string root,string value,bool allowRoot)
    {
        string full=Path.GetFullPath(value,root);
        if(!IsUnder(root,full)||(!allowRoot&&full.Equals(root,StringComparison.OrdinalIgnoreCase)))throw new IOException("独立模式的模板和日记必须位于当前资料夹内。");
        if(allowRoot){if(File.Exists(full))throw new IOException("日记目录地址是文件，请选择文件夹。");SafeDirectory(full);}
        else
        {
            SafeDirectory(Path.GetDirectoryName(full)!);
            if(Directory.Exists(full)||(File.Exists(full)&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0))throw new IOException("请选择普通本地文件，不能使用链接："+full);
        }
        return full;
    }
    public static StandaloneLibrary InspectStandalone(string root)
    {
        if(string.IsNullOrWhiteSpace(root))throw new IOException("请选择本地日记资料夹。");
        root=Path.GetFullPath(root);SafeDirectory(root);
        string file=StandaloneBound(root,StandaloneMarker,false);
        if(!File.Exists(file))throw new IOException("这个文件夹不是 DiaryCard 独立资料夹。请选择空文件夹新建，或选择包含 "+StandaloneMarker+" 的已有资料夹。");
        LibraryDescriptor d;
        try
        {
            string text=DiarySession.ReadText(file).TrimStart('\uFEFF');
            using var json=JsonDocument.Parse(text,CardConfig.Json.ReadCommentHandling==JsonCommentHandling.Skip?new JsonDocumentOptions{CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true}:default);
            if(json.RootElement.ValueKind!=JsonValueKind.Object||!json.RootElement.TryGetProperty("Kind",out _)||!json.RootElement.TryGetProperty("SchemaVersion",out _))throw new IOException("资料夹标记缺少类型或版本。");
            d=JsonSerializer.Deserialize<LibraryDescriptor>(text,CardConfig.Json)??throw new IOException("资料夹标记为空。");
        }
        catch(JsonException e){throw new IOException("资料夹标记损坏，已保留原文件："+file,e);}
        if(d.Kind!="DiaryCard"||d.SchemaVersion!=1)throw new IOException("无法识别资料夹类型或版本，已停止读取。");
        if(string.IsNullOrWhiteSpace(d.TemplatePath)||string.IsNullOrWhiteSpace(d.DiaryFolder)||string.IsNullOrWhiteSpace(d.DateFormat)||Path.IsPathRooted(d.TemplatePath)||Path.IsPathRooted(d.DiaryFolder))throw new IOException("资料夹标记的路径或命名规则不完整。");
        string template=StandaloneBound(root,d.TemplatePath,false),folder=StandaloneBound(root,d.DiaryFolder,true);
        if(!template.EndsWith(".md",StringComparison.OrdinalIgnoreCase)||!File.Exists(template))throw new IOException("资料夹模板不存在，请保留现有文件后修复："+template);
        _=TemplateReconciler.Parse(DiarySession.ReadText(template));
        _=new CardConfig{DiaryFolder=folder,DateFormat=d.DateFormat}.PathFor(new(2026,10,26));
        return new(d.TemplatePath,d.DiaryFolder,d.DateFormat);
    }
    public static VaultPlan PlanStandalone(string configFile,string library,string template,bool useDefault,string folder,string format,string? data=null)
    {
        configFile=Path.GetFullPath(configFile);string install=Path.GetDirectoryName(configFile)!;
        if(string.IsNullOrWhiteSpace(library))throw new IOException("请选择本地日记资料夹。");
        string root=Path.GetFullPath(library.Trim(),install);SafeDirectory(root);
        if(File.Exists(root))throw new IOException("资料夹地址是一个文件。");
        bool create=!Directory.Exists(root)||!Directory.EnumerateFileSystemEntries(root).Any();
        string? expected=create?null:DiarySession.ReadText(StandaloneBound(root,StandaloneMarker,false));
        StandaloneLibrary? previous=create?null:InspectStandalone(root);
        if(!create&&DiarySession.ReadText(Path.Combine(root,StandaloneMarker))!=expected)throw new IOException("资料夹设置正在被修改，请稍后重新读取。");
        var c=new CardConfig{StorageMode="standalone",SetupVersion=3,ConfigPath=configFile,VaultPath=root,UserDataPath=Path.GetFullPath(data??Path.Combine(install,"data"),install),DiaryFolder=StandaloneBound(root,string.IsNullOrWhiteSpace(folder)?previous?.Folder??"日记":folder.Trim(),true),DateFormat=string.IsNullOrWhiteSpace(format)?previous?.Format??"YYYY/MM/YYYY-MM-DD":format.Trim(),UseBundledTemplate=false};
        SafeDirectory(c.UserDataPath);
        if(IsUnder(c.UserDataPath,root))throw new IOException("程序数据目录不能包含整个日记资料夹，请选择另一个地址。");
        string text;
        if(useDefault)
        {
            c.TemplatePath=StandaloneBound(root,create?Path.Combine("模板","日记模板.md"):Path.Combine("模板","内置复盘.md"),false);
            text=File.Exists(c.TemplatePath)?DiarySession.ReadText(c.TemplatePath):DefaultAssets.Template;
        }
        else
        {
            string source=string.IsNullOrWhiteSpace(template)?previous?.Template??"":template.Trim();
            if(source.Length==0)throw new IOException("请选择模板，或使用内置复盘模板。");
            source=Path.GetFullPath(source,root);SafeDirectory(Path.GetDirectoryName(source)!);
            if(!File.Exists(source)||(File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("模板不存在或不是普通本地文件。");
            text=DiarySession.ReadText(source);
            c.TemplatePath=IsUnder(root,source)?StandaloneBound(root,source,false):StandaloneBound(root,Path.Combine("模板","导入模板-"+DiarySession.Hash(text)[..12]+".md"),false);
            if(File.Exists(c.TemplatePath)&&DiarySession.ReadText(c.TemplatePath)!=text)throw new IOException("模板目标已存在且内容不同，已停止导入。");
        }
        if(!c.TemplatePath.EndsWith(".md",StringComparison.OrdinalIgnoreCase))throw new IOException("请选择 Markdown（.md）模板。");
        _=TemplateReconciler.Parse(text);string target=c.PathFor(DateOnly.FromDateTime(DateTime.Now));
        if(File.Exists(target)){var existing=new MarkdownDoc(DiarySession.ReadText(target));foreach(var heading in TemplateReconciler.Parse(text))_=existing.Unique(heading.Title);}
        return new(c,install,create,text,create?"新建独立资料夹":"已有独立资料夹",!File.Exists(c.TemplatePath),expected);
    }
    internal static string DescriptorText(CardConfig c)=>JsonSerializer.Serialize(new LibraryDescriptor{TemplatePath=Path.GetRelativePath(c.VaultPath,c.TemplatePath),DiaryFolder=Path.GetRelativePath(c.VaultPath,c.DiaryFolder),DateFormat=c.DateFormat},CardConfig.Json);
}
public sealed partial class SetupTransaction
{
    private byte[]? previousManifest,writtenManifest;
    private void CommitStandaloneManifest()
    {
        if(!Config.IsStandalone)return;
        SetupService.StandaloneBound(Config.VaultPath,Config.TemplatePath,false);
        string template=DiarySession.ReadText(Config.TemplatePath);
        if(template!=Plan.TemplateText)throw new IOException("模板在预览后发生变化，请重新读取后保存。");
        _=TemplateReconciler.Parse(template);
        string file=SetupService.StandaloneBound(Config.VaultPath,SetupService.StandaloneMarker,false);
        byte[] bytes=DiarySession.Utf8.GetBytes(SetupService.DescriptorText(Config));
        if(Plan.CreateVault){Write(file,bytes,false);return;}
        using var f=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        if(f.Length>8*1024*1024)throw new IOException("资料夹标记过大，已停止保存。");
        var current=new byte[f.Length];f.ReadExactly(current);
        if(DiarySession.Utf8.GetString(current)!=Plan.ManifestExpected)throw new IOException("资料夹设置已被其他程序修改，请重新读取。");
        var old=JsonSerializer.Deserialize<SetupService.LibraryDescriptor>(DiarySession.Utf8.GetString(current).TrimStart('\uFEFF'),CardConfig.Json)!;
        if(Path.GetFullPath(old.TemplatePath,Config.VaultPath).Equals(Config.TemplatePath,StringComparison.OrdinalIgnoreCase)&&Path.GetFullPath(old.DiaryFolder,Config.VaultPath).Equals(Config.DiaryFolder,StringComparison.OrdinalIgnoreCase)&&old.DateFormat==Config.DateFormat)return;
        var node=System.Text.Json.Nodes.JsonNode.Parse(DiarySession.Utf8.GetString(current).TrimStart('\uFEFF'),documentOptions:new JsonDocumentOptions{CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true})!.AsObject();
        node["TemplatePath"]=Path.GetRelativePath(Config.VaultPath,Config.TemplatePath);
        node["DiaryFolder"]=Path.GetRelativePath(Config.VaultPath,Config.DiaryFolder);
        node["DateFormat"]=Config.DateFormat;
        bytes=DiarySession.Utf8.GetBytes(node.ToJsonString(CardConfig.Json));
        if(current.SequenceEqual(bytes))return;
        previousManifest=current;writtenManifest=bytes;
        try{f.Position=0;f.Write(bytes);f.SetLength(bytes.Length);f.Flush(true);}
        catch{try{f.Position=0;f.Write(current);f.SetLength(current.Length);f.Flush(true);}catch{}previousManifest=null;writtenManifest=null;throw;}
    }
    private void RollbackStandaloneManifest()
    {
        if(previousManifest==null||writtenManifest==null)return;
        try
        {
            string file=SetupService.StandaloneBound(Config.VaultPath,SetupService.StandaloneMarker,false);
            using var f=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            var current=new byte[f.Length];f.ReadExactly(current);
            if(!current.SequenceEqual(writtenManifest))return;
            f.Position=0;f.Write(previousManifest);f.SetLength(previousManifest.Length);f.Flush(true);
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
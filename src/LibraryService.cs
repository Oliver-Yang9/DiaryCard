using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DailyDiaryCard;

public enum LibraryKind{Diary,Weekly,Template,Reference}
public sealed record LibraryEntry(string Path,string Title,string Summary,LibraryKind Kind,DateOnly? Date);
public sealed record LibraryLink(string Path,string Heading,string Label);
public static class LibraryService
{
    public static string CheckPath(CardConfig c,string file)
    {
        file=Path.GetFullPath(file);
        bool permitted=SetupService.IsUnder(c.VaultPath,file)||SetupService.IsUnder(c.DiaryFolder,file)||file.Equals(c.TemplatePath,StringComparison.OrdinalIgnoreCase);
        if(!permitted||!file.EndsWith(".md",StringComparison.OrdinalIgnoreCase))throw new IOException("只能打开当前资料库内的 Markdown 文件。");
        SetupService.SafeDirectory(Path.GetDirectoryName(file)!);
        if(!File.Exists(file)||Directory.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("记录不存在或不是普通本地文件："+file);
        return file;
    }
    private static IEnumerable<string> Walk(string directory)
    {
        SetupService.SafeDirectory(directory);if(!Directory.Exists(directory))yield break;
        foreach(string path in Directory.EnumerateFileSystemEntries(directory).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase))
        {
            var attr=File.GetAttributes(path);if((attr&FileAttributes.ReparsePoint)!=0)continue;
            if((attr&FileAttributes.Directory)!=0){if(Path.GetFileName(path).StartsWith('.'))continue;foreach(var file in Walk(path))yield return file;}
            else if(path.EndsWith(".md",StringComparison.OrdinalIgnoreCase))yield return path;
        }
    }
    public static DateOnly? DateFor(string file,string text)
    {
        var parsed=WeeklyReport.Split(text);
        return WeeklyReport.ValidDate(parsed.Meta.GetValueOrDefault("journal_date"))??WeeklyReport.ValidDate(Path.GetFileNameWithoutExtension(file))??WeeklyReport.ValidDate(parsed.Meta.GetValueOrDefault("date"));
    }
    public static List<LibraryEntry> List(CardConfig c,LibraryKind kind,string query,out int unavailable)
    {
        unavailable=0;var candidates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(kind==LibraryKind.Template)candidates.Add(c.TemplatePath);
        else if(kind==LibraryKind.Diary)foreach(var file in Walk(c.DiaryFolder))candidates.Add(file);
        else
        {
            var settings=WeeklySettings.Load(c);
            string directory=WeeklyReport.OutputRoot(c,settings.Folder);
            foreach(var file in Walk(directory))candidates.Add(file);
        }
        var catalog=new WeeklyCatalog();catalog.Add(DefaultAssets.Template);if(File.Exists(c.TemplatePath))catalog.Add(DiarySession.ReadText(c.TemplatePath));
        var result=new List<LibraryEntry>();
        foreach(string file in candidates)
        {
            try
            {
                if(SetupService.IsUnder(c.UserDataPath,file)&&!file.Equals(c.TemplatePath,StringComparison.OrdinalIgnoreCase))continue;
                string text=DiarySession.ReadText(CheckPath(c,file));var split=WeeklyReport.Split(text);
                string type=split.Meta.GetValueOrDefault("type")??"";
                if(kind==LibraryKind.Diary&&(file.Equals(c.TemplatePath,StringComparison.OrdinalIgnoreCase)||type=="weekly-report"))continue;
                if(kind==LibraryKind.Weekly&&type!="weekly-report")continue;
                DateOnly? date=DateFor(file,text);
                if(kind==LibraryKind.Diary&&(date==null||(type!="diary"&&!(type.Length==0&&c.PathFor(date.Value).Equals(file,StringComparison.OrdinalIgnoreCase)))))continue;
                string title=kind==LibraryKind.Template?"当前日记模板":kind==LibraryKind.Diary&&date!=null?date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):Path.GetFileNameWithoutExtension(file);
                string summary=Regex.Replace(split.Body,@"(?m)^\s*#+\s*.*$|<!--[\s\S]*?-->","");summary=Regex.Replace(summary,@"\s+"," ").Trim();if(summary.Length>92)summary=summary[..92]+"…";
                if(kind==LibraryKind.Diary)summary=string.Join(" · ",WeeklyReport.Extract(split.Body,catalog).Take(2).Select(e=>e.Text));
                else if(kind==LibraryKind.Template)summary=TemplateReconciler.Parse(text).Count+" 个栏目 · 在软件内调整复盘结构";
                else if(kind==LibraryKind.Weekly){var stats=Regex.Match(text,@"(?m)^-\s*有效日记：[^\r\n]+");if(stats.Success)summary=stats.Value.TrimStart('-',' ');}
                if(summary.Length>92)summary=summary[..92]+"…";
                if(query.Length>0&&!title.Contains(query,StringComparison.OrdinalIgnoreCase)&&!text.Contains(query,StringComparison.OrdinalIgnoreCase))continue;
                result.Add(new(file,title,summary,kind,date));
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or DecoderFallbackException){unavailable++;}
        }
        return result.OrderByDescending(x=>x.Date).ThenByDescending(x=>x.Title,StringComparer.Ordinal).ToList();
    }
    public static List<LibraryLink> Links(CardConfig c,string text)
    {
        var result=new List<LibraryLink>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(Match match in Regex.Matches(text,@"\[\[([^\]\r\n]+)\]\]"))
        {
            string token=match.Groups[1].Value;int bar=token.IndexOf('|');string target=bar<0?token:token[..bar];string label=bar<0?target:token[(bar+1)..];
            int hash=target.IndexOf('#');string file=hash<0?target:target[..hash],heading=hash<0?"":target[(hash+1)..];
            if(string.IsNullOrWhiteSpace(file)||Path.IsPathRooted(file)||Uri.TryCreate(file,UriKind.Absolute,out _))continue;
            try
            {
                if(!file.EndsWith(".md",StringComparison.OrdinalIgnoreCase))file+=".md";
                string path=Path.GetFullPath(file.Replace('/',Path.DirectorySeparatorChar),c.VaultPath);
                if(!SetupService.IsUnder(c.VaultPath,path))continue;
                path=CheckPath(c,path);if(seen.Add(path+"#"+heading))result.Add(new(path,heading,label));
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){}
        }
        return result;
    }
    public static void Export(string destination,string text,string original)
    {
        if(Path.GetFullPath(destination).Equals(Path.GetFullPath(original),StringComparison.OrdinalIgnoreCase))throw new IOException("导出请选择其他文件名，避免覆盖正在编辑的原件。");
        SetupService.SafeDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        if(Directory.Exists(destination)||(File.Exists(destination)&&(File.GetAttributes(destination)&FileAttributes.ReparsePoint)!=0))throw new IOException("导出目标必须是普通本地文件。");
        string temp=destination+".tmp-"+Guid.NewGuid().ToString("N");
        try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(DiarySession.Utf8.GetBytes(text));file.Flush(true);}File.Move(temp,destination,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

public sealed class LibraryDocument
{
    private sealed class Recovery
    {
        public string Path{get;set;}="";
        public string Baseline{get;set;}="";
        public string Text{get;set;}="";
        public bool Pending{get;set;}
    }
    private readonly CardConfig config;
    private byte[] expected;
    private string baseline;
    public string Path{get;}
    public LibraryKind Kind{get;}
    public string Text{get;private set;}
    public bool Dirty=>Text!=baseline;
    public bool Restored{get;private set;}
    public string? RecoveryNotice{get;private set;}
    public string RecoveryPath=>System.IO.Path.Combine(config.UserDataPath,"library","recovery",DiarySession.Hash(Path.ToUpperInvariant())[..24]+".json");
    public LibraryDocument(CardConfig c,string path,LibraryKind kind,bool restore=true)
    {
        config=c;Path=LibraryService.CheckPath(c,path);Kind=kind;expected=File.ReadAllBytes(Path);baseline=Text=DiarySession.Utf8.GetString(expected);
        if(restore&&File.Exists(RecoveryPath))
        {
            try
            {
                var d=JsonSerializer.Deserialize<Recovery>(DiarySession.ReadText(RecoveryPath),CardConfig.Json);
                if(d is {Pending:true}&&System.IO.Path.GetFullPath(d.Path).Equals(Path,StringComparison.OrdinalIgnoreCase))
                {
                    expected=Convert.FromBase64String(d.Baseline);baseline=DiarySession.Utf8.GetString(expected);Text=d.Text;Restored=Dirty;
                }
            }
            catch(Exception e) when(e is JsonException or FormatException or IOException){RecoveryNotice="恢复记录无法读取，原文件仍保留："+e.Message;}
        }
    }
    public void Edit(string text){Text=text;}
    public void PersistDraft()
    {
        SetupService.SafeDirectory(System.IO.Path.GetDirectoryName(RecoveryPath)!);
        DiarySession.AtomicText(RecoveryPath,JsonSerializer.Serialize(new Recovery{Path=Path,Baseline=Convert.ToBase64String(expected),Text=Text,Pending=Dirty},CardConfig.Json));
    }
    public void Save()
    {
        if(!Dirty)return;
        PersistDraft();LibraryService.CheckPath(config,Path);
        if(Kind==LibraryKind.Template)_=TemplateReconciler.Parse(Text);
        if(Kind==LibraryKind.Weekly&&WeeklyReport.Markers(expected)!=null&&WeeklyReport.Markers(DiarySession.Utf8.GetBytes(Text))==null)throw new IOException("周报自动区标记不能删除或重复，请修复后保存。当前输入已保留。");
        byte[] output=DiarySession.Utf8.GetBytes(Text);if(output.Length>8*1024*1024)throw new IOException("文件超过 8 MB，当前输入已保留。");
        using(var file=new FileStream(Path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            if(file.Length!=expected.Length)throw new SaveConflictException("文件已被外部修改，已停止覆盖。当前输入和磁盘内容均保留；请导出草稿或重新读取。");
            var bytes=new byte[file.Length];file.ReadExactly(bytes);
            if(!bytes.SequenceEqual(expected))throw new SaveConflictException("文件已被外部修改，已停止覆盖。当前输入和磁盘内容均保留；请导出草稿或重新读取。");
            string backup=System.IO.Path.Combine(config.UserDataPath,"library","backups",DiarySession.Hash(Path)[..20]+"-"+DateTime.UtcNow.Ticks+".md");
            SetupService.SafeDirectory(System.IO.Path.GetDirectoryName(backup)!);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup)!);File.WriteAllBytes(backup,bytes);
            try{file.Position=0;file.Write(output);file.SetLength(output.Length);file.Flush(true);}
            catch{try{file.Position=0;file.Write(bytes);file.SetLength(bytes.Length);file.Flush(true);}catch{}throw;}
        }
        expected=output;baseline=Text;PersistDraft();
    }
    public string ReloadKeepingDraft()
    {
        string archive=System.IO.Path.Combine(config.UserDataPath,"library","recovery",DiarySession.Hash(Path.ToUpperInvariant())[..24]+"-"+DateTime.UtcNow.Ticks+".md");
        DiarySession.AtomicText(archive,Text);expected=File.ReadAllBytes(LibraryService.CheckPath(config,Path));baseline=Text=DiarySession.Utf8.GetString(expected);Restored=false;PersistDraft();return archive;
    }
}
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace DailyDiaryCard;

public sealed class CardConfig
{
    public string VaultPath { get; set; } = "";
    public string TemplatePath { get; set; } = "";
    public string DiaryFolder { get; set; } = "";
    public string DateFormat { get; set; } = "";
    public string UserDataPath { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public int SetupVersion { get; set; }
    public bool UseBundledTemplate { get; set; }
    public Dictionary<string,string> PathMappings { get; set; } = new();
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    public static CardConfig Load(string? file)
    {
        file ??= Path.Combine(AppContext.BaseDirectory, "diary-card.json");
        var c = File.Exists(file) ? JsonSerializer.Deserialize<CardConfig>(File.ReadAllText(file), Json)! : new();
        c.PathMappings ??= new();
        c.ConfigPath = Path.GetFullPath(file);
        var baseDir = Path.GetDirectoryName(c.ConfigPath)!;
        if(string.IsNullOrWhiteSpace(c.VaultPath))throw new IOException("请先选择或创建日记库。");
        c.VaultPath=c.ResolvePath(c.VaultPath,baseDir);
        if(!Directory.Exists(c.VaultPath))throw new IOException("日记库地址不存在，请在日记库设置中重新选择。");
        if(string.IsNullOrWhiteSpace(c.UserDataPath))c.UserDataPath=Path.Combine(baseDir,"data");
        c.UserDataPath=c.ResolvePath(c.UserDataPath,baseDir);
        var settings=string.IsNullOrWhiteSpace(c.DiaryFolder)||string.IsNullOrWhiteSpace(c.DateFormat)||(!c.UseBundledTemplate&&string.IsNullOrWhiteSpace(c.TemplatePath))?SetupService.Inspect(c.VaultPath):new VaultSettings("","日记","YYYY/MM/YYYY-MM-DD","显式配置",false);
        if(c.UseBundledTemplate)c.TemplatePath=DefaultAssets.TemplatePath(c.UserDataPath);
        else if(string.IsNullOrWhiteSpace(c.TemplatePath))c.TemplatePath=settings.Template;
        if(string.IsNullOrWhiteSpace(c.TemplatePath))throw new IOException("未找到日记模板，请选择模板或使用内置模板。");
        if(!c.TemplatePath.EndsWith(".md",StringComparison.OrdinalIgnoreCase))c.TemplatePath+=".md";
        c.TemplatePath=c.ResolvePath(c.TemplatePath,c.VaultPath);
        if(string.IsNullOrWhiteSpace(c.DiaryFolder))c.DiaryFolder=settings.Folder;
        c.DiaryFolder=c.ResolvePath(c.DiaryFolder,c.VaultPath);
        if(string.IsNullOrWhiteSpace(c.DateFormat))c.DateFormat=settings.Format;
        if (!File.Exists(c.TemplatePath)) throw new IOException("找不到模板：" + c.TemplatePath);
        _ = TemplateReconciler.Parse(DiarySession.ReadText(c.TemplatePath));
        _ = c.PathFor(new DateOnly(2026, 10, 24));
        return c;
    }
    public string ResolvePath(string value,string relativeTo)
    {
        string path=Path.GetFullPath(value,relativeTo);
        string configDir=Path.GetDirectoryName(ConfigPath)??relativeTo;
        foreach(var entry in PathMappings.Select(p=>(From:Path.GetFullPath(p.Key,configDir).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),To:Path.GetFullPath(p.Value,configDir))).OrderByDescending(p=>p.From.Length))
        {
            if(path.Equals(entry.From,StringComparison.OrdinalIgnoreCase))return entry.To;
            if(path.StartsWith(entry.From+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return Path.GetFullPath(path[(entry.From.Length+1)..],entry.To);
        }
        return path;
    }
    public IEnumerable<string> PreviousPaths(string current)
    {
        string configDir=Path.GetDirectoryName(ConfigPath)??AppContext.BaseDirectory;
        foreach(var entry in PathMappings)
        {
            string from=Path.GetFullPath(entry.Key,configDir).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),to=Path.GetFullPath(entry.Value,configDir).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(current.Equals(to,StringComparison.OrdinalIgnoreCase))yield return from;
            else if(current.StartsWith(to+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))yield return Path.Combine(from,current[(to.Length+1)..]);
        }
    }
    public string PathFor(DateOnly date)
    {
        if (!DateFormat.Contains("YYYY") || !DateFormat.Contains("MM") || !DateFormat.Contains("DD")) throw new IOException("日记命名规则需要包含 YYYY、MM、DD。");
        var pattern = DateFormat.Replace("YYYY", date.Year.ToString("0000")).Replace("MM", date.Month.ToString("00")).Replace("DD", date.Day.ToString("00"));
        if (Regex.IsMatch(pattern, "[A-Za-z{}]")) throw new IOException("暂不支持此日期格式，请使用 YYYY/MM/YYYY-MM-DD 等年月日规则。");
        var target = Path.GetFullPath(pattern + ".md", DiaryFolder);
        if (!target.StartsWith(DiaryFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("日记路径超出已配置目录。");
        return target;
    }
}

public sealed record Heading(int Level, string Title, int Start, int ContentStart, int End, string RawHeading, string Body);
public sealed class MarkdownDoc
{
    public string Source { get; }
    public string NewLine => Source.Contains("\r\n") ? "\r\n" : "\n";
    public List<Heading> Sections { get; } = new();
    public MarkdownDoc(string source)
    {
        Source = source;
        int yamlEnd = 0;
        if (Regex.IsMatch(source, "\\A\\uFEFF?---\\r?\\n"))
        {
            var yaml = Regex.Match(source, "\\A\\uFEFF?---\\r?\\n[\\s\\S]*?\\r?\\n(?:---|\\.\\.\\.)[ \\t]*(?:\\r?\\n|$)");
            if (!yaml.Success) throw new IOException("YAML 属性区缺少结束标记，已停止保存。");
            yamlEnd = yaml.Length;
        }
        var headings = new List<(int level, string title, int start, int content, string raw)>();
        foreach (var line in VisibleLines(source))
        {
            if (line.Start < yamlEnd) continue;
            var m = Regex.Match(line.Text, "^ {0,3}(#{1,2})[ \\t]+(.+?)[ \\t]*$");
            if (m.Success) headings.Add((m.Groups[1].Length, Regex.Replace(m.Groups[2].Value, "[ \\t]+#+[ \\t]*$", ""), line.Start, line.Start + line.Raw.Length, line.Raw));
        }
        for (int i = 0; i < headings.Count; i++)
        {
            var h = headings[i];
            if (h.level != 2) continue;
            int end = i + 1 < headings.Count ? headings[i + 1].start : source.Length;
            Sections.Add(new(h.level, h.title, h.start, h.content, end, h.raw, source[h.content..end]));
        }
    }
    public Heading? Unique(string title)
    {
        var matches = Sections.Where(s => s.Title == title).ToList();
        if (matches.Count > 1) throw new IOException("存在重复栏目，无法安全定位：“" + title + "”。请在 Obsidian 中调整标题。");
        return matches.FirstOrDefault();
    }
    public static IEnumerable<(int Start, string Text, string Raw)> Lines(string source)
    {
        foreach (Match m in Regex.Matches(source, "[^\\n]*\\n|[^\\n]+$")) yield return (m.Index, m.Value.TrimEnd('\r', '\n'), m.Value);
    }
    public static IEnumerable<(int Start, string Text, string Raw)> VisibleLines(string source, bool allowTrailingComments = false)
    {
        char fence = '\0'; int fenceCount = 0; bool comment = false;
        foreach (var line in Lines(source))
        {
            if (fence != '\0')
            {
                if (Regex.IsMatch(line.Text, "^ {0,3}" + Regex.Escape(fence.ToString()) + "{" + fenceCount + ",}[ \\t]*$")) fence = '\0';
                continue;
            }
            var wasComment = comment;
            if (!comment)
            {
                var f = Regex.Match(line.Text, "^ {0,3}(`{3,}|~{3,})");
                if (f.Success) { fence = f.Groups[1].Value[0]; fenceCount = f.Groups[1].Length; continue; }
            }
            foreach (Match token in Regex.Matches(line.Text, "<!--|-->")) comment = token.Value == "<!--";
            if (!wasComment && !comment)
            {
                int commentStart = line.Text.IndexOf("<!--", StringComparison.Ordinal);
                if (commentStart < 0) yield return line;
                // Form controls may edit the visible prefix while keeping a closed comment suffix verbatim.
                // The heading parser retains its original behavior unless this option is explicitly requested.
                else if (allowTrailingComments && Regex.IsMatch(line.Text[commentStart..], @"\A(?:<!--.*?-->[ \t]*)+\z"))
                    yield return (line.Start, line.Text[..commentStart].TrimEnd(' ', '\t'), line.Raw);
            }
        }
    }
    public string Apply(Dictionary<string, string> changes, IReadOnlyList<Heading> template)
    {
        var result = Source;
        var replacements = new List<(int start, int length, string value)>();
        var additions = new StringBuilder();
        foreach (var t in template)
        {
            if (!changes.TryGetValue(t.Title, out var body)) continue;
            var h = Unique(t.Title);
            if (h != null) replacements.Add((h.ContentStart, h.End - h.ContentStart, EnsureBoundary(body, h.End < Source.Length)));
            else additions.Append(NewLine).Append(t.RawHeading.TrimEnd('\r','\n')).Append(NewLine).Append(EnsureBoundary(body, true));
        }
        foreach (var r in replacements.OrderByDescending(r => r.start)) result = result.Remove(r.start, r.length).Insert(r.start, r.value);
        if (additions.Length > 0) result = result.TrimEnd('\r','\n') + NewLine + additions;
        return result;
    }
    public string EnsureBoundary(string text, bool required) => required && !text.EndsWith('\n') ? text + NewLine : text;
}

public sealed class SaveConflictException(string message) : IOException(message);
public sealed class Draft
{
    public string Target { get; set; } = "";
    public string Date { get; set; } = "";
    public string? Baseline { get; set; }
    public Dictionary<string, string> Bodies { get; set; } = new();
    public bool Pending { get; set; }
    public string Reason { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
public sealed class DiarySession
{
    public CardConfig Config { get; }
    public DateOnly Date { get; }
    public string Target { get; }
    public string TemplateText { get; private set; }
    public IReadOnlyList<Heading> Template { get; private set; }
    private string rawTemplate;
    public string TemplateNotice {get;private set;}="";
    public string TemplateCachePath=>Path.Combine(Config.UserDataPath,"templates",Hash(Config.TemplatePath.ToUpperInvariant())[..24]+".md");
    public IReadOnlyList<Heading> ExtraSections=>new MarkdownDoc(Baseline??TemplateText).Sections.Where(h=>!Template.Any(t=>t.Title==h.Title)).ToArray();
    public string? Baseline { get; private set; }
    public Dictionary<string,string> Bodies { get; private set; } = new();
    private Dictionary<string,string> effectiveBaseline = new();
    public bool Restored { get; private set; }
    public string? Error { get; private set; }
    public bool IsConflict { get; private set; }
    public bool Dirty => Bodies.Any(p => !effectiveBaseline.TryGetValue(p.Key, out var b) || p.Value != b);
    public string RecoveryPath => Path.Combine(Config.UserDataPath, "recovery", Key + ".json");
    public string Key => Hash(Target.ToUpperInvariant())[..24];
    public static readonly UTF8Encoding Utf8 = new(false, true);
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(text)));
    public DiarySession(CardConfig config, DateOnly date, bool restore = true)
    {
        Config = config; Date = date; Target = config.PathFor(date);
        var requested=ReadText(config.TemplatePath);rawTemplate=requested;
        var previousCache=Config.PreviousPaths(Config.TemplatePath).Select(path=>Path.Combine(Config.UserDataPath,"templates",Hash(path.ToUpperInvariant())[..24]+".md")).Prepend(TemplateCachePath).FirstOrDefault(File.Exists);
        if(previousCache!=null)try{var cached=ReadText(previousCache);_=TemplateReconciler.Parse(cached);rawTemplate=cached;}catch(IOException){}
        TemplateText=ExpandTemplate(rawTemplate);Template=TemplateReconciler.Parse(TemplateText);
        Reset(File.Exists(Target) ? ReadText(Target) : null);
        var recovery=Config.PreviousPaths(Target).Select(path=>Path.Combine(Config.UserDataPath,"recovery",Hash(path.ToUpperInvariant())[..24]+".json")).Prepend(RecoveryPath).FirstOrDefault(File.Exists);
        if (restore && recovery!=null)
        {
            try
            {
                var d = JsonSerializer.Deserialize<Draft>(File.ReadAllText(recovery), CardConfig.Json);
                if (d is { Pending: true } && Config.ResolvePath(d.Target,Config.VaultPath).Equals(Target, StringComparison.OrdinalIgnoreCase) && d.Date == Date.ToString("yyyy-MM-dd"))
                {
                    var previous=new MarkdownDoc(d.Baseline??"");
                    Template=Template.Concat(d.Bodies.Keys.Where(title=>!Template.Any(t=>t.Title==title)).Select(title=>previous.Unique(title)??new Heading(2,title,0,0,0,"## "+title+"\n",""))).ToArray();
                    Reset(d.Baseline);
                    foreach (var t in Template) if (d.Bodies.TryGetValue(t.Title, out var value)) Bodies[t.Title] = value;
                    Restored = Dirty;
                }
            }
            catch (JsonException) { Error = "恢复草稿格式异常，原文件已保留：" + RecoveryPath; }
        }
        if(rawTemplate!=requested||Template.Count!=TemplateReconciler.Parse(ExpandTemplate(requested)).Count)
            try{ReloadTemplate(ExpandTemplate(requested));}catch(IOException e){Error="模板更新尚未应用，输入仍保留："+e.Message;}
        SaveTemplateCache();
    }
    private string ExpandTemplate(string value)=>value.Replace("{{date:YYYY-MM-DD}}",Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
    private void SaveTemplateCache(){try{AtomicText(TemplateCachePath,rawTemplate);}catch(IOException){}catch(UnauthorizedAccessException){}}
    public bool ReloadTemplate(string? expected=null)
    {
        var raw=ReadText(Config.TemplatePath);var text=ExpandTemplate(raw);var fresh=TemplateReconciler.Parse(text);
        if(expected!=null&&text!=expected)throw new IOException("模板仍在保存，请稍后再试。");
        if(text==TemplateText&&Template.Select(t=>t.Title).SequenceEqual(fresh.Select(t=>t.Title)))return false;
        if(Dirty)Save();
        string? next=Baseline;string notice="";
        EnsureSafePath(Config.DiaryFolder,Target);
        if(File.Exists(Target))
        {
            using var file=OpenLocked(Target);if(file.Length>8*1024*1024)throw new IOException("日记超过 8 MB，已停止同步。");
            var bytes=new byte[file.Length];file.ReadExactly(bytes);var latest=Decode(bytes);
            next=TemplateReconciler.Update(latest,TemplateText,text,out notice);
            if(Utf8.GetByteCount(next)>8*1024*1024)throw new IOException("更新后的日记超过 8 MB，已停止同步。");
            if(next!=latest)
            {
                AtomicText(Path.Combine(Config.UserDataPath,"backups",Key+".template-previous.md"),latest);
                var output=Utf8.GetBytes(next);
                try{file.Position=0;file.Write(output);file.SetLength(output.Length);file.Flush(true);}
                catch{try{file.Position=0;file.Write(bytes);file.SetLength(bytes.Length);file.Flush(true);}catch{}throw;}
            }
        }
        else if(Baseline!=null)throw new SaveConflictException("日记被外部删除或移动，暂不更新模板，以保留原内容。");
        rawTemplate=raw;TemplateText=text;Template=fresh;Reset(next);TemplateNotice=notice;SaveTemplateCache();
        try{PersistDraft("模板已同步");}catch(IOException){TemplateNotice+="模板已应用，但恢复记录暂时无法写入。";}catch(UnauthorizedAccessException){TemplateNotice+="模板已应用，但恢复记录暂时无法写入。";}
        return true;
    }
    private void Reset(string? source)
    {
        var doc = new MarkdownDoc(source ?? TemplateText);
        Baseline = source;
        Bodies = Template.ToDictionary(t => t.Title, t => doc.Unique(t.Title)?.Body ?? t.Body);
        effectiveBaseline = new(Bodies);
        IsConflict = false; Error = null;
    }
    public void Edit(string title, string body)
    {
        if (!Bodies.ContainsKey(title)) throw new ArgumentException("未知栏目");
        Bodies[title] = body;
    }
    public void PersistDraft(string reason = "等待保存")
    {
        AtomicText(RecoveryPath, JsonSerializer.Serialize(new Draft {Target=Target, Date=Date.ToString("yyyy-MM-dd"), Baseline=Baseline, Bodies=Bodies, Pending=Dirty, Reason=reason, UpdatedAt=DateTime.Now}, CardConfig.Json));
    }
    public string LocalDocument() => new MarkdownDoc(Baseline ?? TemplateText).Apply(Bodies, Template);
    public string ArchiveDraft(string reason)
    {
        PersistDraft(reason);
        var file = Path.Combine(Config.UserDataPath, "recovery", Key + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-ffff") + ".md");
        AtomicText(file, "<!-- 卡片恢复草稿；原文件：" + Target.Replace("--", "—") + " -->\n" + LocalDocument());
        return file;
    }
    public bool ReloadExternal()
    {
        if (Dirty) return false;
        var latest = File.Exists(Target) ? ReadText(Target) : null;
        if (latest == null && Baseline != null) throw new SaveConflictException("日记已被外部删除或移动，卡片内容保留；请通过恢复菜单处理。");
        if (latest == Baseline) return false;
        Reset(latest); return true;
    }
    public void ReloadKeepingDraft()
    {
        ArchiveDraft("用户选择保留草稿并重新载入磁盘");
        Reset(File.Exists(Target) ? ReadText(Target) : null);
        PersistDraft("本地冲突稿已另存为 Markdown");
    }
    public void EnsureDailyFile()
    {
        if(Dirty){Save();return;}
        EnsureSafePath(Config.DiaryFolder,Target);
        if(File.Exists(Target)){Reset(ReadText(Target));return;}
        if(Baseline!=null)throw new SaveConflictException("当天日记已被删除或移动，已停止自动创建，以保护原有内容。");
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        var text=LocalDocument();
        var temp=Path.Combine(Path.GetDirectoryName(Target)!,".diary-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            WriteDurable(temp,Utf8.GetBytes(text));
            try{File.Move(temp,Target,false);}
            catch(IOException) when(File.Exists(Target)){Reset(ReadText(Target));return;}
            Reset(text);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public void Save()
    {
        if (!Dirty) return;
        try
        {
            PersistDraft("正在保存");
            EnsureSafePath(Config.DiaryFolder, Target);
            Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
            if (!File.Exists(Target))
            {
                if (Baseline != null) throw new SaveConflictException("日记被外部删除或移动，已暂停写入。你的内容保留在恢复草稿中。");
                var text = Merge(null);
                var temp = Path.Combine(Path.GetDirectoryName(Target)!, ".diary-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    WriteDurable(temp, Utf8.GetBytes(text));
                    try { File.Move(temp, Target, false); }
                    catch (IOException) when (File.Exists(Target)) { SaveExisting(); return; }
                    Reset(text); PersistDraft("已保存"); return;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            SaveExisting();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Error = e.Message; IsConflict = e is SaveConflictException;
            try { PersistDraft(Error); } catch { /* 当前输入仍保留在内存，UI 明确报告草稿也可能失败。 */ }
            throw;
        }
    }
    private void SaveExisting()
    {
        using var file = OpenLocked(Target);
        if (file.Length > 8 * 1024 * 1024) throw new IOException("日记超过 8 MB，已停止自动写入。");
        var bytes = new byte[file.Length]; file.ReadExactly(bytes);
        var latest = Decode(bytes);
        var result = Merge(latest);
        if (result != latest)
        {
            // 同一独占句柄内比较和更新，避免释放锁后替换文件时覆盖 Obsidian 的新修改。
            // 更新前持久保存完整草稿和上一版，断电或中途失败可恢复。
            var backup = Path.Combine(Config.UserDataPath, "backups", Key + ".previous.md");
            AtomicText(backup, latest);
            var output = Utf8.GetBytes(result);
            try { file.Position=0; file.Write(output); file.SetLength(output.Length); file.Flush(true); }
            catch
            {
                try { file.Position=0; file.Write(bytes); file.SetLength(bytes.Length); file.Flush(true); } catch { }
                throw;
            }
        }
        Reset(result); PersistDraft("已保存");
    }
    private string Merge(string? disk)
    {
        var original = new MarkdownDoc(Baseline ?? TemplateText);
        var current = new MarkdownDoc(disk ?? TemplateText);
        var updates = new Dictionary<string,string>();
        var conflicts = new List<string>();
        foreach (var t in Template)
        {
            var local = Bodies[t.Title];
            if (local == effectiveBaseline[t.Title]) continue;
            var old = Baseline == null ? new MarkdownDoc(TemplateText).Unique(t.Title)?.Body : original.Unique(t.Title)?.Body;
            var other = current.Unique(t.Title)?.Body;
            if (other != old && other != local) { conflicts.Add(t.Title); continue; }
            foreach (var marker in new[]{"<!-- TASKS:START -->", "<!-- TASKS:END -->"})
                if (old?.Contains(marker) == true && Count(local, marker) != Count(old, marker)) throw new IOException("任务区域标记不能删除或重复，请恢复标记后重试：" + marker);
            updates[t.Title] = local;
        }
        if (conflicts.Count > 0) throw new SaveConflictException("以下栏目同时被外部修改：" + string.Join("、", conflicts) + "。已暂停自动保存，双方内容均保留。");
        var merged = current.Apply(updates, Template);
        if(Utf8.GetByteCount(merged)>8*1024*1024)throw new IOException("日记超过 8 MB，已保留输入并停止写入。");
        return merged;
    }
    private static int Count(string s, string token) => (s.Length - s.Replace(token, "").Length) / token.Length;
    private static FileStream OpenLocked(string path)
    {
        for (int i=0;;i++)
        {
            try { return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i<3) { Thread.Sleep(65); }
        }
    }
    public static string ReadText(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (f.Length > 8*1024*1024) throw new IOException("文件超过 8 MB，无法在卡片中编辑。");
        var bytes = new byte[f.Length]; f.ReadExactly(bytes); return Decode(bytes);
    }
    private static string Decode(byte[] bytes)
    {
        try{return Utf8.GetString(bytes);}
        catch(DecoderFallbackException e){throw new IOException("文件不是有效的 UTF-8 编码，已停止读取和写入。请在编辑器中转换编码后重试。",e);}
    }
    private static void EnsureSafePath(string root, string target)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(target)!);
        while (dir != null)
        {
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("日记路径含有链接或重解析目录，已停止写入：" + dir.FullName);
            dir = dir.Parent;
        }
        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("日记文件是链接，已停止写入。");
    }
    public static void AtomicText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { WriteDurable(temp, Utf8.GetBytes(text)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void WriteDurable(string path, byte[] bytes)
    {
        using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); f.Write(bytes); f.Flush(true);
    }
}

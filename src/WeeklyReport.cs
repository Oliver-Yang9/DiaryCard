using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DailyDiaryCard;

public sealed record WeeklyWindow(DateOnly Saturday,DateOnly Friday,string Id,int Year,DateTimeOffset Now)
{
    public static readonly TimeSpan Beijing=TimeSpan.FromHours(8);
    public static WeeklyWindow ForDate(DateOnly date,DateTimeOffset now)
    {
        var friday=date.AddDays((5-(int)date.DayOfWeek+7)%7);var saturday=friday.AddDays(-6);var dt=friday.ToDateTime(TimeOnly.MinValue);
        int year=ISOWeek.GetYear(dt),week=ISOWeek.GetWeekOfYear(dt);
        return new(saturday,friday,$"{year}-W{week:00}",year,now.ToOffset(Beijing));
    }
    public static WeeklyWindow Latest(DateTimeOffset now)
    {
        var local=now.ToOffset(Beijing);var day=DateOnly.FromDateTime(local.DateTime);
        var friday=day.AddDays(-(((int)day.DayOfWeek-5+7)%7));
        if(new DateTimeOffset(friday.ToDateTime(new TimeOnly(12,0)),Beijing)>local)friday=friday.AddDays(-7);
        return ForDate(friday,now);
    }
    public static WeeklyWindow Latest(DateTimeOffset now,WeeklySettings schedule)
    {
        schedule.ValidateTime();
        var local=now.ToOffset(Beijing);var day=DateOnly.FromDateTime(local.DateTime);
        int mondayIndex=((int)day.DayOfWeek+6)%7;
        var anchor=day.AddDays(-((mondayIndex-schedule.Weekday+7)%7));
        DateTimeOffset Trigger(DateOnly date)=>new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue),Beijing).AddHours(schedule.Hour).AddMinutes(schedule.Minute);
        if(Trigger(anchor)>local)anchor=anchor.AddDays(-7);
        // Select the latest Friday on or before the logical scheduled day.
        var friday=anchor.AddDays(-(((int)anchor.DayOfWeek-5+7)%7));
        return ForDate(friday,now);
    }
    public string Start=>Saturday.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    public string End=>Friday.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
}
public sealed record WeeklyEntry(string Heading,string Category,string Label,string Text);
public sealed record WeeklyNote(string Relative,string Date,List<WeeklyEntry> Entries);
public sealed class WeeklyCatalog
{
    public HashSet<string> Lines{get;}=new(StringComparer.Ordinal);
    public List<string> Prompts{get;}=new();
    public void Add(string text)
    {
        foreach(var raw in WeeklyReport.Split(text).Body.Split('\n'))
        {
            if(Regex.IsMatch(raw,@"^\s*#"))continue;
            string line=WeeklyReport.Strip(raw);if(line.Length==0)continue;
            Lines.Add(WeeklyReport.Canonical(line));
            if(Regex.IsMatch(line,@"[：:]$")&&line.Length>2&&!line.Contains('|')&&!Prompts.Contains(line))Prompts.Add(line);
        }
        Prompts.Sort((a,b)=>b.Length.CompareTo(a.Length));
    }
}
public sealed record WeeklyPreview(CardConfig Config,WeeklyWindow Window,string OutputRoot,string Path,byte[]? Expected,byte[] Output,string Markdown,string Fingerprint,string Action,bool MergeRequired,int ValidNotes,int Excerpts,int EmptyNotes,int InvalidDates);
public sealed record WeeklyResult(string Path,string Action,bool MergeRequired,int ValidNotes,int Excerpts,string Week);
public static class WeeklyReport
{
    public const string Start="<!-- AUTO:START -->",End="<!-- AUTO:END -->";
    private static readonly string[] Categories={"工作成果与证据","有效做法","学习内容","问题与风险","后续计划","其他记录"};
    private static readonly UTF8Encoding Utf8=new(false,true);
    public static (Dictionary<string,string> Meta,string Body) Split(string text)
    {
        text=text.TrimStart('\uFEFF');var meta=new Dictionary<string,string>(StringComparer.Ordinal);
        var yaml=Regex.Match(text,@"\A---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)");
        if(yaml.Success)
        {
            foreach(var line in yaml.Groups[1].Value.Split('\n'))
            {
                var m=Regex.Match(line,@"^(type|date|journal_date)\s*:\s*(.*?)\s*$");
                if(m.Success)meta[m.Groups[1].Value]=Scalar(m.Groups[2].Value);
            }
            text=text[yaml.Length..];
        }
        text=Regex.Replace(text,@"<!--\s*AUTO:START\s*-->[\s\S]*?<!--\s*AUTO:END\s*-->","");
        text=Regex.Replace(text,@"<!--[\s\S]*?(?:-->|\z)","");
        return(meta,text);
    }
    private static string Scalar(string value)
    {
        value=value.Trim();var quoted=Regex.Match(value,"^(\"(?:\\\\.|[^\"\\\\])*\"|'(?:''|[^'])*')\\s*(?:#.*)?$");
        if(quoted.Success)
        {
            value=quoted.Groups[1].Value;if(value[0]=='\'')return value[1..^1].Replace("''","'");
            try{return JsonSerializer.Deserialize<string>(value)??"";}catch(JsonException){return value[1..^1];}
        }
        return Regex.Replace(value,@"\s+#.*$","").Trim();
    }
    public static string Strip(string value)=>Regex.Replace(Regex.Replace(Regex.Replace(value.Trim(),@"^>\s*",""),@"^[-*+]\s*(?:\[[ xX]\]\s*)?",""),@"^\d+[.)、]\s*","").Trim();
    public static string Canonical(string text)=>Regex.Replace(Regex.Replace(Regex.Replace(Strip(text),@"\{\{[^}]+\}\}","@DATE"),@"\b\d{4}-\d{2}-\d{2}\b","@DATE"),@"\s+"," ").Trim();
    private static bool Placeholder(string text)
    {
        var s=Regex.Replace(text,@"\s+","");
        return s.Length==0||Regex.IsMatch(s,@"^[-—_~.。:：…]+$")||Regex.IsMatch(s,@"^(?:TODO|TBD|待填写|待补充|待填|暂无内容|\{\{[^}]+\}\})[：:。.!！?？]*$",RegexOptions.IgnoreCase)||ValidDate(s)!=null;
    }
    public static DateOnly? ValidDate(string? value)=>value!=null&&Regex.IsMatch(value,@"^\d{4}-\d{2}-\d{2}$")&&DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:null;
    public static string Category(string heading)
    {
        if(Regex.IsMatch(heading,"下一|明天|第二日任务规划|后续|一个动作|收工前|开始"))return "后续计划";
        if(Regex.IsMatch(heading,"做法|值得保留|保留|有效|收获的方法"))return "有效做法";
        if(Regex.IsMatch(heading,"卡在|问题|阻塞|风险|说出来|错题|错因|纠正"))return "问题与风险";
        if(Regex.IsMatch(heading,"学到|学了|学习|回忆|把今天连起来|知识|复习|自测"))return "学习内容";
        if(Regex.IsMatch(heading,"推进|交付|成果|完成|今日临时任务|晚间回看|留时间"))return "工作成果与证据";
        return "其他记录";
    }
    private static (string Category,string Label) Context(string prompt,string heading)
    {
        if(Regex.IsMatch(prompt,"最想推进|要推进的任务|今天想交付|准备用来"))return("工作成果与证据","计划（原文）");
        if(Regex.IsMatch(prompt,"实际完成的结果|今天留下的成果"))return("工作成果与证据","结果或证据（原文）");
        if(prompt.Contains("一个有效做法"))return("有效做法","做法与适用条件");
        if(prompt.Contains("观察到的事实"))return("问题与风险","观察事实");
        if(prompt.Contains("可能原因"))return("问题与风险","可能原因（待确认）");
        if(Regex.IsMatch(prompt,"下次想试|想调整|怎样处理|具体动作|小步骤|动手|下次开始"))return("后续计划","记录中的后续安排");
        if(prompt.Contains("学习任务"))return("学习内容","学习记录（原文）");
        return(Category(heading),prompt.TrimEnd('：',':'));
    }
    public static List<WeeklyEntry> Extract(string body,WeeklyCatalog catalog)
    {
        var result=new List<WeeklyEntry>();var seen=new HashSet<WeeklyEntry>();string heading="",parent="";(string Category,string Label)? context=null;string[]? table=null;
        char fence='\0';int fenceLength=0;var lines=body.Replace("\r\n","\n").Split('\n');
        void Add(WeeklyEntry entry){if(seen.Add(entry))result.Add(entry);}
        for(int i=0;i<lines.Length;i++)
        {
            string line=lines[i].Trim();var fm=Regex.Match(line,@"^(\x60{3,}|~{3,})");
            if(fm.Success){if(fence=='\0'){fence=fm.Value[0];fenceLength=fm.Length;}else if(fm.Value[0]==fence&&fm.Length>=fenceLength){fence='\0';}continue;}
            if(fence!='\0')continue;
            var h=Regex.Match(line,@"^(#{1,6})\s+(.+?)\s*#*\s*$");
            if(h.Success){heading=h.Groups[2].Value;if(h.Groups[1].Length<=2)parent=heading;context=null;table=null;continue;}
            if(line.Length==0||Regex.IsMatch(line,@"^[-*_]{3,}$"))continue;
            string effective=Category(parent)=="后续计划"?parent:heading;
            if(line.StartsWith('|'))
            {
                var cells=line.Trim('|').Split('|').Select(x=>x.Trim()).ToArray();
                if(cells.All(x=>x.Length==0||Regex.IsMatch(x,@"^:?-{2,}:?$")))continue;
                if((i+1<lines.Length&&Regex.IsMatch(lines[i+1].Trim(),@"^\|\s*:?-{2,}:?\s*(?:\|\s*:?-{2,}:?\s*)+\|?$"))||catalog.Lines.Contains(Canonical(line))){table=cells;continue;}
                if(cells.Any(x=>!Placeholder(x)))
                {
                    string text=string.Join("；",cells.Select((x,n)=>x.Length>0?$"{(table!=null&&n<table.Length?table[n]:$"第{n+1}列")}：{x}":"").Where(x=>x.Length>0));
                    Add(new(heading,Category(effective),"表格记录（原文）",text));
                }
                continue;
            }
            var checkbox=Regex.Match(line,@"^[-*+]\s+\[([ xX])\]\s*(.*)$");
            string value=Strip(line);string? prompt=catalog.Prompts.FirstOrDefault(p=>value.StartsWith(p,StringComparison.Ordinal));
            if(prompt!=null){context=Context(prompt,effective);value=value[prompt.Length..].Trim();}
            if(Placeholder(value)||catalog.Lines.Contains(Canonical(value)))continue;
            string category=context?.Category??Category(effective),label=context?.Label??"";
            bool planned=Category(effective)=="后续计划";if(planned)category="后续计划";
            if(checkbox.Success)
            {
                if(!checkbox.Groups[1].Value.Equals("x",StringComparison.OrdinalIgnoreCase)){category="后续计划";label="未完成待办（原文；按原记录日期理解）";}
                else if(planned)label="计划事项（原文已勾选；按原记录日期理解）";
                else{if(heading.Contains("每日任务完成情况"))category="工作成果与证据";label="已勾选事项（原文）";}
            }
            Add(new(heading,category,label,value));
        }
        return result;
    }
    private static void SafeFile(string path)
    {
        SetupService.SafeDirectory(Path.GetDirectoryName(path)!);
        if(Directory.Exists(path)||(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0))throw new IOException("不是普通文件，已停止周报读写："+path);
    }
    private static byte[]? Read(string path)
    {
        SafeFile(path);if(!File.Exists(path))return null;
        using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(f.Length>8*1024*1024)throw new IOException("文件超过 8 MB，无法安全汇总："+path);
        var bytes=new byte[f.Length];f.ReadExactly(bytes);return bytes;
    }
    private static IEnumerable<string> Walk(string directory,string? excluded=null)
    {
        SetupService.SafeDirectory(directory);if(!Directory.Exists(directory))yield break;
        foreach(var entry in Directory.EnumerateFileSystemEntries(directory).OrderBy(p=>p,StringComparer.Ordinal))
        {
            var attr=File.GetAttributes(entry);if((attr&FileAttributes.ReparsePoint)!=0)continue;
            if(excluded!=null&&SetupService.IsUnder(excluded,entry))continue;
            if((attr&FileAttributes.Directory)!=0){if(Path.GetFileName(entry).StartsWith('.'))continue;foreach(var file in Walk(entry,excluded))yield return file;}
            else if(entry.EndsWith(".md",StringComparison.OrdinalIgnoreCase))yield return entry;
        }
    }
    public static string OutputRoot(CardConfig config,string folder)
    {
        if(string.IsNullOrWhiteSpace(folder))throw new IOException("请填写周报保存目录。");
        var root=Path.GetFullPath(folder.Trim(),config.VaultPath);
        if(!SetupService.IsUnder(config.VaultPath,root)||root.Equals(config.VaultPath,StringComparison.OrdinalIgnoreCase))throw new IOException("周报目录必须是当前日记库中的独立子目录。");
        if(SetupService.IsUnder(root,config.DiaryFolder)||SetupService.IsUnder(root,config.TemplatePath)||SetupService.IsUnder(root,config.UserDataPath))throw new IOException("周报目录不能包含日记目录、模板或程序数据。");
        SetupService.SafeDirectory(root);return root;
    }
    public static WeeklyPreview Preview(CardConfig config,WeeklyWindow window,string folder="周报")
    {
        SetupService.SafeDirectory(config.VaultPath);SetupService.SafeDirectory(config.DiaryFolder);
        string outputRoot=OutputRoot(config,folder);
        var catalog=new WeeklyCatalog();catalog.Add(DefaultAssets.Template);catalog.Add("一个可以直接动手的小步骤：\n写下明天要做的具体事项：\n");
        var templates=new HashSet<string>(StringComparer.OrdinalIgnoreCase){config.TemplatePath};
        foreach(var path in Walk(Path.Combine(config.VaultPath,"模板")))templates.Add(path);
        foreach(var path in templates)if(File.Exists(path))catalog.Add(Utf8.GetString(Read(path)!));
        var notes=new List<WeeklyNote>();var empty=new List<string>();var invalid=new List<string>();int excludedType=0,outside=0;
        foreach(var path in Walk(config.DiaryFolder,outputRoot))
        {
            if(templates.Contains(path)||SetupService.IsUnder(config.UserDataPath,path))continue;
            var parsed=Split(Utf8.GetString(Read(path)!));string relative=Path.GetRelativePath(config.VaultPath,path).Replace('\\','/');
            if(parsed.Meta.GetValueOrDefault("type")!="diary"){excludedType++;continue;}
            if(Regex.IsMatch(Path.GetFileNameWithoutExtension(path),"周报|周复盘")||Regex.IsMatch(parsed.Body,@"(?m)^#\s+.*(?:周报|周复盘)"))continue;
            var date=ValidDate(parsed.Meta.GetValueOrDefault("journal_date"))??ValidDate(Path.GetFileNameWithoutExtension(path))??ValidDate(parsed.Meta.GetValueOrDefault("date"));
            if(date==null){invalid.Add(relative);continue;}
            if(date<window.Saturday||date>window.Friday){outside++;continue;}
            var entries=Extract(parsed.Body,catalog);
            if(entries.Count==0)empty.Add(relative);else notes.Add(new(relative,date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),entries));
        }
        notes=notes.OrderBy(n=>n.Date,StringComparer.Ordinal).ThenBy(n=>n.Relative,StringComparer.Ordinal).ToList();
        string fingerprint=DiarySession.Hash(JsonSerializer.Serialize(new{Version="diarycard-weekly-saturday-v1",window.Id,window.Start,window.End,notes,empty,invalid,excludedType,outside}));
        string body=Body(window,notes,empty,invalid,fingerprint);
        string file=Path.Combine(outputRoot,window.Year.ToString(CultureInfo.InvariantCulture),window.Id+"-工作周报.md");
        byte[]? expected=Read(file);bool merge=expected!=null&&Markers(expected)==null;
        if(merge)
        {
            string basic=Path.Combine(Path.GetDirectoryName(file)!,window.Id+"-工作周报-待合并.md");file=basic;expected=Read(file);
            if(expected!=null&&!(Markers(expected) is {} info&&Utf8.GetString(info.Body).Contains("<!-- INPUT:SHA256 "+fingerprint+" -->",StringComparison.Ordinal)))
            {
                int number=0;string suffix=window.Now.ToString("yyyyMMddHHmmss",CultureInfo.InvariantCulture);
                do{file=Path.Combine(Path.GetDirectoryName(basic)!,window.Id+"-工作周报-待合并-"+suffix+(number==0?"":"-"+number)+".md");number++;}while(File.Exists(file)||Directory.Exists(file));
                expected=null;
            }
        }
        string action=expected==null?(merge?"merge-required":"created"):"updated";
        byte[] output;
        if(expected!=null&&Markers(expected) is {} markers)
        {
            if(Utf8.GetString(markers.Body).Contains("<!-- INPUT:SHA256 "+fingerprint+" -->",StringComparison.Ordinal)){action="unchanged";output=expected;}
            else{string eol=Utf8.GetString(expected).Contains("\r\n")?"\r\n":"\n";output=markers.Prefix.Concat(Utf8.GetBytes(eol+body.Replace("\n",eol)+eol+eol)).Concat(markers.Suffix).ToArray();}
        }
        else output=Utf8.GetBytes($"---\ntype: weekly-report\ndate: \"{window.Now:yyyy-MM-dd}\"\ntags:\n  - 周报\n  - 工作\nsources: []\n---\n\n# {window.Id} 工作周报\n\n{Start}\n{body}\n\n{End}\n\n## 手工补充\n\n<!-- 在这里填写补充和反馈；自动重跑会保留此区域。 -->\n");
        return new(config,window,outputRoot,file,expected,output,Utf8.GetString(output),fingerprint,action,merge,notes.Count,notes.Sum(n=>n.Entries.Count),empty.Count,invalid.Count);
    }
    private static string Link(WeeklyNote note,string heading)
    {
        string file=note.Relative[..^3],safe=Regex.Replace(heading,@"[\[\]|]","");
        return $"[[{file}{(safe.Length>0?"#"+safe:"")}|{note.Date}{(safe.Length>0?" · "+safe:"")}]]";
    }
    private static string Body(WeeklyWindow window,List<WeeklyNote> notes,List<string> empty,List<string> invalid,string fingerprint)
    {
        var lines=new List<string>{"<!-- INPUT:SHA256 "+fingerprint+" -->","","## 本周概况","",$"- 统计周次：{window.Id}。",$"- 统计范围：{window.Start} 至 {window.End}（周六至下周五，按归属日期纳入）。",$"- 本次读取时间：{window.Now:yyyy-MM-dd HH:mm:ss zzz}。",$"- 有效日记：{notes.Count} 篇，涉及 {notes.Select(n=>n.Date).Distinct().Count()} 个记录日。","- 按日记原文合并摘编；计划、可能原因和未完成事项保留原有状态。","- 按归属日期纳入；本次读取文件现有内容，无法还原周五中午的历史版本。"};
        if(window.Now<new DateTimeOffset(window.Friday.AddDays(1).ToDateTime(TimeOnly.MinValue),WeeklyWindow.Beijing))lines.Add("- 当前周期尚未结束，仅包含读取时已经保存的内容。");
        if(notes.Count==0)lines.AddRange(new[]{"","**本周未发现有效记录。**","","空模板、提示和来源说明不作为成果。"});
        foreach(string category in Categories)
        {
            var groups=new Dictionary<(string Label,string Text),List<string>>();
            foreach(var note in notes)foreach(var entry in note.Entries.Where(e=>e.Category==category))
            {
                var key=(entry.Label,entry.Text);if(!groups.TryGetValue(key,out var links))groups[key]=links=new();
                string link=Link(note,entry.Heading);if(!links.Contains(link))links.Add(link);
            }
            if(category=="其他记录"&&groups.Count==0)continue;
            lines.AddRange(new[]{"","## "+category,""});if(groups.Count==0)lines.Add("暂无可据以摘编的记录。");
            foreach(var pair in groups)lines.Add("- "+(pair.Key.Label.Length>0?"**"+pair.Key.Label+"**：":"")+pair.Key.Text+"（来源："+string.Join("；",pair.Value)+"）");
            if(category=="后续计划"&&groups.Count>0)lines.AddRange(new[]{"","以上是原日记中的后续安排，保留其原日期语境；不自动改成下周承诺。"});
        }
        lines.AddRange(new[]{"","## 参考笔记",""});if(notes.Count==0)lines.Add("无有效来源笔记。");
        foreach(var note in notes)lines.Add($"- {Link(note,"")}，归属日期 {note.Date}；采用 {note.Entries.Count} 条实写摘录。路径：\u0060{note.Relative}\u0060。");
        if(empty.Count>0){lines.AddRange(new[]{"",$"本周排除 {empty.Count} 篇空模板或仅提示的笔记："});lines.AddRange(empty.Select(p=>"- \u0060"+p+"\u0060"));}
        if(invalid.Count>0){lines.AddRange(new[]{"","以下日记缺少可确认的归属日期，未纳入："});lines.AddRange(invalid.Select(p=>"- \u0060"+p+"\u0060"));}
        return string.Join("\n",lines);
    }
    public sealed record AutoMarkers(byte[] Prefix,byte[] Body,byte[] Suffix);
    public static AutoMarkers? Markers(byte[] bytes)
    {
        var a=Utf8.GetBytes(Start);var b=Utf8.GetBytes(End);var span=bytes.AsSpan();int start=span.IndexOf(a),end=span.IndexOf(b);
        if(start<0||end<start+a.Length||span[(start+a.Length)..].IndexOf(a)>=0||span[(end+b.Length)..].IndexOf(b)>=0)return null;
        return new(bytes[..(start+a.Length)],bytes[(start+a.Length)..end],bytes[end..]);
    }
    public static WeeklyResult Save(WeeklyPreview preview)
    {
        OutputRoot(preview.Config,Path.GetRelativePath(preview.Config.VaultPath,preview.OutputRoot));SafeFile(preview.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(preview.Path)!);SetupService.SafeDirectory(Path.GetDirectoryName(preview.Path)!);
        if(preview.Expected==null)
        {
            string temp=preview.Path+".tmp-"+Guid.NewGuid().ToString("N");
            try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(preview.Output);f.Flush(true);}File.Move(temp,preview.Path,false);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        else
        {
            using var file=new FileStream(preview.Path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            if(file.Length!=preview.Expected.Length)throw new IOException("周报已被外部修改，请重新预览后生成。");
            var current=new byte[file.Length];file.ReadExactly(current);
            if(!current.SequenceEqual(preview.Expected))throw new IOException("周报已被外部修改，请重新预览后生成。");
            if(preview.Action!="unchanged")
            {
                string backup=Path.Combine(preview.Config.UserDataPath,"weekly","backups",DiarySession.Hash(preview.Path)[..20]+"-"+DateTime.UtcNow.Ticks+".md");
                SetupService.SafeDirectory(Path.GetDirectoryName(backup)!);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.WriteAllBytes(backup,current);
                try{file.Position=0;file.Write(preview.Output);file.SetLength(preview.Output.Length);file.Flush(true);}
                catch{try{file.Position=0;file.Write(current);file.SetLength(current.Length);file.Flush(true);}catch{}throw;}
            }
        }
        return new(preview.Path,preview.Action,preview.MergeRequired,preview.ValidNotes,preview.Excerpts,preview.Window.Id);
    }
}

public sealed class WeeklySettings
{
    public bool AutoEnabled{get;set;}
    public int Weekday{get;set;}=4;
    public int Hour{get;set;}=12;
    public int Minute{get;set;}
    public string Folder{get;set;}="周报";
    public string LastCompletedWeek{get;set;}="";
    public string LastOutput{get;set;}="";
    public string LastMessage{get;set;}="";
    public static string FileFor(CardConfig c)=>Path.Combine(c.UserDataPath,"weekly",DiarySession.Hash(c.VaultPath.ToUpperInvariant())[..24]+".json");
    public static WeeklySettings Load(CardConfig c)
    {
        string file=FileFor(c);if(!File.Exists(file))return new();
        try{return JsonSerializer.Deserialize<WeeklySettings>(DiarySession.ReadText(file),CardConfig.Json)??throw new IOException("周报设置为空，请检查："+file);}
        catch(JsonException e){throw new IOException("周报设置损坏，请保留文件后修复："+file,e);}
    }
    public void Save(CardConfig c){ValidateTime();SetupService.SafeDirectory(Path.GetDirectoryName(FileFor(c))!);DiarySession.AtomicText(FileFor(c),JsonSerializer.Serialize(this,CardConfig.Json));}
    public void ValidateTime()
    {
        if(Weekday<0||Weekday>6||Hour<0||Hour>24||Minute<0||Minute>59||(Hour==24&&Minute!=0))throw new IOException("生成时间需为周一至周日的 00:00–24:00；24 点仅允许 24:00。");
    }
    public string ScheduleLabel=>$"每{new[]{"周一","周二","周三","周四","周五","周六","周日"}[Weekday]} {Hour:00}:{Minute:00}（北京时间）";
    public bool Due(DateTimeOffset now)=>AutoEnabled&&LastCompletedWeek!=WeeklyWindow.Latest(now,this).Id;
}
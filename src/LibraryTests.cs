using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DailyDiaryCard;
public static class LibraryTests
{
    private static CardConfig Fixture(string install)
    {
        Directory.CreateDirectory(install);
        return SetupService.Complete(SetupService.PlanStandalone(Path.Combine(install,"diary-card.json"),Path.Combine(install,"Diary"),"",true,"日记","YYYY/MM/YYYY-MM-DD"));
    }
    private static string Diary(CardConfig c,string day,string body,string? journal=null)
    {
        string file=c.PathFor(DateOnly.ParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture));Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file,"---\ntype: diary\ndate: \""+day+"\"\njournal_date: \""+(journal??day)+"\"\n---\n# "+day+"\n\n"+body,DiarySession.Utf8);return file;
    }
    private static string Report(CardConfig c)
    {
        Diary(c,"2026-10-26","## 今日学习内容\n形成原文摘录");
        var p=WeeklyReport.Preview(c,WeeklyWindow.ForDate(new(2026,10,30),DateTimeOffset.Parse("2026-10-30T18:00:00+08:00",CultureInfo.InvariantCulture)));
        WeeklyReport.Save(p);return p.Path;
    }
    public static int Run(string root)
    {
        root=Path.GetFullPath(root);Directory.CreateDirectory(root);int index=0,failed=0;var results=new List<object>();
        CardConfig F()=>Fixture(Path.Combine(root,(++index).ToString("00")));
        void Check(bool ok,string reason="断言失败"){if(!ok)throw new Exception(reason);}
        void Reject(Action action)
        {
            bool rejected=false;try{action();}catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){rejected=true;}
            Check(rejected,"应阻止此操作，不能静默继续。");
        }
        void Test(string name,Action action){try{action();results.Add(new{name,passed=true});}catch(Exception e){failed++;results.Add(new{name,passed=false,error=e.ToString()});}}
        Test("资料库日期优先归属日期，再取日期文件名和属性",()=>{
            Check(LibraryService.DateFor("2026-10-26.md","---\njournal_date: \"2026-10-24\"\ndate: \"2026-10-27\"\n---\n正文")==new DateOnly(2026,10,24));
            Check(LibraryService.DateFor("2026-10-25.md","---\njournal_date: \"2026-02-30\"\ndate: \"2026-10-27\"\n---\n正文")==new DateOnly(2026,10,25));
            Check(LibraryService.DateFor("custom.md","---\ndate: \"2026-10-27\"\n---\n正文")==new DateOnly(2026,10,27));
        });
        Test("独立资料库按日期倒序列出历史日记且不写原文",()=>{
            var c=F();string a=Diary(c,"2026-10-24","## 今日学习内容\n周六记录"),b=Diary(c,"2026-10-26","## 今日学习内容\n周一记录");
            var before=new[]{a,b}.ToDictionary(p=>p,File.ReadAllBytes);var rows=LibraryService.List(c,LibraryKind.Diary,"",out int unavailable);
            Check(unavailable==0&&rows.Count==2&&rows[0].Date==new DateOnly(2026,10,26)&&rows[1].Date==new DateOnly(2026,10,24));
            Check(before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)));
        });
        Test("中文正文和日期可搜索，不匹配记录不混入",()=>{
            var c=F();Diary(c,"2026-10-24","## 学习内容\n中文项目分析结论");Diary(c,"2026-10-26","## 学习内容\n另一个任务");
            var chinese=LibraryService.List(c,LibraryKind.Diary,"项目分析",out _);var date=LibraryService.List(c,LibraryKind.Diary,"2026-10-26",out _);
            Check(chinese.Count==1&&chinese[0].Date==new DateOnly(2026,10,24)&&date.Count==1&&date[0].Date==new DateOnly(2026,10,26));
        });
        Test("日记列表排除周报与当前模板，模板和周报可分别筛选",()=>{
            var c=F();string report=Report(c);
            string misplaced=Path.Combine(c.DiaryFolder,"错误目录周报.md");File.Copy(report,misplaced);
            var diary=LibraryService.List(c,LibraryKind.Diary,"",out _);var weekly=LibraryService.List(c,LibraryKind.Weekly,"",out _);var template=LibraryService.List(c,LibraryKind.Template,"",out _);
            Check(diary.Count==1&&weekly.Count==1&&weekly[0].Path==report&&template.Count==1&&template[0].Path==c.TemplatePath);
        });
        Test("自定义周报目录沿用当前库的设置",()=>{
            var c=F();new WeeklySettings{Folder="整理结果"}.Save(c);Diary(c,"2026-10-26","## 学习内容\n自定义记录");
            var p=WeeklyReport.Preview(c,WeeklyWindow.ForDate(new(2026,10,30),WeeklyTests.Now),"整理结果");WeeklyReport.Save(p);
            var rows=LibraryService.List(c,LibraryKind.Weekly,"",out _);Check(rows.Count==1&&rows[0].Path==p.Path);
        });
        Test("周报来源链接解析标题标签，重复来源去重",()=>{
            var c=F();string path=Diary(c,"2026-10-24","## 今日学习内容\n有意义的记录");string relative=Path.GetRelativePath(c.VaultPath,path).Replace('\\','/')[..^3];
            var links=LibraryService.Links(c,"[["+relative+"#今日学习内容|周六学习]]；[["+relative+"#今日学习内容|重复来源]]");
            Check(links.Count==1&&links[0].Path==path&&links[0].Heading=="今日学习内容"&&links[0].Label=="周六学习");
        });
        Test("来源链接拒绝库外路径网址及不存在文件，不执行外部动作",()=>{
            var c=F();string outside=Path.Combine(Path.GetDirectoryName(c.VaultPath)!,"outside.md");File.WriteAllText(outside,"外部文档");
            string source="[[../outside|越界]] [[https://example.com/file|网页]] [["+outside+"|绝对路径]] [[missing|不存在]]";
            Check(LibraryService.Links(c,source).Count==0);Reject(()=>LibraryService.CheckPath(c,outside));
        });
        Test("不同章节的同一来源分别保留，日期路径可包含中文",()=>{
            var c=F();string path=Diary(c,"2026-10-25","## 今日学习内容\n甲\n## 卡在哪里\n乙");string relative=Path.GetRelativePath(c.VaultPath,path).Replace('\\','/');
            var links=LibraryService.Links(c,"[["+relative+"#今日学习内容|学习]] [["+relative+"#卡在哪里|风险]]");
            Check(links.Count==2&&links.All(x=>x.Path==path)&&links.Select(x=>x.Heading).Distinct().Count()==2);
        });
        Test("内置模板文档保存合法新栏目并保留原模板内容",()=>{
            var c=F();string before=DiarySession.ReadText(c.TemplatePath);var d=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);
            d.Edit(d.Text+"\n## 新增复盘栏目\n中文提示\n");d.Save();
            Check(!d.Dirty&&DiarySession.ReadText(c.TemplatePath).StartsWith(before,StringComparison.Ordinal)&&TemplateReconciler.Parse(DiarySession.ReadText(c.TemplatePath)).Any(h=>h.Title=="新增复盘栏目"));
            Check(Directory.GetFiles(Path.Combine(c.UserDataPath,"library","backups"),"*.md").Length==1);
        });
        Test("无二级标题或重复标题的模板不能覆盖原件，草稿保留",()=>{
            foreach(string invalid in new[]{"# 只有标题\n正文","## 重复\n甲\n## 重复\n乙"})
            {
                var c=F();var before=File.ReadAllBytes(c.TemplatePath);var d=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);d.Edit(invalid);Reject(d.Save);
                Check(d.Dirty&&d.Text==invalid&&File.Exists(d.RecoveryPath)&&File.ReadAllBytes(c.TemplatePath).SequenceEqual(before));
            }
        });
        Test("周报手工补充保存后自动区内容逐字节保留",()=>{
            var c=F();string file=Report(c);var before=WeeklyReport.Markers(File.ReadAllBytes(file))!;var d=new LibraryDocument(c,file,LibraryKind.Weekly);
            d.Edit(d.Text+"\n我的手工反馈 😀\n");d.Save();var after=WeeklyReport.Markers(File.ReadAllBytes(file))!;
            Check(before.Prefix.SequenceEqual(after.Prefix)&&before.Body.SequenceEqual(after.Body)&&DiarySession.ReadText(file).Contains("我的手工反馈 😀"));
        });
        Test("周报已有自动区的结束标记不能误删",()=>{
            var c=F();string file=Report(c);var before=File.ReadAllBytes(file);var d=new LibraryDocument(c,file,LibraryKind.Weekly);
            d.Edit(d.Text.Replace(WeeklyReport.End,""));Reject(d.Save);
            Check(d.Dirty&&File.Exists(d.RecoveryPath)&&File.ReadAllBytes(file).SequenceEqual(before));
        });
        Test("外部同长度改动仍被检测，不覆盖双方内容",()=>{
            var c=F();File.AppendAllText(c.TemplatePath,"\n## 验证栏目\n原文甲\n");var d=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);
            d.Edit(d.Text+"\n本地尚未落盘的输入\n");File.WriteAllText(c.TemplatePath,DiarySession.ReadText(c.TemplatePath).Replace("原文甲","原文乙"),DiarySession.Utf8);
            Reject(d.Save);Check(d.Dirty&&d.Text.Contains("本地尚未落盘的输入")&&DiarySession.ReadText(c.TemplatePath).Contains("原文乙")&&File.Exists(d.RecoveryPath));
        });
        Test("文件被独占时保存失败保留草稿，解除后重试成功",()=>{
            var c=F();var d=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);d.Edit(d.Text+"\n## 占用时新增\n需要保留的输入\n");
            using(var hold=new FileStream(c.TemplatePath,FileMode.Open,FileAccess.Read,FileShare.None)){Reject(d.Save);Check(d.Dirty&&d.Text.Contains("需要保留的输入")&&File.Exists(d.RecoveryPath));}
            d.Save();Check(!d.Dirty&&DiarySession.ReadText(c.TemplatePath).Contains("需要保留的输入"));
        });
        Test("未保存文档草稿重新打开可以恢复并继续保存",()=>{
            var c=F();var d=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);d.Edit(d.Text+"\n## 恢复的栏目\n恢复的中文内容\n");d.PersistDraft();
            var resumed=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);Check(resumed.Restored&&resumed.Dirty&&resumed.Text.Contains("恢复的中文内容"));
            resumed.Save();Check(DiarySession.ReadText(c.TemplatePath).Contains("恢复的中文内容")&&!new LibraryDocument(c,c.TemplatePath,LibraryKind.Template).Restored);
        });
        Test("恢复草稿不抹去磁盘后来修改，保存仍检查原始基线",()=>{
            var c=F();var d=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);d.Edit(d.Text+"\n## 草稿栏目\n草稿内容\n");d.PersistDraft();
            File.AppendAllText(c.TemplatePath,"\n## 他处新增栏目\n外部内容\n");var resumed=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);
            Check(resumed.Restored&&resumed.Text.Contains("草稿内容"));Reject(resumed.Save);Check(DiarySession.ReadText(c.TemplatePath).Contains("外部内容"));
        });
        Test("保留草稿并重新读取把本地输入另存，载入最新磁盘",()=>{
            var c=F();var d=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);d.Edit(d.Text+"\n## 本地独有\n先保留本地内容\n");
            File.AppendAllText(c.TemplatePath,"\n## 外部独有\n最新磁盘内容\n");string archive=d.ReloadKeepingDraft();
            Check(File.Exists(archive)&&DiarySession.ReadText(archive).Contains("先保留本地内容")&&d.Text.Contains("最新磁盘内容")&&!d.Dirty);
        });
        Test("损坏恢复文件保留原文并给出读取提示",()=>{
            var c=F();var d=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);Directory.CreateDirectory(Path.GetDirectoryName(d.RecoveryPath)!);const string broken="{ broken recovery";File.WriteAllText(d.RecoveryPath,broken);
            var resumed=new LibraryDocument(c,c.TemplatePath,LibraryKind.Template);Check(resumed.RecoveryNotice!=null&&!resumed.Restored&&!resumed.Dirty&&File.ReadAllText(d.RecoveryPath)==broken);
        });
        Test("导出Markdown副本不改变原件，拒绝以原件路径导出",()=>{
            var c=F();string original=Diary(c,"2026-10-26","## 学习内容\n原件中文正文");var before=File.ReadAllBytes(original);string text=DiarySession.ReadText(original)+"\n导出的草稿补充\n";
            string destination=Path.Combine(root,"export-"+index+".md");LibraryService.Export(destination,text,original);
            Check(DiarySession.ReadText(destination)==text&&File.ReadAllBytes(original).SequenceEqual(before));Reject(()=>LibraryService.Export(original,"不能覆盖",original));Reject(()=>LibraryService.Export(original.ToUpperInvariant(),"不能覆盖",original));
            Check(File.ReadAllBytes(original).SequenceEqual(before));
        });
        DiarySession.AtomicText(Path.Combine(root,"library-result.json"),JsonSerializer.Serialize(new{passed=results.Count-failed,failed,results},CardConfig.Json));
        return failed==0?0:1;
    }
}
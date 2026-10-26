using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DailyDiaryCard;
public static class WeeklyTests
{
    public static CardConfig Fixture(string root)
    {
        Directory.CreateDirectory(root);
        return SetupService.Complete(SetupService.Plan(Path.Combine(root,"diary-card.json"),true,"","",true,"日记","YYYY/MM/YYYY-MM-DD"));
    }
    public static string Note(CardConfig c,string date,string body,string? journal=null,string? name=null)
    {
        string file=name==null?c.PathFor(DateOnly.ParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture)):Path.Combine(c.DiaryFolder,name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file,"---\ntype: diary\njournal_date: \""+(journal??date)+"\"\ndate: \""+date+"\"\n---\n"+body,DiarySession.Utf8);return file;
    }
    public static DateTimeOffset Now=>DateTimeOffset.Parse("2026-10-30T12:00:00+08:00",CultureInfo.InvariantCulture);
    public static WeeklyPreview Preview(CardConfig c)=>WeeklyReport.Preview(c,WeeklyWindow.ForDate(new(2026,10,30),Now));
    public static int Run(string root,string? sourceVault=null)
    {
        Directory.CreateDirectory(root);int n=0,failed=0;var results=new List<object>();
        CardConfig F()=>Fixture(Path.Combine(root,(++n).ToString("00")));
        void Check(bool ok,string reason="断言失败"){if(!ok)throw new Exception(reason);}
        void Reject(Action action){bool rejected=false;try{action();}catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){rejected=true;}Check(rejected);}
        void Test(string label,Action action){try{action();results.Add(new{name=label,passed=true});}catch(Exception e){failed++;results.Add(new{name=label,passed=false,error=e.ToString()});}}
        Test("默认周五12点前后及周一补跑选择最近一期",()=>{Check(WeeklyWindow.Latest(Now.AddSeconds(-1)).Id=="2026-W43");Check(WeeklyWindow.Latest(Now).Id=="2026-W44");Check(WeeklyWindow.Latest(DateTimeOffset.Parse("2026-11-02T09:00:00+08:00")).Id=="2026-W44");});
        Test("周期周六至下周五及ISO跨年归档",()=>{var w=WeeklyWindow.ForDate(new(2027,1,1),Now);Check(w.Start=="2026-12-26"&&w.End=="2027-01-01"&&w.Id=="2026-W53");Check(WeeklyWindow.ForDate(new(2026,10,24),Now).End=="2026-10-30");});
        Test("任意星期零点和24点边界正确，触发前不提前",()=>{
            for(int day=0;day<7;day++)foreach(int hour in new[]{0,24})
            {
                var settings=new WeeklySettings{Weekday=day,Hour=hour,Minute=0};
                var anchor=new DateOnly(2026,10,26).AddDays(day);
                var trigger=new DateTimeOffset(anchor.ToDateTime(TimeOnly.MinValue),WeeklyWindow.Beijing).AddHours(hour);
                var before=WeeklyWindow.Latest(trigger.AddSeconds(-1),settings);var at=WeeklyWindow.Latest(trigger,settings);
                Check(at.Friday.DayNumber-before.Friday.DayNumber==7,$"星期{day} {hour}:00");
                var expectedFriday=anchor.AddDays(-(((int)anchor.DayOfWeek-5+7)%7));
                Check(at.Friday==expectedFriday);
            }
        });
        Test("23点59分及非北京时间输入按同一时刻处理",()=>{var s=new WeeklySettings{Weekday=6,Hour=23,Minute=59};var t=DateTimeOffset.Parse("2026-11-01T23:59:00+08:00");Check(WeeklyWindow.Latest(t,s).Id=="2026-W44"&&WeeklyWindow.Latest(t.ToOffset(TimeSpan.FromHours(-5)),s).Id=="2026-W44");});
        Test("24点只接受00分，非法星期小时分钟拒绝",()=>{foreach(var s in new[]{new WeeklySettings{Hour=24,Minute=1},new WeeklySettings{Weekday=7},new WeeklySettings{Hour=-1},new WeeklySettings{Minute=60}})Reject(s.ValidateTime);});
        Test("周六周日周五纳入，前后周期排除，原日记不写入",()=>{
            var c=F();var files=new[]{"2026-10-23","2026-10-24","2026-10-25","2026-10-30","2026-10-31"}.Select(d=>Note(c,d,"## 今天推进了什么\n完成记录-"+d)).ToArray();var before=files.Select(File.ReadAllBytes).ToArray();var p=Preview(c);
            Check(p.ValidNotes==3&&p.Markdown.Contains("完成记录-2026-10-24")&&p.Markdown.Contains("完成记录-2026-10-25")&&!p.Markdown.Contains("完成记录-2026-10-23")&&!p.Markdown.Contains("完成记录-2026-10-31"));WeeklyReport.Save(p);
            Check(files.Select((file,i)=>File.ReadAllBytes(file).SequenceEqual(before[i])).All(x=>x));
        });
        Test("空的真实默认模板不作为成果，预览不创建周报目录",()=>{var c=F();Directory.CreateDirectory(Path.GetDirectoryName(c.PathFor(new(2026,10,26)))!);File.WriteAllText(c.PathFor(new(2026,10,26)),DefaultAssets.Template.Replace("{{date:YYYY-MM-DD}}","2026-10-26"));var p=Preview(c);Check(p.ValidNotes==0&&p.EmptyNotes==1&&p.Markdown.Contains("本周未发现有效记录")&&!Directory.Exists(p.OutputRoot));});
        Test("归属日期优先journal_date，其次完整文件名，再次date",()=>{
            var c=F();Note(c,"2026-10-26","## 今天推进了什么\n旧周内容","2026-10-20");
            string file=Note(c,"2026-10-25","## 今天推进了什么\n兜底内容","invalid");Check(Preview(c).ValidNotes==1);
            File.WriteAllText(file,File.ReadAllText(file).Replace("journal_date: \"invalid\"","journal_date: \"2026-02-30\""));Check(Preview(c).ValidNotes==1);
            Note(c,"2026-10-26","## 今天推进了什么\n属性日期兜底","invalid","custom.md");Check(Preview(c).ValidNotes==2);
        });
        Test("完成待办和规划即使勾选仍保留各自状态",()=>{
            var c=F();Note(c,"2026-10-26","## 每日任务完成情况\n- [x] 交付报告\n- [ ] 整理记录\n## 第二日任务规划\n### 下一阶段\n- [x] 明天检查\n- [ ] 核对参数\n");var p=Preview(c);
            var work=p.Markdown.Split("## 工作成果与证据")[1].Split("## 有效做法")[0];var plan=p.Markdown.Split("## 后续计划")[1].Split("## 参考笔记")[0];
            Check(work.Contains("交付报告")&&!work.Contains("整理记录")&&!work.Contains("明天检查")&&plan.Contains("未完成待办")&&plan.Contains("计划事项（原文已勾选")&&plan.Contains("明天检查"));
        });
        Test("可能原因保留待确认，未知栏目保留其他记录",()=>{var c=F();Note(c,"2026-10-26","## 卡在哪里\n- 可能原因（还未确认也可以）：可能权限不足\n## 自己的观察\n实际观察甲\n");var p=Preview(c);Check(p.Markdown.Contains("可能原因（待确认）")&&p.Markdown.Contains("## 其他记录")&&p.Markdown.Contains("实际观察甲"));});
        Test("同类原文去重且保留多日来源链接",()=>{var c=F();Note(c,"2026-10-24","## 今日学习内容\n掌握验证方法");Note(c,"2026-10-25","## 今日学习内容\n掌握验证方法");var p=Preview(c);Check(p.Markdown.Split("掌握验证方法").Length==2&&p.Markdown.Contains("2026-10-24#今日学习内容")&&p.Markdown.Contains("2026-10-25#今日学习内容"));});
        Test("代码围栏注释来源和空表格排除，实写表格提取列名",()=>{var c=F();Note(c,"2026-10-26","## 学习内容\n~~~\n不应成为成果\n~~~\n<!-- 来源注释 -->\n| 主题 | 产出 |\n| --- | --- |\n| | |\n| 方法 | 对比笔记 |\n");var p=Preview(c);Check(p.Excerpts==1&&p.Markdown.Contains("主题：方法；产出：对比笔记")&&!p.Markdown.Contains("不应成为成果")&&!p.Markdown.Contains("来源注释"));});
        Test("非diary与周报不被重复汇总",()=>{var c=F();string file=Note(c,"2026-10-26","## 学习内容\n无效记录");File.WriteAllText(file,File.ReadAllText(file).Replace("type: diary","type: weekly-report"));Check(Preview(c).ValidNotes==0);});
        Test("BOM和CRLF手工区更新前后逐字节保留",()=>{
            var c=F();Note(c,"2026-10-26","## 今天推进了什么\n第一段实写");var p=Preview(c);WeeklyReport.Save(p);
            string text="\uFEFF"+File.ReadAllText(p.Path).Replace("\n","\r\n").Replace("## 手工补充","## 手工补充\r\n\r\n手写反馈 😀");
            File.WriteAllBytes(p.Path,new UTF8Encoding(false).GetBytes(text));var before=WeeklyReport.Markers(File.ReadAllBytes(p.Path))!;
            Note(c,"2026-10-27","## 今日学习内容\n新的方法");WeeklyReport.Save(Preview(c));var after=WeeklyReport.Markers(File.ReadAllBytes(p.Path))!;
            Check(before.Prefix.SequenceEqual(after.Prefix)&&before.Suffix.SequenceEqual(after.Suffix)&&File.ReadAllText(p.Path).Contains("新的方法"));
        });
        Test("有效输入不变不重写，修改时间保持原值",()=>{var c=F();Note(c,"2026-10-26","## 今天推进了什么\n保持原文");var p=Preview(c);WeeklyReport.Save(p);File.SetLastWriteTimeUtc(p.Path,new DateTime(2001,1,1,0,0,0,DateTimeKind.Utc));var next=Preview(c);Check(next.Action=="unchanged");WeeklyReport.Save(next);Check(File.GetLastWriteTimeUtc(p.Path).Year==2001);});
        Test("标记丢失重复乱序另存待合并，不覆盖手写原件",()=>{
            foreach(string original in new[]{"手写原件",WeeklyReport.Start+WeeklyReport.End+WeeklyReport.End,WeeklyReport.End+WeeklyReport.Start})
            {var c=F();Note(c,"2026-10-26","## 学习内容\n有效学习");var p=Preview(c);Directory.CreateDirectory(Path.GetDirectoryName(p.Path)!);File.WriteAllText(p.Path,original);var merge=Preview(c);Check(merge.MergeRequired&&merge.Path!=p.Path);WeeklyReport.Save(merge);Check(File.ReadAllText(p.Path)==original);}
        });
        Test("已有待合并文件受保护，输入相同不新增副本",()=>{var c=F();var p=Preview(c);Directory.CreateDirectory(Path.GetDirectoryName(p.Path)!);File.WriteAllText(p.Path,"手写");var merge=Preview(c);WeeklyReport.Save(merge);var same=Preview(c);Check(same.Action=="unchanged"&&same.Path==merge.Path);});
        Test("预览后外部改动周报会停止写入",()=>{var c=F();var p=Preview(c);WeeklyReport.Save(p);Note(c,"2026-10-26","## 学习内容\n新增一条");var stale=Preview(c);File.AppendAllText(p.Path,"\n他处手写");Reject(()=>WeeklyReport.Save(stale));Check(File.ReadAllText(p.Path).Contains("他处手写"));});
        Test("预览后他处先创建目标文件不会被覆盖",()=>{var c=F();var p=Preview(c);Directory.CreateDirectory(Path.GetDirectoryName(p.Path)!);File.WriteAllText(p.Path,"别人创建的内容");Reject(()=>WeeklyReport.Save(p));Check(File.ReadAllText(p.Path)=="别人创建的内容");});
        Test("保存失败时预览内容保留并可重试",()=>{var c=F();Note(c,"2026-10-26","## 学习内容\n应保留内容");var p=Preview(c);WeeklyReport.Save(p);Note(c,"2026-10-27","## 学习内容\n新增内容");var next=Preview(c);using(var hold=new FileStream(p.Path,FileMode.Open,FileAccess.Read,FileShare.None)){Reject(()=>WeeklyReport.Save(next));Check(next.Markdown.Contains("新增内容"));}WeeklyReport.Save(next);Check(File.ReadAllText(p.Path).Contains("新增内容"));});
        Test("输出只能在当前库独立目录，不能覆盖输入目录",()=>{var c=F();Reject(()=>WeeklyReport.OutputRoot(c,".."));Reject(()=>WeeklyReport.OutputRoot(c,"."));Reject(()=>WeeklyReport.OutputRoot(c,"日记"));Check(WeeklyReport.OutputRoot(c,"自定义/工作汇总").StartsWith(c.VaultPath));});
        Test("自定义日记目录和周报目录生效",()=>{var c=F();c.DiaryFolder=Path.Combine(c.VaultPath,"记录");Note(c,"2026-10-26","## 学习内容\n自定义路径记录");var p=WeeklyReport.Preview(c,WeeklyWindow.Latest(Now),"汇总");Check(p.ValidNotes==1&&p.Path.Contains(Path.Combine("汇总","2026")));});
        Test("自动设置每库独立且重新加载时间不丢失",()=>{var a=F();var b=F();b.UserDataPath=a.UserDataPath;var s=new WeeklySettings{AutoEnabled=true,Weekday=6,Hour=24,Minute=0};s.Save(a);var loaded=WeeklySettings.Load(a);Check(loaded.AutoEnabled&&loaded.Weekday==6&&loaded.Hour==24&&!WeeklySettings.Load(b).AutoEnabled);});
        Test("自动生成默认关闭，完成后不重复，下一期再触发",()=>{var c=F();var s=WeeklySettings.Load(c);Check(!s.Due(Now));s.AutoEnabled=true;Check(s.Due(Now));s.LastCompletedWeek=WeeklyWindow.Latest(Now,s).Id;Check(!s.Due(Now.AddMinutes(1))&&s.Due(Now.AddDays(7)));});
        Test("周报设置损坏不静默重置并保留原文件",()=>{var c=F();string file=WeeklySettings.FileFor(c);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,"{ broken");Reject(()=>WeeklySettings.Load(c));Check(File.ReadAllText(file)=="{ broken");});
        if(sourceVault!=null)
        {
            void CopyTemplates(CardConfig c)
            {
                foreach(string file in Directory.EnumerateFiles(Path.Combine(sourceVault,"模板"),"*.md",SearchOption.AllDirectories))
                {
                    string target=Path.Combine(c.VaultPath,"模板",Path.GetRelativePath(Path.Combine(sourceVault,"模板"),file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,true);
                }
            }
            Test("第一版真实模板全部空白时不计作周报成果",()=>{
                var c=F();CopyTemplates(c);int index=0;
                foreach(string file in Directory.EnumerateFiles(Path.Combine(c.VaultPath,"模板"),"*.md",SearchOption.AllDirectories))
                {
                    string text=DiarySession.ReadText(file).Replace("{{date:YYYY-MM-DD}}","2026-10-26");
                    Directory.CreateDirectory(c.DiaryFolder);File.WriteAllText(Path.Combine(c.DiaryFolder,"empty-"+(++index)+".md"),text,DiarySession.Utf8);
                }
                Check(Preview(c).ValidNotes==0,"真实模板提示被误计为成果");
            });
            Test("真实日记及旧周报副本聚合，源库与副本日报均逐字节保留",()=>{
                var c=F();CopyTemplates(c);string source=Path.Combine(sourceVault,"日记");
                var original=Directory.GetFiles(source,"*.md",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
                foreach(var pair in original){string file=Path.Combine(c.DiaryFolder,Path.GetRelativePath(source,pair.Key));Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllBytes(file,pair.Value);}
                var copies=Directory.GetFiles(c.DiaryFolder,"*.md",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
                var window=WeeklyWindow.Latest(DateTimeOffset.Parse("2026-10-23T12:00:00+08:00"));
                string old=Path.Combine(sourceVault,"周报","2026","2026-W43-工作周报.md");
                WeeklyReport.AutoMarkers? before=null;
                if(File.Exists(old)){string dest=Path.Combine(c.VaultPath,"周报","2026",Path.GetFileName(old));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(old,dest);before=WeeklyReport.Markers(File.ReadAllBytes(old));}
                var preview=WeeklyReport.Preview(c,window);WeeklyReport.Save(preview);Check(File.Exists(preview.Path));
                Check(original.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value))&&copies.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)));
                if(before!=null){var after=WeeklyReport.Markers(File.ReadAllBytes(preview.Path))!;Check(before.Prefix.SequenceEqual(after.Prefix)&&before.Suffix.SequenceEqual(after.Suffix));}
            });
        }
        DiarySession.AtomicText(Path.Combine(root,"weekly-result.json"),JsonSerializer.Serialize(new{passed=results.Count-failed,failed,results},CardConfig.Json));return failed==0?0:1;
    }
}
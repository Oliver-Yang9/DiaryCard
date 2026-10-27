using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DailyDiaryCard;

public static class StandaloneTests
{
    private sealed class NotAvailableException(string message):Exception(message);
    public static int Run(string root)
    {
        root=Path.GetFullPath(root);Directory.CreateDirectory(root);
        int sequence=0,failed=0,skipped=0;
        var results=new List<object>();
        string Fixture()
        {
            string folder=Path.GetFullPath(Path.Combine(root,(++sequence).ToString("00")));
            if(!SetupService.IsUnder(root,folder)||folder==root)throw new IOException("测试路径校验失败。");
            Directory.CreateDirectory(folder);return folder;
        }
        void Check(bool ok,string reason="断言失败"){if(!ok)throw new Exception(reason);}
        void Reject(Action action)
        {
            bool rejected=false;
            try{action();}catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException){rejected=true;}
            Check(rejected,"应当拒绝此次操作，不能静默继续。");
        }
        void Test(string name,Action action)
        {
            try{action();results.Add(new{name,passed=true,skipped=false});}
            catch(NotAvailableException e){skipped++;results.Add(new{name,passed=false,skipped=true,reason=e.Message});}
            catch(Exception e){failed++;results.Add(new{name,passed=false,skipped=false,error=e.ToString()});}
        }
        string Config(string install)=>Path.Combine(install,"diary-card.json");
        string Library(string install)=>Path.Combine(install,"我的日记");
        VaultPlan Plan(string install,string? library=null,string template="",bool useDefault=true,string folder="日记",string format="YYYY/MM/YYYY-MM-DD")
            =>SetupService.PlanStandalone(Config(install),library??Library(install),template,useDefault,folder,format);
        CardConfig New(string install)=>SetupService.Complete(Plan(install));
        string Marker(CardConfig c)=>Path.Combine(c.VaultPath,"diarycard-library.json");
        Dictionary<string,byte[]> Snapshot(string directory)
            =>Directory.GetFiles(directory,"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes,StringComparer.OrdinalIgnoreCase);
        void EqualFiles(Dictionary<string,byte[]> before)
            =>Check(before.All(p=>File.Exists(p.Key)&&File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"原文件字节发生变化。");
        void WriteManifest(string library,string template="模板/复盘.md",string folder="日记",string format="YYYY/MM/YYYY-MM-DD",int schema=1,string kind="DiaryCard")
        {
            Directory.CreateDirectory(library);
            File.WriteAllText(Path.Combine(library,"diarycard-library.json"),JsonSerializer.Serialize(new{Kind=kind,SchemaVersion=schema,TemplatePath=template,DiaryFolder=folder,DateFormat=format,custom="保留属性"},CardConfig.Json),DiarySession.Utf8);
        }
        void WriteTemplate(string library,string relative="模板/复盘.md")
        {
            string path=Path.Combine(library,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,DefaultAssets.Template,DiarySession.Utf8);
        }
        string Note(CardConfig c,string date,string body)
        {
            string path=c.PathFor(DateOnly.ParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,"---\ntype: diary\njournal_date: \""+date+"\"\ndate: \""+date+"\"\n---\n"+body,DiarySession.Utf8);
            return path;
        }

        Test("独立模式预览不创建资料夹配置或默认数据",()=>{
            string install=Fixture();var plan=Plan(install);
            Check(plan.CreateVault&&plan.Config.StorageMode=="standalone"&&plan.Config.IsStandalone&&plan.Config.SetupVersion==3);
            Check(!Directory.Exists(Library(install))&&!Directory.Exists(plan.Config.UserDataPath)&&!File.Exists(Config(install)));
            Check(TemplateReconciler.Parse(plan.TemplateText).Count==TemplateReconciler.Parse(DefaultAssets.Template).Count);
        });
        Test("独立模式创建自己的manifest和本地模板，不创建.obsidian",()=>{
            string install=Fixture();var c=New(install);
            Check(c.IsStandalone&&c.StorageMode=="standalone"&&c.SetupVersion==3&&File.Exists(Marker(c)));
            Check(!Directory.Exists(Path.Combine(c.VaultPath,".obsidian"))&&SetupService.IsUnder(c.VaultPath,c.TemplatePath)&&File.Exists(c.TemplatePath));
            Check(DiarySession.ReadText(c.TemplatePath)==DefaultAssets.Template&&!File.Exists(c.PathFor(new(2026,10,26))));
            using var manifest=JsonDocument.Parse(DiarySession.ReadText(Marker(c)));
            Check(manifest.RootElement.GetProperty("Kind").GetString()=="DiaryCard"&&manifest.RootElement.GetProperty("SchemaVersion").GetInt32()==1);
        });
        Test("模式和便携相对路径保存后重新加载仍正确",()=>{
            var c=New(Fixture());var raw=JsonSerializer.Deserialize<CardConfig>(DiarySession.ReadText(c.ConfigPath),CardConfig.Json)!;
            Check(raw.StorageMode=="standalone"&&!Path.IsPathRooted(raw.VaultPath)&&!Path.IsPathRooted(raw.TemplatePath)&&!Path.IsPathRooted(raw.DiaryFolder));
            var loaded=CardConfig.Load(c.ConfigPath);
            Check(loaded.IsStandalone&&loaded.VaultPath==c.VaultPath&&loaded.TemplatePath==c.TemplatePath&&loaded.DiaryFolder==c.DiaryFolder);
        });
        Test("自选空目录可创建，非空陌生目录拒绝且原文件保留",()=>{
            string install=Fixture(),library=Path.Combine(install,"自选位置");Directory.CreateDirectory(library);
            var c=SetupService.Complete(Plan(install,library));Check(c.VaultPath==library&&c.IsStandalone);
            string other=Path.Combine(install,"其他非空目录");Directory.CreateDirectory(other);string note=Path.Combine(other,"已有文件.md");File.WriteAllText(note,"原始内容");
            Reject(()=>SetupService.PlanStandalone(Path.Combine(install,"second.json"),other,"",true,"日记","YYYY/MM/YYYY-MM-DD"));
            Check(File.ReadAllText(note)=="原始内容"&&!File.Exists(Path.Combine(other,"diarycard-library.json")));
        });
        Test("已有独立资料夹载入manifest，原资料和未知属性不改写",()=>{
            string install=Fixture(),library=Library(install);WriteTemplate(library);WriteManifest(library);
            string existing=Path.Combine(library,"日记","2026-10-26.md");Directory.CreateDirectory(Path.GetDirectoryName(existing)!);File.WriteAllText(existing,"原有日记内容");
            var before=Snapshot(library);var settings=SetupService.InspectStandalone(library);
            Check(Path.GetFullPath(settings.Template,library)==Path.Combine(library,"模板","复盘.md")&&settings.Folder=="日记"&&settings.Format=="YYYY/MM/YYYY-MM-DD");
            var plan=SetupService.PlanStandalone(Config(install),library,"",false,"","");
            Check(!plan.CreateVault&&plan.Config.IsStandalone);var c=SetupService.Complete(plan);
            Check(c.TemplatePath==Path.Combine(library,"模板","复盘.md"));EqualFiles(before);
        });
        Test("缺省配置从独立manifest恢复，绝不读取损坏的.obsidian",()=>{
            var c=New(Fixture());string obsidian=Path.Combine(c.VaultPath,".obsidian");Directory.CreateDirectory(obsidian);
            string external=Path.Combine(obsidian,"daily-notes.json");const string broken="{ invalid Obsidian settings";File.WriteAllText(external,broken);
            string minimal=Path.Combine(Path.GetDirectoryName(c.ConfigPath)!,"minimal.json");
            File.WriteAllText(minimal,JsonSerializer.Serialize(new CardConfig{StorageMode="standalone",VaultPath=c.VaultPath,UserDataPath=c.UserDataPath},CardConfig.Json));
            var loaded=CardConfig.Load(minimal);
            Check(loaded.IsStandalone&&loaded.TemplatePath==c.TemplatePath&&loaded.DiaryFolder==c.DiaryFolder&&File.ReadAllText(external)==broken);
        });
        Test("旧配置缺少模式字段继续按Obsidian恢复，不修改库",()=>{
            string install=Fixture();string vault=SetupTests.MakeVault(install,"旧版库");
            File.WriteAllText(Config(install),JsonSerializer.Serialize(new{VaultPath=vault,TemplatePath="templates/custom.md",DiaryFolder="records",DateFormat="YYYY-MM-DD",UserDataPath=Path.Combine(install,"data"),SetupVersion=2},CardConfig.Json));
            var before=Snapshot(vault);var c=CardConfig.Load(Config(install));
            Check(c.StorageMode=="obsidian"&&!c.IsStandalone);EqualFiles(before);
        });
        Test("未知StorageMode拒绝，不降级成其他模式",()=>{
            var c=New(Fixture());var raw=JsonSerializer.Deserialize<CardConfig>(DiarySession.ReadText(c.ConfigPath),CardConfig.Json)!;raw.StorageMode="unexpected-mode";
            File.WriteAllText(c.ConfigPath,JsonSerializer.Serialize(raw,CardConfig.Json));var before=Snapshot(c.VaultPath);
            Reject(()=>CardConfig.Load(c.ConfigPath));EqualFiles(before);
        });
        Test("空损坏或非对象manifest拒绝，文件原文保留",()=>{
            foreach(string text in new[]{"null","{ broken","[]","\"not a manifest\""})
            {
                string install=Fixture(),library=Library(install);Directory.CreateDirectory(library);
                string marker=Path.Combine(library,"diarycard-library.json");File.WriteAllText(marker,text);
                Reject(()=>SetupService.InspectStandalone(library));
                Reject(()=>Plan(install,library));
                Check(File.ReadAllText(marker)==text&&!File.Exists(Config(install)));
            }
        });
        Test("manifest未知版本与错误Kind拒绝，不静默重建",()=>{
            foreach(var pair in new[]{(Schema:2,Kind:"DiaryCard"),(Schema:0,Kind:"DiaryCard"),(Schema:1,Kind:"OtherProgram")})
            {
                string install=Fixture(),library=Library(install);WriteTemplate(library);WriteManifest(library,schema:pair.Schema,kind:pair.Kind);var before=Snapshot(library);
                Reject(()=>SetupService.InspectStandalone(library));Reject(()=>Plan(install,library));EqualFiles(before);
            }
        });
        Test("manifest中的模板路径越界拒绝",()=>{
            string install=Fixture(),library=Library(install);WriteTemplate(library);File.WriteAllText(Path.Combine(install,"outside.md"),DefaultAssets.Template);
            foreach(string path in new[]{"../outside.md",Path.Combine(install,"outside.md")})
            {
                WriteManifest(library,template:path);var before=Snapshot(install);
                Reject(()=>SetupService.InspectStandalone(library));EqualFiles(before);
            }
        });
        Test("manifest中的日记目录越界拒绝",()=>{
            string install=Fixture(),library=Library(install);WriteTemplate(library);
            foreach(string path in new[]{"../外部日记",Path.Combine(install,"外部日记")})
            {
                WriteManifest(library,folder:path);var before=Snapshot(library);
                Reject(()=>SetupService.InspectStandalone(library));EqualFiles(before);
            }
        });
        Test("独立模式不接受缺失或无效的本地模板",()=>{
            string install=Fixture(),library=Library(install);WriteManifest(library);
            Reject(()=>SetupService.InspectStandalone(library));
            WriteTemplate(library);string template=Path.Combine(library,"模板","复盘.md");File.WriteAllText(template,"# 没有二级标题\n正文");
            Reject(()=>SetupService.InspectStandalone(library));
            File.WriteAllText(template,"## 重复\n甲\n## 重复\n乙");
            Reject(()=>SetupService.InspectStandalone(library));
        });
        Test("manifest文件链接不读取不覆盖，无法建立链接时明确记录未测",()=>{
            var c=New(Fixture());string marker=Marker(c),saved=marker+".saved";File.Move(marker,saved);
            bool created=false;
            try
            {
                try{File.CreateSymbolicLink(marker,saved);created=true;}
                catch(Exception e) when(e is UnauthorizedAccessException or IOException or PlatformNotSupportedException){throw new NotAvailableException("当前系统不允许创建文件符号链接，manifest文件链接保护未测："+e.Message);}
                var before=File.ReadAllBytes(saved);Reject(()=>SetupService.InspectStandalone(c.VaultPath));Check(File.ReadAllBytes(saved).SequenceEqual(before));
            }
            finally{if(created&&File.Exists(marker))File.Delete(marker);if(File.Exists(saved)&&!File.Exists(marker))File.Move(saved,marker);}
        });
        Test("导入外部模板生成库内副本且不修改原件",()=>{
            string install=Fixture(),external=Path.Combine(install,"外部模板.md");
            string text="---\ntype: diary\ndate: \"{{date:YYYY-MM-DD}}\"\n---\n## 工作\n原提示\n## 计划\n- [ ] \n";File.WriteAllText(external,text,DiarySession.Utf8);
            var before=File.ReadAllBytes(external);var c=SetupService.Complete(Plan(install,template:external,useDefault:false));
            Check(c.IsStandalone&&c.TemplatePath!=external&&SetupService.IsUnder(c.VaultPath,c.TemplatePath)&&File.ReadAllBytes(c.TemplatePath).SequenceEqual(before)&&File.ReadAllBytes(external).SequenceEqual(before));
            File.AppendAllText(external,"\n外部后续编辑");Check(DiarySession.ReadText(c.TemplatePath)==text);
        });
        Test("外部模板无效时拒绝且不创建库或改原模板",()=>{
            string install=Fixture(),external=Path.Combine(install,"无效模板.md");File.WriteAllText(external,"# 没有表单栏目\n");
            var before=File.ReadAllBytes(external);Reject(()=>Plan(install,template:external,useDefault:false));
            Check(!Directory.Exists(Library(install))&&!File.Exists(Config(install))&&File.ReadAllBytes(external).SequenceEqual(before));
        });
        Test("独立模式中文多行和复选框持续保存同一当天文件",()=>{
            var c=New(Fixture());var day=new DateOnly(2026,10,26);var s=new DiarySession(c,day);string first=s.Template[0].Title;
            s.Edit(first,s.Bodies[first]+"\n第一行中文\n第二行中文 😀\n- [x] 已完成事项\n- [ ] 仍需处理\n");s.Save();
            s.Edit(first,s.Bodies[first]+"继续追加\n");s.Save();
            var resumed=new DiarySession(CardConfig.Load(c.ConfigPath),day);
            Check(resumed.Bodies[first].Contains("第二行中文 😀")&&resumed.Bodies[first].Contains("- [x] 已完成事项")&&resumed.Bodies[first].Contains("- [ ] 仍需处理")&&resumed.Bodies[first].Contains("继续追加"));
            Check(Directory.GetFiles(c.DiaryFolder,"*.md",SearchOption.AllDirectories).Length==1&&!DiarySession.ReadText(s.Target).Contains("{{date:"));
        });
        Test("未落盘输入由独立库恢复草稿重启恢复",()=>{
            var c=New(Fixture());var day=new DateOnly(2026,10,26);var s=new DiarySession(c,day);string first=s.Template[0].Title;
            s.Edit(first,s.Bodies[first]+"\n未完成保存的输入\n");s.PersistDraft("独立模式恢复测试");
            var restored=new DiarySession(CardConfig.Load(c.ConfigPath),day);
            Check(restored.Restored&&restored.Dirty&&restored.Bodies[first].Contains("未完成保存的输入"));
            restored.Save();Check(DiarySession.ReadText(restored.Target).Contains("未完成保存的输入"));
        });
        Test("独立模式跨天新建或匹配当天文件，不把旧内容复制到新日",()=>{
            var c=New(Fixture());var old=new DiarySession(c,new(2026,10,26));string first=old.Template[0].Title;
            old.Edit(first,old.Bodies[first]+"\n只能属于前一天的内容\n");old.Save();var before=File.ReadAllBytes(old.Target);
            var next=new DiarySession(c,new(2026,10,27));next.EnsureDailyFile();
            Check(File.Exists(next.Target)&&!DiarySession.ReadText(next.Target).Contains("只能属于前一天的内容")&&File.ReadAllBytes(old.Target).SequenceEqual(before));
            File.AppendAllText(next.Target,"\n## 额外记录\n磁盘已有内容\n");
            var restored=new DiarySession(c,new(2026,10,27));Check(restored.LocalDocument().Contains("磁盘已有内容"));
        });
        Test("独立模式外部同栏冲突保留双方及恢复草稿",()=>{
            var c=New(Fixture());var day=new DateOnly(2026,10,26);var seed=new DiarySession(c,day);string first=seed.Template[0].Title;
            seed.Edit(first,seed.Bodies[first]+"\n最初内容\n");seed.Save();var local=new DiarySession(c,day);
            local.Edit(first,local.Bodies[first]+"\n卡片的新输入\n");
            string external=new MarkdownDoc(DiarySession.ReadText(local.Target)).Apply(new(){{first,seed.Bodies[first]+"\n他处编辑\n"}},seed.Template);
            File.WriteAllText(local.Target,external,DiarySession.Utf8);Reject(local.Save);
            Check(local.IsConflict&&local.Bodies[first].Contains("卡片的新输入")&&DiarySession.ReadText(local.Target).Contains("他处编辑")&&File.Exists(local.RecoveryPath));
        });
        Test("独立日记被占用时保存失败保留输入，解除后可以保存",()=>{
            var c=New(Fixture());var s=new DiarySession(c,new(2026,10,26));string first=s.Template[0].Title;s.EnsureDailyFile();s.Edit(first,s.Bodies[first]+"\n文件占用时的输入\n");
            using(var hold=new FileStream(s.Target,FileMode.Open,FileAccess.Read,FileShare.None)){Reject(s.Save);Check(s.Dirty&&s.Bodies[first].Contains("文件占用时的输入"));}
            s.Save();Check(DiarySession.ReadText(s.Target).Contains("文件占用时的输入"));
        });
        Test("独立库周报沿用周六到下周五范围并保护源日记",()=>{
            var c=New(Fixture());var dates=new[]{"2026-10-23","2026-10-24","2026-10-25","2026-10-30","2026-10-31"};
            var files=dates.Select(d=>Note(c,d,"## 今日学习内容\n独立记录-"+d)).ToArray();var before=files.ToDictionary(p=>p,File.ReadAllBytes);
            var p=WeeklyReport.Preview(c,WeeklyWindow.ForDate(new(2026,10,30),WeeklyTests.Now));Check(p.ValidNotes==3&&p.Window.Start=="2026-10-24"&&p.Window.End=="2026-10-30");
            Check(p.Markdown.Contains("独立记录-2026-10-24")&&p.Markdown.Contains("独立记录-2026-10-25")&&!p.Markdown.Contains("独立记录-2026-10-23")&&!p.Markdown.Contains("独立记录-2026-10-31"));
            WeeklyReport.Save(p);EqualFiles(before);Check(!Directory.Exists(Path.Combine(c.VaultPath,".obsidian")));
        });
        Test("独立库周报重跑保留手工补充，来源链接仍指向本地笔记",()=>{
            var c=New(Fixture());Note(c,"2026-10-26","## 今日学习内容\n第一条学习");var window=WeeklyWindow.ForDate(new(2026,10,30),WeeklyTests.Now);
            var p=WeeklyReport.Preview(c,window);WeeklyReport.Save(p);File.AppendAllText(p.Path,"\n我的手工补充 😀\n");
            Note(c,"2026-10-27","## 今日学习内容\n第二条学习");var next=WeeklyReport.Preview(c,window);WeeklyReport.Save(next);
            string text=DiarySession.ReadText(next.Path);Check(text.Contains("我的手工补充 😀")&&text.Contains("第二条学习")&&text.Contains("2026-10-26#今日学习内容")&&text.Contains("2026-10-27#今日学习内容"));
        });
        Test("独立模式定时与24点设置保存，重启模式不丢失",()=>{
            var c=New(Fixture());var settings=new WeeklySettings{AutoEnabled=true,Weekday=6,Hour=24,Minute=0};settings.Save(c);
            var loaded=CardConfig.Load(c.ConfigPath);var restored=WeeklySettings.Load(loaded);
            Check(loaded.IsStandalone&&restored.AutoEnabled&&restored.Weekday==6&&restored.Hour==24&&restored.Minute==0);
            Check(WeeklyWindow.Latest(DateTimeOffset.Parse("2026-11-02T00:00:00+08:00",CultureInfo.InvariantCulture),restored).Id=="2026-W44");
        });
        Test("独立资料夹与程序目录整体移动后相对路径有效",()=>{
            string install=Fixture();var c=New(install);string destination=install+"-moved";
            Check(SetupService.IsUnder(root,install)&&SetupService.IsUnder(root,destination)&&!Directory.Exists(destination));
            Directory.Move(install,destination);var loaded=CardConfig.Load(Path.Combine(destination,"diary-card.json"));
            Check(loaded.IsStandalone&&loaded.VaultPath==Path.Combine(destination,"我的日记")&&SetupService.IsUnder(loaded.VaultPath,loaded.TemplatePath)&&File.Exists(loaded.TemplatePath));
        });
        Test("配置提交被占用时回滚新建库，旧配置和旧库保持不变",()=>{
            string install=Fixture();var original=New(install);var before=File.ReadAllBytes(original.ConfigPath);var oldFiles=Snapshot(original.VaultPath);
            string next=Path.Combine(install,"第二份日记");var plan=Plan(install,next);
            using(var hold=new FileStream(original.ConfigPath,FileMode.Open,FileAccess.Read,FileShare.None))
                Reject(()=>SetupService.Complete(plan));
            Check(!Directory.Exists(next)&&File.ReadAllBytes(original.ConfigPath).SequenceEqual(before));EqualFiles(oldFiles);
        });
        Test("事务未提交时只清理自己创建的资源，不留manifest",()=>{
            string install=Fixture();var plan=Plan(install);
            using(var transaction=SetupService.Prepare(plan)){Check(!File.Exists(Path.Combine(plan.Config.VaultPath,"diarycard-library.json"))&&File.Exists(plan.Config.TemplatePath));}
            Check(!Directory.Exists(plan.Config.VaultPath)&&!File.Exists(Config(install))&&!Directory.Exists(plan.Config.UserDataPath));
        });
        Test("回滚时保留已被他处修改的本次模板",()=>{
            string install=Fixture();var plan=Plan(install);
            using(var transaction=SetupService.Prepare(plan)){File.AppendAllText(plan.Config.TemplatePath,"\n外部进程随后加入的内容\n");}
            Check(File.Exists(plan.Config.TemplatePath)&&File.ReadAllText(plan.Config.TemplatePath).Contains("外部进程随后加入的内容")&&!File.Exists(Config(install)));
        });
        Test("同一资料夹两种模式沿用同一互斥身份",()=>{
            var c=New(Fixture());string first=VaultUse.Key(c);c.StorageMode="obsidian";Check(VaultUse.Key(c)==first);
        });
        Test("带注释和尾随逗号的合法资料夹设置可以修改",()=>{
            var c=New(Fixture());string marker=Marker(c);string text=File.ReadAllText(marker);
            text=text.Insert(text.IndexOf('{')+1,"\n// 本地设置备注\n");text=text[..text.LastIndexOf('}')].TrimEnd()+",\n}\n";File.WriteAllText(marker,text,DiarySession.Utf8);
            var plan=SetupService.PlanStandalone(c.ConfigPath,c.VaultPath,c.TemplatePath,false,"新日记目录",c.DateFormat,c.UserDataPath);
            var updated=SetupService.Complete(plan);Check(updated.DiaryFolder==Path.Combine(c.VaultPath,"新日记目录")&&SetupService.InspectStandalone(c.VaultPath).Folder=="新日记目录");
        });
        Test("已有资料夹设置提交失败会回滚标记并保留旧配置",()=>{
            var c=New(Fixture());string marker=Marker(c);var beforeMarker=File.ReadAllBytes(marker);var beforeConfig=File.ReadAllBytes(c.ConfigPath);
            var plan=SetupService.PlanStandalone(c.ConfigPath,c.VaultPath,c.TemplatePath,false,"不能提交的新目录",c.DateFormat,c.UserDataPath);
            File.SetAttributes(c.ConfigPath,FileAttributes.ReadOnly);
            try{Reject(()=>SetupService.Complete(plan));}
            finally{File.SetAttributes(c.ConfigPath,FileAttributes.Normal);}
            Check(File.ReadAllBytes(marker).SequenceEqual(beforeMarker)&&File.ReadAllBytes(c.ConfigPath).SequenceEqual(beforeConfig));
        });
        Test("切换计划命中旧地址映射时阻止提交以免实际位置串写",()=>{
            var c=New(Fixture());var plan=SetupService.PlanStandalone(c.ConfigPath,c.VaultPath,c.TemplatePath,false,"","",c.UserDataPath);
            var before=Snapshot(c.VaultPath);var mappings=new Dictionary<string,string>{{c.VaultPath,Path.Combine(Path.GetDirectoryName(c.VaultPath)!,"另一资料夹")}};
            Reject(()=>MainWindow.ValidatePlanMappings(plan,mappings));EqualFiles(before);Check(plan.Config.PathMappings.Count==0);
            MainWindow.ValidatePlanMappings(plan,new(){{Path.Combine(root,"不相关旧地址"),Path.Combine(root,"不相关新地址")}});
        });
        DiarySession.AtomicText(Path.Combine(root,"standalone-result.json"),JsonSerializer.Serialize(new{passed=results.Count-failed-skipped,failed,skipped,results},CardConfig.Json));
        return failed==0?0:1;
    }
}
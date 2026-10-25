using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DailyDiaryCard;

internal static class TemplateRefreshRegression
{
    public static void Start(MainWindow window,CardConfig config,string output,Action<bool,string> assert,Action<Exception?> done)
    {
        var original=DiarySession.ReadText(config.TemplatePath);var parsed=new MarkdownDoc(original);var work=parsed.Sections[1].Title;var first=parsed.Sections[0].Title;
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(180)};var watch=Stopwatch.StartNew();int phase=0;SectionEditor? retained=null;string protectedHash="";var results=new List<string>();
        void Check(bool ok,string name){assert(ok,name);results.Add(name);}
        void Write(string text)=>File.WriteAllText(config.TemplatePath,text,DiarySession.Utf8);
        void Next(){phase++;watch.Restart();}
        void Finish(Exception? error)
        {
            timer.Stop();Write(original);window.RefreshTemplateNow();window.RefreshFromDisk();
            DiarySession.AtomicText(Path.Combine(output,"template-refresh-result.json"),JsonSerializer.Serialize(new{passed=error==null,results,error=error?.ToString()},CardConfig.Json));done(error);
        }
        timer.Tick+=(_,_)=>
        {
            try
            {
                if(watch.ElapsedMilliseconds>14000)throw new Exception("模板自动同步没有在合理时间内完成。");
                switch(phase)
                {
                    case 0:
                        retained=window.Editors[work];retained.Append("\n模板同步需保留的已填中文。\n");Check(window.TrySave(),"模板热更新前已填写内容正常保存");window.Activate();retained.TextInputs[0].Focus();
                        var next=original[..parsed.Sections[0].Start]+string.Concat(parsed.Sections.AsEnumerable().Reverse().Select(h=>h.RawHeading+h.Body))+"\n## 新增同步栏目\n最新提示\n";Write(next);Next();break;
                    case 1:
                        if(!window.Editors.ContainsKey("新增同步栏目"))break;
                        Check(window.Session.Template[0].Title==parsed.Sections.Last().Title,"实际计时器自动检测模板并按新顺序重排栏目");
                        Check(window.Editors.Count==parsed.Sections.Count+1&&window.Editors["新增同步栏目"].Body.Contains("最新提示"),"自动新增栏目并载入模板提示");
                        Check(ReferenceEquals(window.Editors[work],retained)&&window.Editors[work].Body.Contains("需保留的已填中文"),"模板更新保留既有编辑控件和已填写内容");
                        window.Capture(Path.Combine(output,"26-template-auto-refresh.png"));protectedHash=DiarySession.Hash(DiarySession.ReadText(window.Session.Target));Write("## 重复\n甲\n## 重复\n乙\n");Next();break;
                    case 2:
                        if(!window.Status.Contains("同步失败"))break;
                        Check(window.Editors.ContainsKey("新增同步栏目")&&DiarySession.Hash(DiarySession.ReadText(window.Session.Target))==protectedHash,"无效模板保留上一版界面与日记，不覆盖内容");
                        string changed=original.Replace(parsed.Sections[1].RawHeading,"## 重新命名后的栏目\n").Replace("前一天规划的任务会自动放在这里，完成后点击复选框即可。","模板提示已同步：完成后请勾选。");Write(changed);Next();break;
                    case 3:
                        if(!window.Editors.ContainsKey("重新命名后的栏目"))break;
                        Check(window.Session.ExtraSections.Any(h=>h.Title==work&&h.Body.Contains("需保留的已填中文")),"不明确改名的旧栏目保留在模板外内容中");
                        Check(window.Editors[first].Body.Contains("模板提示已同步"),"未修改的正文提示随新模板同步");
                        Write(original+"\n## 占用后新增栏目\n新提示\n");
                        using(var file=new FileStream(window.Session.Target,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(!window.RefreshTemplateNow()&&!window.Editors.ContainsKey("占用后新增栏目"),"日记被占用时模板同步暂停，旧界面仍可保留");
                        Check(window.RefreshTemplateNow()&&window.Editors.ContainsKey("占用后新增栏目"),"释放文件占用后模板可重试同步");
                        Check(window.Editors[work].Body.Contains("需保留的已填中文"),"恢复原栏目名称时重新匹配已有内容");Finish(null);break;
                }
            }
            catch(Exception e){Finish(e);}
        };
        timer.Start();
    }
}

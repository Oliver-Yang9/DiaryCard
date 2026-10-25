using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace DailyDiaryCard;

public static class TaskRegression
{
    private static readonly Regex Carried = new(@"(?m)^ {0,3}- \[(?<mark>[ xX])\][ \t]+(?<text>[^\r\n]*?)(?<meta>[ \t]+<!-- dailynote-task:[^\r\n]*)(?:\r?$)");
    public static void Run(CardConfig parent, string? fixturePath, string output, Action<bool,string> assert)
    {
        // All writes, including edits of a real-note copy, are restricted to this smoke test's vault.
        var root=Path.Combine(parent.VaultPath,"task-regression");Directory.CreateDirectory(root);
        var config=new CardConfig {VaultPath=root,TemplatePath=parent.TemplatePath,DiaryFolder=Path.Combine(root,"日记"),DateFormat="YYYY/MM/YYYY-MM-DD",UserDataPath=Path.Combine(root,"local")};
        var date=new DateOnly(2026,10,24);var target=config.PathFor(date);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var template=DiarySession.ReadText(config.TemplatePath).Replace("{{date:YYYY-MM-DD}}","2026-10-24");
        var doc=new MarkdownDoc(template);var title=doc.Sections[0].Title;
        string SyntheticBody()=>"\n前一天规划的任务会自动放在这里，完成后点击复选框即可。\n\n<!-- TASKS:START -->\n"+
            string.Concat(new[]{"整理学习笔记","完成桌面卡片","复习两节课程","阅读一小时","整理明天的资料"}.Select((text,i)=>$"- [{(i<2?"x":" ")}] {text} <!-- dailynote-task:2026-10-23:sample-{i} -->\n<!-- dailynote-seen:2026-10-23:sample-{i} -->\n"))+
            "<!-- TASKS:END -->\n\n### 今日临时任务\n\n- [ ] 在这里补充今天临时增加的任务：\n\n";
        var original=fixturePath is null?doc.Apply(new(){{title,SyntheticBody()}},doc.Sections):DiarySession.ReadText(fixturePath);
        var sourceHash=fixturePath is null?null:DiarySession.Hash(original);
        File.WriteAllText(target,original,DiarySession.Utf8);
        var session=new DiarySession(config,date);var body=session.Bodies[title];
        var carried=Carried.Matches(body).Cast<Match>().ToArray();
        assert(carried.Length>0,"承接任务回归：带内部标记的真实任务已载入隔离副本");
        int expectedCount=Regex.Matches(body,@"(?m)^ {0,3}(?:[-*+]|[0-9]+[.)])[ \t]+\[[ xX]\][ \t]+").Count;
        var editor=new SectionEditor(title,body,new MarkdownDoc(original).NewLine,15,s=>session.Edit(title,s));
        assert(editor.TaskInputs.Count==expectedCount,"带尾部承接标记的任务及临时任务均生成复选框");
        assert(editor.TaskInputs.Take(carried.Length).Select(c=>c.IsChecked==true).SequenceEqual(carried.Select(m=>m.Groups["mark"].Value!=" ")),"每项已有完成状态均与日记原文一致");
        assert(!editor.TextInputs.Any(t=>t.Text.Contains("dailynote-")),"任务与输入区不显示内部承接备注");
        assert(editor.Body==body&&!session.Dirty,"初次渲染不改写任务、标记或换行");
        session.Save();assert(DiarySession.ReadText(target)==original,"仅打开承接任务时日记原文不变");
        int pending=Array.FindIndex(carried,m=>m.Groups["mark"].Value==" ");
        if(pending<0)pending=0;
        bool initiallyChecked=editor.TaskInputs[pending].IsChecked==true;
        editor.TaskInputs[pending].IsChecked=!initiallyChecked;
        int marker=carried[pending].Groups["mark"].Index;
        var checkedBody=body[..marker]+(initiallyChecked?" ":"x")+body[(marker+1)..];
        assert(editor.Body==checkedBody,"切换承接任务只修改一个完成标记，尾注释逐字保留");
        editor.TaskInputs[pending].IsChecked=initiallyChecked;
        assert(editor.Body==body,"再次切换后完整栏目逐字恢复");
        editor.TaskInputs[pending].IsChecked=!initiallyChecked;
        var taskText=editor.TextInputs.Where(t=>!t.AcceptsReturn).ToArray();
        var task=carried[pending];var textIndex=task.Groups["text"].Index;var textLength=task.Groups["text"].Length;
        taskText[pending].Text+="，中文补充";
        var editedBody=checkedBody[..(textIndex+textLength)]+"，中文补充"+checkedBody[(textIndex+textLength)..];
        assert(editor.Body==editedBody,"修改任务中文只更新可见文字，保留 task/seen 标记与其他任务");
        session.Save();var saved=DiarySession.ReadText(target);
        assert(saved==new MarkdownDoc(original).Apply(new(){{title,editedBody}},session.Template),"保存后其他栏目、属性和任务区域标记完整保留");
        var restarted=new DiarySession(config,date);var restored=new SectionEditor(title,restarted.Bodies[title],new MarkdownDoc(saved).NewLine,15,_=>{});
        assert(restored.TaskInputs[pending].IsChecked==!initiallyChecked&&restored.TextInputs.Any(t=>t.Text.EndsWith("，中文补充")),"重新载入后勾选状态及中文修改恢复");
        var switcher=restored.Children.OfType<DockPanel>().Single().Children.OfType<Button>().Single();
        switcher.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        assert(restored.TextInputs.Single().Text.Contains("dailynote-task:"),"Markdown 模式仍可查看完整内部标记");
        restored.Children.OfType<DockPanel>().Single().Children.OfType<Button>().Single().RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        assert(restored.Body==editedBody&&restored.TaskInputs.Count==expectedCount,"表单和 Markdown 往返不丢失承接任务");
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var sample=string.Join(nl,new[]{"- [ ] 真实任务 \t<!-- one -->  <!-- two -->  ","<!--","- [ ] 注释内伪任务","-->","```md","- [ ] 代码内伪任务 <!-- 未闭合注释","```","    - [ ] 缩进代码伪任务","- [X] 已完成 <!-- done -->","- [ ] 普通任务",""});
            var control=new SectionEditor("任意栏目",sample,nl,15,_=>{});
            assert(control.TaskInputs.Count==3&&control.TaskInputs[1].IsChecked==true,$"{(nl.Length==1?"LF":"CRLF")}：多尾注释任务识别，代码和整块注释不生成复选框");
            control.TaskInputs[0].IsChecked=true;
            assert(control.Body==sample.Replace("- [ ] 真实任务","- [x] 真实任务"),$"{(nl.Length==1?"LF":"CRLF")}：多注释、原空格和换行全部保留");
        }
        if(fixturePath is not null)assert(DiarySession.Hash(DiarySession.ReadText(fixturePath))==sourceHash,"真实当天日记只读，验证前后散列不变");
        // The preview uses synthetic labels, never the user's private diary text.
        File.WriteAllText(target,doc.Apply(new(){{title,SyntheticBody()}},doc.Sections),DiarySession.Utf8);
        var preview=new MainWindow(config,()=>date);preview.Show();preview.UpdateLayout();
        preview.Capture(Path.Combine(output,"10-carried-tasks.png"));preview.ExitCard();
    }
}

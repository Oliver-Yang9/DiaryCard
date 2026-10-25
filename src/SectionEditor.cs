using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DailyDiaryCard;

public sealed class SectionEditor : StackPanel
{
    private sealed class Part(string raw) { public string Raw = raw; }
    private readonly List<Part> parts = new();
    private readonly Action<string> changed;
    private string body;
    private bool loading, sourceMode;
    private readonly string newLine;
    private double fontSize;
    public string Body => body;
    public string Title { get; }
    public List<TextBox> TextInputs { get; } = new();
    public List<CheckBox> TaskInputs { get; } = new();
    private static readonly Regex Task = new("^ {0,3}(?:[-*+]|[0-9]+[.)])[ \\t]+\\[(?<mark>[ xX])\\][ \\t]+(?<text>.*)$");
    public SectionEditor(string title, string initial, string nl, double size, Action<string> onChanged)
    {
        Title=title; body=initial; changed=onChanged; newLine=nl; fontSize=size; Render();
    }
    public void SetBody(string value) { if (value==body) return; body=value; Render(); }
    public void SetFont(double size) { fontSize=size; foreach(var t in TextInputs){t.FontSize=size;t.SetValue(TextBlock.LineHeightProperty,size*1.8);} }
    public void Append(string value) { SetBody(body+value); changed(body); }
    private string Normalize(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n",newLine);
    private TextBox Input(string text, bool multiline)
    {
        var tb=new TextBox {Text=text, AcceptsReturn=multiline, AcceptsTab=multiline, TextWrapping=TextWrapping.Wrap, FontSize=fontSize, VerticalScrollBarVisibility=ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, MinHeight=multiline ? 72 : 36, Margin=new Thickness(0,4,0,5)};
        tb.SetValue(TextBlock.LineHeightProperty, fontSize*1.8);
        tb.SetResourceReference(Control.BackgroundProperty,"SectionField");
        tb.PreviewMouseWheel += (_,e) => { if (e.Handled) return; var wheel=new MouseWheelEventArgs(e.MouseDevice,e.Timestamp,e.Delta){RoutedEvent=Mouse.MouseWheelEvent,Source=tb}; RaiseEvent(wheel); e.Handled=true; };
        TextInputs.Add(tb); return tb;
    }
    private void Render()
    {
        loading=true; Children.Clear(); parts.Clear(); TextInputs.Clear(); TaskInputs.Clear();
        var tools=new DockPanel {LastChildFill=false, Margin=new Thickness(0,0,0,6)};
        var label=new TextBlock {Text=sourceMode ? "Markdown 原文" : "正文与任务",FontSize=12,VerticalAlignment=VerticalAlignment.Center}; label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var switcher=new Button {Content=sourceMode ? "返回表单" : "编辑 Markdown",FontSize=12,Padding=new Thickness(8,4,8,4)};
        switcher.Click+=(_,_)=>{sourceMode=!sourceMode;Render();};
        DockPanel.SetDock(label,Dock.Left);DockPanel.SetDock(switcher,Dock.Right);tools.Children.Add(switcher);tools.Children.Add(label);Children.Add(tools);
        if(sourceMode)
        {
            var source=Input(body,true); source.MinHeight=150;
            source.TextChanged+=(_,_)=>{if(loading)return;body=Normalize(source.Text);changed(body);};
            Children.Add(source);
        }
        else
        {
            var visible=MarkdownDoc.VisibleLines(body,allowTrailingComments:true).ToDictionary(l=>l.Start,l=>l.Text);
            string pending=""; bool comment=false;
            void AddNormal()
            {
                if(pending.Length==0)return;
                var p=new Part(pending);parts.Add(p);pending="";
                if(string.IsNullOrWhiteSpace(p.Raw))return;
                var prefix=Regex.Match(p.Raw,"\\A(?:[ \\t]*\\r?\\n)*").Value;
                var remaining=p.Raw[prefix.Length..];
                var suffix=Regex.Match(remaining,"(?:\\r?\\n[ \\t]*)+\\z").Value;
                var content=remaining[..(remaining.Length-suffix.Length)];
                var input=Input(content,true);
                input.MinHeight=string.IsNullOrWhiteSpace(content)?90:64;
                input.TextChanged+=(_,_)=>{if(loading)return;p.Raw=prefix+Normalize(input.Text)+suffix;Update();};
                Children.Add(input);
            }
            foreach(var line in MarkdownDoc.Lines(body))
            {
                bool hidden=comment||line.Text.Contains("<!--");
                foreach(Match token in Regex.Matches(line.Text,"<!--|-->"))comment=token.Value=="<!--";
                bool eligible=visible.TryGetValue(line.Start,out var visibleText);
                var task=Task.Match(eligible?visibleText!:line.Text);
                if(hidden&&!(eligible&&task.Success)){AddNormal();parts.Add(new(line.Raw));continue;}
                if(eligible&&task.Success)
                {
                    AddNormal();var p=new Part(line.Raw);parts.Add(p);
                    var row=new Grid {Margin=new Thickness(0,3,0,3)};row.ColumnDefinitions.Add(new(){Width=new GridLength(32)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
                    var cb=new CheckBox {IsChecked=task.Groups["mark"].Value!=" ",ToolTip="切换任务完成状态",VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,9,0,0)};
                    System.Windows.Automation.AutomationProperties.SetName(cb,"任务："+task.Groups["text"].Value);
                    var text=Input(task.Groups["text"].Value,false); Grid.SetColumn(text,1);TaskInputs.Add(cb);
                    var prefix=line.Text[..task.Groups["text"].Index];var tail=line.Raw[(task.Groups["text"].Index+task.Groups["text"].Length)..];
                    void TaskChanged(){if(loading)return;var marker=task.Groups["mark"].Index;var lead=prefix[..marker]+(cb.IsChecked==true?"x":" ")+prefix[(marker+1)..];p.Raw=lead+text.Text.Replace("\r","").Replace("\n"," ")+tail;Update();}
                    cb.Checked+=(_,_)=>TaskChanged();cb.Unchecked+=(_,_)=>TaskChanged();text.TextChanged+=(_,_)=>TaskChanged();row.Children.Add(cb);row.Children.Add(text);Children.Add(row);
                }
                else if(eligible&&Regex.IsMatch(line.Text,"^ {0,3}###(?:[ \\t]+)"))
                {
                    AddNormal();parts.Add(new(line.Raw));Children.Add(new TextBlock {Text=Regex.Replace(line.Text,"^ {0,3}###[ \\t]+", ""),FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,8)});
                }
                else pending+=line.Raw;
            }
            AddNormal();
            if(TextInputs.Count==0){var p=new Part("");parts.Add(p);var blank=Input("",true);blank.TextChanged+=(_,_)=>{if(loading)return;p.Raw=Normalize(blank.Text);Update();};Children.Add(blank);}
        }
        var add=new Button {Content="＋ 添加任务",HorizontalAlignment=HorizontalAlignment.Left,FontSize=12,Padding=new Thickness(9,5,9,5),Margin=new Thickness(0,8,0,0)};
        add.Click+=(_,_)=>{body+=(body.EndsWith('\n')?"":newLine)+"- [ ] 新任务"+newLine;sourceMode=false;Render();changed(body);if(TaskInputs.Count>0)TextInputs.Last().Focus();};Children.Add(add);
        loading=false;
    }
    private void Update(){body=string.Concat(parts.Select(p=>p.Raw));changed(body);}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace DailyDiaryCard;
public sealed partial class MainWindow
{
    private LibraryPanel? libraryPanel;
    public LibraryPanel CreateLibraryDialog()
    {
        if(libraryPanel!=null)return libraryPanel;
        var panel=new LibraryPanel(this,config,Dialog("我的日记资料库",1120,850));
        libraryPanel=panel;panel.Window.Closed+=(_,_)=>{if(ReferenceEquals(libraryPanel,panel))libraryPanel=null;};return panel;
    }
    public void OpenDocumentInApp(string path)
    {
        if(!TrySave())return;
        if(Path.GetFullPath(path).Equals(session.Target,StringComparison.OrdinalIgnoreCase)){ShowCard();return;}
        var panel=CreateLibraryDialog();if(panel.OpenPath(path)){if(!panel.Window.IsVisible)panel.Window.Show();panel.Window.Activate();}
    }
    public bool TrySaveLibrary()=>libraryPanel?.SaveCurrent()??true;
    public bool LibraryHasPendingInput=>libraryPanel?.HasPendingInput==true;
    internal void FollowLibraryDay(string path)
    {
        if(libraryPanel?.CurrentPath?.Equals(path,StringComparison.OrdinalIgnoreCase)!=true)return;
        bool visible=libraryPanel.Window.IsVisible;
        libraryPanel.Window.Close();
        if(visible)ShowCard(false);
    }
}

public sealed class LibraryPanel
{
    private readonly MainWindow owner;
    private readonly CardConfig config;
    private DiarySession? diary;
    private LibraryDocument? document;
    private readonly StackPanel form=new();
    private readonly RichTextBox reader=new(){IsReadOnly=true,BorderThickness=new Thickness(0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly ScrollViewer formScroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    private readonly Grid body=new();
    private readonly TextBlock documentTitle=new(){FontSize=24,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock location=new(){FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,10)};
    private readonly TextBlock count=new(){FontSize=12,Margin=new Thickness(0,8,0,8)};
    private readonly WrapPanel sources=new(){Margin=new Thickness(0,8,0,0)};
    private readonly WrapPanel templateTools=new(){Margin=new Thickness(0,0,0,8),Visibility=Visibility.Collapsed};
    private readonly Button editButton=new(){Content="编辑手工补充",Visibility=Visibility.Collapsed};
    private readonly Button readButton=new(){Content="阅读周报",Visibility=Visibility.Collapsed};
    private readonly DispatcherTimer timer,searchTimer;
    private DateTime lastEdit=DateTime.MinValue,lastDraft=DateTime.MinValue,nextAttempt=DateTime.MinValue;
    private int listingVersion;
    private bool loading,selecting,composing,manualOnly;
    private string manualPrefix="";
    private LibraryKind currentKind;
    public Window Window{get;}
    public TextBox SearchBox{get;}=new(){Height=38,MinWidth=180,ToolTip="搜索日期、标题或正文"};
    public ComboBox FilterChoice{get;}=new(){Width=100,MinHeight=36};
    public ListBox ItemsList{get;}=new(){BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
    public TextBox RawText{get;}=new(){AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=15,Visibility=Visibility.Collapsed};
    public TextBox NewSectionInput{get;}=new(){Width=180,Height=36,ToolTip="新栏目的标题"};
    public Button AddColumnButton{get;}=new(){Content="添加栏目"};
    public TextBlock Status{get;}=new(){TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,8,0,0)};
    public Button SaveButton{get;}=new(){Content="立即保存"};
    public Button ExportButton{get;}=new(){Content="导出副本"};
    public List<Button> SourceButtons{get;}=new();
    public Dictionary<string,SectionEditor> Editors{get;}=new();
    public string? CurrentPath{get;private set;}
    public bool Dirty=>diary?.Dirty??document?.Dirty??false;
    public bool HasPendingInput=>Dirty||composing;
    public bool IsListing{get;private set;}
    public string CurrentText=>diary?.LocalDocument()??document?.Text??"";
    public LibraryPanel(MainWindow owner,CardConfig config,Window window)
    {
        this.owner=owner;this.config=config;Window=window;WeeklyControlStyles.Apply(window);
        window.Width=Math.Min(1120,SystemParameters.WorkArea.Width-24);window.MinWidth=Math.Min(760,window.Width);window.MinHeight=Math.Min(600,window.Height);
        ApplyStyles();
        foreach(var control in new Control[]{ItemsList,reader,RawText}){control.SetResourceReference(Control.BackgroundProperty,"Field");control.SetResourceReference(Control.ForegroundProperty,"Text");control.SetResourceReference(Control.BorderBrushProperty,"Line");}
        count.SetResourceReference(TextBlock.ForegroundProperty,"Muted");location.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var layout=new DockPanel{Margin=new Thickness(22)};
        var head=new StackPanel();head.Children.Add(new TextBlock{Text="所有记录，都在这里",FontSize=25,FontWeight=FontWeights.SemiBold});
        head.Children.Add(new TextBlock{Text="查看过去的日记，阅读周报，调整自己的复盘栏目。",FontSize=13,Margin=new Thickness(0,6,0,14)});
        var filters=new Grid();filters.ColumnDefinitions.Add(new(){Width=GridLength.Auto});filters.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});filters.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        foreach(string name in new[]{"日记","周报","模板"})FilterChoice.Items.Add(name);FilterChoice.SelectedIndex=0;filters.Children.Add(FilterChoice);
        SearchBox.Margin=new Thickness(10,0,10,0);Grid.SetColumn(SearchBox,1);filters.Children.Add(SearchBox);var refresh=new Button{Content="刷新记录"};Grid.SetColumn(refresh,2);filters.Children.Add(refresh);head.Children.Add(filters);head.Children.Add(count);DockPanel.SetDock(head,Dock.Top);layout.Children.Add(head);
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(240)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});ItemsList.Margin=new Thickness(0,0,16,0);grid.Children.Add(ItemsList);
        var content=new DockPanel();Grid.SetColumn(content,1);var top=new StackPanel();top.Children.Add(documentTitle);top.Children.Add(location);
        var tools=new WrapPanel{Margin=new Thickness(0,0,0,8)};tools.Children.Add(SaveButton);tools.Children.Add(ExportButton);tools.Children.Add(editButton);tools.Children.Add(readButton);
        var reload=new Button{Content="保留草稿并重新读取"};reload.Click+=(_,_)=>ReloadKeepingDraft();tools.Children.Add(reload);top.Children.Add(tools);
        templateTools.Children.Add(NewSectionInput);templateTools.Children.Add(AddColumnButton);top.Children.Add(templateTools);DockPanel.SetDock(top,Dock.Top);content.Children.Add(top);
        var bottom=new StackPanel();bottom.Children.Add(new ScrollViewer{Content=sources,MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});bottom.Children.Add(Status);DockPanel.SetDock(bottom,Dock.Bottom);content.Children.Add(bottom);
        formScroll.Content=form;body.Children.Add(formScroll);body.Children.Add(reader);body.Children.Add(RawText);content.Children.Add(body);grid.Children.Add(content);layout.Children.Add(grid);window.Content=layout;
        formScroll.Visibility=reader.Visibility=Visibility.Collapsed;documentTitle.Text="从左侧选择一条记录";location.Text="今天的填写仍在桌面卡片，过去的记录可以在这里继续完善。";SaveButton.IsEnabled=ExportButton.IsEnabled=false;
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};timer.Tick+=(_,_)=>Tick();
        searchTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};searchTimer.Tick+=async(_,_)=>{searchTimer.Stop();await RefreshAsync();};
        SearchBox.TextChanged+=(_,_)=>{searchTimer.Stop();searchTimer.Start();};
        FilterChoice.SelectionChanged+=async(_,_)=>{if(!loading)await RefreshAsync();};refresh.Click+=async(_,_)=>await RefreshAsync();
        ItemsList.SelectionChanged+=(_,_)=>{if(selecting)return;if(ItemsList.SelectedItem is ListBoxItem item&&item.Tag is LibraryEntry entry){if(!OpenPath(entry.Path)){selecting=true;ItemsList.SelectedItem=ItemsList.Items.OfType<ListBoxItem>().FirstOrDefault(i=>i.Tag is LibraryEntry e&&e.Path.Equals(CurrentPath,StringComparison.OrdinalIgnoreCase));selecting=false;}}};
        SaveButton.Click+=(_,_)=>SaveCurrent();
        ExportButton.Click+=(_,_)=>{if(CurrentPath==null)return;var picker=new Microsoft.Win32.SaveFileDialog{Title="导出本地记录副本",FileName=Path.GetFileName(CurrentPath),Filter="Markdown|*.md"};if(picker.ShowDialog(window)==true)try{ExportTo(picker.FileName);Status.Text="副本已导出，原件保留。";}catch(Exception e){Error(e);}};
        RawText.TextChanged+=(_,_)=>{if(loading||document==null)return;document.Edit(manualOnly?manualPrefix+RawText.Text:RawText.Text);Changed();};
        editButton.Click+=(_,_)=>ShowManualEditor();readButton.Click+=(_,_)=>ShowReader();
        AddColumnButton.Click+=(_,_)=>AddColumn(NewSectionInput.Text);
        TextCompositionManager.AddPreviewTextInputStartHandler(window,(_,_)=>composing=true);TextCompositionManager.AddPreviewTextInputUpdateHandler(window,(_,_)=>composing=true);TextCompositionManager.AddPreviewTextInputHandler(window,(_,_)=>composing=false);
        window.Deactivated+=(_,_)=>{composing=false;SaveCurrent();};
        window.Closing+=(_,e)=>{if(!SaveCurrent())e.Cancel=true;};
        window.Closed+=(_,_)=>{timer.Stop();searchTimer.Stop();listingVersion++;};
        window.ContentRendered+=async(_,_)=>{timer.Start();await RefreshAsync();};
    }
    private void ApplyStyles()
    {
        var resources=(ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="ListBoxItem"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Padding" Value="12"/><Setter Property="Margin" Value="0,0,0,6"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="row" Background="{DynamicResource Surface}" CornerRadius="8" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="row" Property="Background" Value="{DynamicResource Hover}"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="row" Property="Background" Value="{DynamicResource Hover}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        </ResourceDictionary>
        """);Window.Resources.MergedDictionaries.Add(resources);
    }
    private void Changed(){lastEdit=DateTime.UtcNow;nextAttempt=DateTime.MinValue;Status.Text="保存中…";Status.SetResourceReference(TextBlock.ForegroundProperty,"Accent");}
    private void Error(Exception e){Status.Text="未能保存或打开，输入仍保留："+e.Message;Status.SetResourceReference(TextBlock.ForegroundProperty,"Danger");}
    private void Tick()
    {
        if(composing||!Dirty)return;
        try
        {
            if((DateTime.UtcNow-lastEdit).TotalMilliseconds>220&&(DateTime.UtcNow-lastDraft).TotalMilliseconds>450){if(diary!=null)diary.PersistDraft("资料库编辑");else document?.PersistDraft();lastDraft=DateTime.UtcNow;}
            if((DateTime.UtcNow-lastEdit).TotalMilliseconds>700&&DateTime.UtcNow>=nextAttempt&&diary?.IsConflict!=true)SaveCurrent();
        }
        catch(Exception e){nextAttempt=DateTime.UtcNow.AddSeconds(5);Error(e);}
    }
    public async Task<bool> RefreshAsync()
    {
        int version=++listingVersion;string query=SearchBox.Text.Trim();var kind=FilterChoice.SelectedIndex==1?LibraryKind.Weekly:FilterChoice.SelectedIndex==2?LibraryKind.Template:LibraryKind.Diary;IsListing=true;count.Text="正在查找记录…";
        try
        {
            var scan=await Task.Run(()=>{var entries=LibraryService.List(config,kind,query,out int unavailable);return(entries,unavailable);});
            if(version!=listingVersion)return false;
            selecting=true;ItemsList.Items.Clear();foreach(var entry in scan.entries)
            {
                var item=new ListBoxItem{Tag=entry};var text=new StackPanel();text.Children.Add(new TextBlock{Text=entry.Title,FontWeight=FontWeights.SemiBold,FontSize=14});
                var summary=new TextBlock{Text=entry.Summary.Length>0?entry.Summary:"尚无正文",FontSize=11,TextWrapping=TextWrapping.Wrap,MaxHeight=48,Margin=new Thickness(0,5,0,0)};summary.SetResourceReference(TextBlock.ForegroundProperty,"Muted");text.Children.Add(summary);item.Content=text;ItemsList.Items.Add(item);
                if(entry.Path.Equals(CurrentPath,StringComparison.OrdinalIgnoreCase))item.IsSelected=true;
            }
            selecting=false;count.Text=$"{scan.entries.Count} 条记录"+(scan.unavailable>0?$" · {scan.unavailable} 条暂无法读取":"");return true;
        }
        catch(Exception e){if(version==listingVersion){count.Text="记录读取失败";Error(e);}return false;}
        finally{if(version==listingVersion)IsListing=false;selecting=false;}
    }
    public bool OpenPath(string file)
    {
        if(!SaveCurrent())return false;
        try
        {
            file=LibraryService.CheckPath(config,file);
            if(file.Equals(config.PathFor(DateOnly.FromDateTime(DateTime.Now)),StringComparison.OrdinalIgnoreCase)){owner.ShowCard();Window.Close();return false;}
            string text=DiarySession.ReadText(file);var parsed=WeeklyReport.Split(text);
            var kind=file.Equals(config.TemplatePath,StringComparison.OrdinalIgnoreCase)?LibraryKind.Template:parsed.Meta.GetValueOrDefault("type")=="weekly-report"?LibraryKind.Weekly:LibraryKind.Diary;
            var date=LibraryService.DateFor(file,text);
            DiarySession? nextDiary=null;LibraryDocument? nextDocument=null;
            if(kind==LibraryKind.Diary&&date!=null&&config.PathFor(date.Value).Equals(file,StringComparison.OrdinalIgnoreCase))nextDiary=new DiarySession(config,date.Value);
            else nextDocument=new LibraryDocument(config,file,kind);
            diary=nextDiary;document=nextDocument;Editors.Clear();form.Children.Clear();sources.Children.Clear();SourceButtons.Clear();manualOnly=false;
            CurrentPath=file;currentKind=kind;documentTitle.Text=kind==LibraryKind.Template?"调整复盘栏目":kind==LibraryKind.Weekly?Path.GetFileNameWithoutExtension(file):date?.ToString("yyyy-MM-dd")??Path.GetFileNameWithoutExtension(file);
            int filter=kind==LibraryKind.Template?2:kind==LibraryKind.Weekly?1:0;
            if(FilterChoice.SelectedIndex!=filter){SearchBox.Clear();FilterChoice.SelectedIndex=filter;}
            location.Text=Path.GetRelativePath(config.VaultPath,file);SaveButton.IsEnabled=ExportButton.IsEnabled=true;
            templateTools.Visibility=kind==LibraryKind.Template?Visibility.Visible:Visibility.Collapsed;editButton.Visibility=kind==LibraryKind.Weekly?Visibility.Visible:Visibility.Collapsed;readButton.Visibility=Visibility.Collapsed;
            if(diary!=null)BuildForm();
            else if(kind==LibraryKind.Weekly)ShowReader();
            else{loading=true;RawText.Text=document!.Text;loading=false;RawText.Visibility=Visibility.Visible;reader.Visibility=formScroll.Visibility=Visibility.Collapsed;}
            Status.Text=kind==LibraryKind.Template?"每个二级标题（## 标题）对应一个卡片栏目；可用上方按钮添加。保存后主卡片会自动同步。":diary?.Restored==true||document?.Restored==true?"已恢复未保存的输入，保存前会检查磁盘修改。":kind==LibraryKind.Weekly?"周报在软件内阅读，来源可点击；手工补充会自动保存。":"已载入 · 输入停顿后自动保存";
            if(document?.RecoveryNotice!=null)Status.Text=document.RecoveryNotice;
            if(diary?.Error!=null)Status.Text=diary.Error;
            Status.SetResourceReference(TextBlock.ForegroundProperty,diary?.Error!=null||document?.RecoveryNotice!=null?"Danger":"Muted");return true;
        }
        catch(Exception e){Error(e);return false;}
    }
    private void BuildForm()
    {
        if(diary==null)return;loading=true;form.Children.Clear();Editors.Clear();
        foreach(var section in diary.Template)
        {
            var editor=new SectionEditor(section.Title,diary.Bodies[section.Title],new MarkdownDoc(diary.TemplateText).NewLine,15,body=>{if(loading||diary==null)return;diary.Edit(section.Title,body);Changed();});
            Editors[section.Title]=editor;form.Children.Add(new Expander{Header=new TextBlock{Text=section.Title,FontWeight=FontWeights.SemiBold,FontSize=17},Content=editor,IsExpanded=true});
        }
        if(diary.ExtraSections.Count>0){form.Children.Add(new TextBlock{Text="模板外已有内容（保留原文）",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,5)});form.Children.Add(new TextBox{Text=string.Concat(diary.ExtraSections.Select(h=>h.RawHeading+h.Body)),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true});}
        formScroll.Visibility=Visibility.Visible;reader.Visibility=RawText.Visibility=Visibility.Collapsed;loading=false;
    }
    public bool SaveCurrent()
    {
        if(composing){Status.Text="请先完成当前中文输入。";return false;}
        if(!Dirty)return true;
        try
        {
            if(diary!=null){diary.Save();loading=true;foreach(var pair in Editors)if(diary.Bodies.TryGetValue(pair.Key,out var body))pair.Value.SetBody(body);loading=false;}
            else document?.Save();
            Status.Text="已保存到本机";Status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");return true;
        }
        catch(Exception e){nextAttempt=DateTime.UtcNow.AddSeconds(5);Error(e);return false;}
    }
    public void ExportTo(string destination)
    {
        if(CurrentPath==null)throw new IOException("请先选择记录。");LibraryService.Export(destination,CurrentText,CurrentPath);
    }
    public bool ReloadKeepingDraft()
    {
        if(CurrentPath==null)return false;try
        {
            string file=CurrentPath,archive="";
            if(diary!=null){archive=diary.ArchiveDraft("资料库重新读取前的本地输入");diary.ReloadKeepingDraft();BuildForm();}
            else if(document!=null){archive=document.ReloadKeepingDraft();if(currentKind==LibraryKind.Weekly)ShowReader();else{loading=true;RawText.Text=document.Text;loading=false;}}
            Status.Text="磁盘内容已重新载入，原输入另存于："+archive;Status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");return true;
        }
        catch(Exception e){Error(e);return false;}
    }
    public bool AddColumn(string title)
    {
        if(currentKind!=LibraryKind.Template||document==null)return false;
        try
        {
            title=title.Trim();if(title.Length==0||title.Contains('\r')||title.Contains('\n')||title.StartsWith('#'))throw new IOException("请输入一行栏目标题。");
            var parsed=TemplateReconciler.Parse(document.Text);if(parsed.Any(h=>h.Title==title))throw new IOException("已有同名栏目，请使用其他名称。");
            loading=true;RawText.Text=document.Text.TrimEnd()+"\n\n## "+title+"\n\n";document.Edit(RawText.Text);loading=false;NewSectionInput.Clear();Changed();return true;
        }
        catch(Exception e){Error(e);return false;}
    }
    public bool ShowManualEditor()
    {
        if(document==null||currentKind!=LibraryKind.Weekly)return false;
        int end=document.Text.IndexOf(WeeklyReport.End,StringComparison.Ordinal);
        if(WeeklyReport.Markers(DiarySession.Utf8.GetBytes(document.Text))==null||end<0){Status.Text="这份周报没有唯一自动区，建议导出副本后整理。";return false;}
        manualPrefix=document.Text[..(end+WeeklyReport.End.Length)];manualOnly=true;loading=true;RawText.Text=document.Text[manualPrefix.Length..];loading=false;
        RawText.Visibility=Visibility.Visible;reader.Visibility=formScroll.Visibility=Visibility.Collapsed;readButton.Visibility=Visibility.Visible;editButton.Visibility=Visibility.Collapsed;Status.Text="仅编辑自动区之后的手工内容。输入停顿后自动保存；来源正文保留。";return true;
    }
    private static void AddReadableText(InlineCollection target,string text)
    {
        int position=0;
        foreach(Match match in Regex.Matches(text,@"\*\*([^*]+)\*\*"))
        {
            target.Add(new Run(text[position..match.Index]));target.Add(new Bold(new Run(match.Groups[1].Value)));position=match.Index+match.Length;
        }
        target.Add(new Run(text[position..]));
    }
    public void ShowReader()
    {
        if(document==null)return;loading=true;RawText.Visibility=formScroll.Visibility=Visibility.Collapsed;reader.Visibility=Visibility.Visible;readButton.Visibility=Visibility.Collapsed;editButton.Visibility=Visibility.Visible;manualOnly=false;
        var flow=new FlowDocument{FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=15,PagePadding=new Thickness(18),LineHeight=26};flow.SetResourceReference(FlowDocument.ForegroundProperty,"Text");
        string text=Regex.Replace(document.Text,@"\A\uFEFF?---\r?\n[\s\S]*?\r?\n---(?:\r?\n|$)","");text=Regex.Replace(text,@"<!--[\s\S]*?-->","");
        var links=LibraryService.Links(config,document.Text);sources.Children.Clear();SourceButtons.Clear();
        foreach(var link in links)
        {
            var button=new Button{Content=link.Label,FontSize=11,MaxWidth=330,Margin=new Thickness(0,2,6,2),ToolTip=link.Heading};button.Click+=(_,_)=>OpenPath(link.Path);sources.Children.Add(button);SourceButtons.Add(button);
        }
        foreach(string raw in text.Replace("\r\n","\n").Split('\n'))
        {
            string line=raw.TrimEnd();if(line.Length==0)continue;var heading=Regex.Match(line,@"^(#{1,6})\s+(.+)");
            var paragraph=new Paragraph{Margin=new Thickness(0,heading.Success?14:0,0,7)};
            if(heading.Success){paragraph.FontSize=heading.Groups[1].Length==1?24:19;paragraph.FontWeight=FontWeights.SemiBold;line=heading.Groups[2].Value;}
            else line=Regex.Replace(Regex.Replace(line,@"^\s*-\s+\[x\]\s*","☑ ",RegexOptions.IgnoreCase),@"^\s*-\s+\[ \]\s*","☐ ");
            int start=0;foreach(Match match in Regex.Matches(line,@"\[\[([^\]\r\n]+)\]\]"))
            {
                AddReadableText(paragraph.Inlines,line[start..match.Index]);string value=match.Groups[1].Value;int bar=value.IndexOf('|');string label=bar<0?value:value[(bar+1)..];var link=LibraryService.Links(config,match.Value).FirstOrDefault();
                if(link!=null){var hyperlink=new Hyperlink(new Run(label));hyperlink.SetResourceReference(Hyperlink.ForegroundProperty,"Accent");hyperlink.Click+=(_,_)=>OpenPath(link.Path);paragraph.Inlines.Add(hyperlink);}
                else paragraph.Inlines.Add(new Run(label));start=match.Index+match.Length;
            }
            AddReadableText(paragraph.Inlines,line[start..]);flow.Blocks.Add(paragraph);
        }
        reader.Document=flow;loading=false;
    }
}
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DailyDiaryCard;

public sealed partial class MainWindow
{
    private bool weeklyBusy;
    internal bool WeeklyTimerEnabled{get;set;}=true;
    private DateTime nextWeeklyCheck=DateTime.UtcNow.AddSeconds(3);
    private readonly TextBlock weeklyStatus=new(){FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0),Visibility=Visibility.Collapsed};
    public bool WeeklyBusy=>weeklyBusy;
    public string WeeklyMessage=>weeklyStatus.Text;
    public WeeklyPanel CreateWeeklyDialog()=>new(this,config,Dialog("周报汇总",900,780));
    private void ShowWeekly()=>CreateWeeklyDialog().Window.ShowDialog();
    private void SetWeeklyMessage(string message,bool error=false)
    {
        weeklyStatus.Text=message;weeklyStatus.Visibility=Visibility.Visible;
        weeklyStatus.SetResourceReference(TextBlock.ForegroundProperty,error?"Danger":"Muted");
    }
    internal bool BeginWeekly()
    {
        if(weeklyBusy)throw new IOException("正在处理周报，请稍候。");
        if(!TrySaveLibrary())throw new IOException("请先处理资料库中未保存的输入或冲突，再汇总周报。");
        if(!CheckDay()||!TrySave())throw new IOException("请先处理当前日记的保存或冲突，再汇总周报。");
        weeklyBusy=true;return true;
    }
    internal void EndWeekly()=>weeklyBusy=false;
    internal void RecordWeekly(WeeklyResult result,DateTimeOffset? now=null)
    {
        string verb=result.Action=="unchanged"?"内容未变":result.MergeRequired?"另存待合并":result.Action=="created"?"已生成":"已更新";
        var settings=WeeklySettings.Load(config);
        settings.LastOutput=result.Path;settings.LastMessage=$"{result.Week} · {verb} · {result.ValidNotes} 篇日记 / {result.Excerpts} 条摘录";
        if(result.Week==WeeklyWindow.Latest(now??DateTimeOffset.Now,settings).Id&&SetupService.IsUnder(WeeklyReport.OutputRoot(config,settings.Folder),result.Path))settings.LastCompletedWeek=result.Week;
        try{settings.Save(config);}catch(Exception e){SetWeeklyMessage("周报已保存，但运行记录未保存："+e.Message,true);throw new IOException("周报已保存至 "+result.Path+"，但运行记录保存失败："+e.Message,e);}
        SetWeeklyMessage(settings.LastMessage);
    }
    private void TickWeekly()
    {
        if(!WeeklyTimerEnabled||exiting||weeklyBusy||LibraryHasPendingInput||composing||session.Dirty||session.IsConflict||draftNeeded||DateTime.UtcNow<nextWeeklyCheck)return;
        nextWeeklyCheck=DateTime.UtcNow.AddSeconds(15);
        _=RunWeeklyAutoAsync(DateTimeOffset.Now);
    }
    public async Task<bool> RunWeeklyAutoAsync(DateTimeOffset now)
    {
        if(weeklyBusy||exiting||session.IsConflict)return false;
        bool acquired=false;
        try
        {
            var settings=WeeklySettings.Load(config);if(!settings.Due(now))return false;
            acquired=BeginWeekly();SetWeeklyMessage("正在汇总最近一期周报…");
            var preview=await Task.Run(()=>WeeklyReport.Preview(config,WeeklyWindow.Latest(now,settings),settings.Folder));
            var result=await Task.Run(()=>WeeklyReport.Save(preview));
            RecordWeekly(result,now);return true;
        }
        catch(Exception e){nextWeeklyCheck=DateTime.UtcNow.AddMinutes(5);SetWeeklyMessage("周报自动生成失败，原日记保留："+e.Message,true);return false;}
        finally{if(acquired)weeklyBusy=false;}
    }
}

public sealed class WeeklyPanel
{
    private readonly MainWindow owner;
    private readonly CardConfig config;
    public Window Window{get;}
    public DatePicker WeekPicker{get;}=new(){Width=160};
    public TextBox FolderInput{get;}=new(){MinWidth=160};
    public ComboBox DayChoice{get;}=new(){Width=86};
    public ComboBox HourChoice{get;}=new(){Width=70};
    public ComboBox MinuteChoice{get;}=new(){Width=70};
    public CheckBox AutoCheck{get;}=new(){VerticalAlignment=VerticalAlignment.Center};
    public TextBox PreviewBox{get;}=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,FontSize=14};
    public TextBlock Summary{get;}=new(){TextWrapping=TextWrapping.Wrap};
    public TextBlock Message{get;}=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
    public Button PreviewButton{get;}=new(){Content="预览周报"};
    public Button GenerateButton{get;}=new(){Content="生成／更新周报",IsEnabled=false};
    public Button SaveSettingsButton{get;}=new(){Content="保存周报设置"};
    public Button OpenButton{get;}=new(){Content="打开已保存周报",IsEnabled=false};
    public WeeklyPreview? CurrentPreview{get;private set;}
    public bool IsBusy{get;private set;}
    private readonly Button current=new(){Content="本周"},latest=new(){Content="最近一期"},export=new(){Content="另存预览草稿",IsEnabled=false};
    private string? savedPath;
    public WeeklyPanel(MainWindow owner,CardConfig config,Window window)
    {
        this.owner=owner;this.config=config;Window=window;
        WeeklyControlStyles.Apply(window);
        WeekPicker.SetResourceReference(DatePicker.CalendarStyleProperty,typeof(Calendar));Window.MinHeight=600;
        var layout=new DockPanel{Margin=new Thickness(22)};
        var top=new StackPanel();top.Children.Add(new TextBlock{Text="把每日记录整理成工作周报",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        var help=new TextBlock{Text="本地按原文摘编、分栏去重，保留任务状态和日记来源链接。不会修改原日记，不调用 AI 或上传。",TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new Thickness(0,0,0,12)};help.SetResourceReference(TextBlock.ForegroundProperty,"Muted");top.Children.Add(help);
        var dates=new WrapPanel{Margin=new Thickness(0,0,0,8)};dates.Children.Add(new TextBlock{Text="选择周期内日期",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)});dates.Children.Add(WeekPicker);dates.Children.Add(current);dates.Children.Add(latest);top.Children.Add(dates);
        top.Children.Add(Summary);
        var path=new DockPanel{Margin=new Thickness(0,10,0,8)};var label=new TextBlock{Text="周报目录",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};DockPanel.SetDock(label,Dock.Left);path.Children.Add(label);path.Children.Add(FolderInput);top.Children.Add(path);
        var autoLine=new WrapPanel();AutoCheck.Width=24;autoLine.Children.Add(AutoCheck);autoLine.Children.Add(new TextBlock{Text="按指定时间自动生成（仅此日记库）",VerticalAlignment=VerticalAlignment.Center});top.Children.Add(autoLine);
        System.Windows.Automation.AutomationProperties.SetName(AutoCheck,"按指定时间自动生成周报");
        var scheduleRow=new WrapPanel{Margin=new Thickness(0,7,0,0)};
        foreach(string day in new[]{"周一","周二","周三","周四","周五","周六","周日"})DayChoice.Items.Add(day);
        for(int hour=0;hour<=24;hour++)HourChoice.Items.Add(hour.ToString("00"));
        for(int minute=0;minute<60;minute++)MinuteChoice.Items.Add(minute.ToString("00"));
        DayChoice.SelectedIndex=4;HourChoice.SelectedIndex=12;MinuteChoice.SelectedIndex=0;
        scheduleRow.Children.Add(DayChoice);scheduleRow.Children.Add(HourChoice);scheduleRow.Children.Add(new TextBlock{Text=":",VerticalAlignment=VerticalAlignment.Center});scheduleRow.Children.Add(MinuteChoice);
        scheduleRow.Children.Add(new TextBlock{Text="  北京时间 · 24:00 即次日 00:00",VerticalAlignment=VerticalAlignment.Center});top.Children.Add(scheduleRow);
        HourChoice.SelectionChanged+=(_,_)=>{if(HourChoice.SelectedIndex==24)MinuteChoice.SelectedIndex=0;MinuteChoice.IsEnabled=!IsBusy&&HourChoice.SelectedIndex!=24;};
        var autoHelp=new TextBlock{Text="自动生成默认关闭；卡片运行或下次打开补跑最近一期，退出期间不运行。每期汇总周六至下周五的 7 天；按生成时点最近的截止周五确定周期。原库计划任务不变。",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,7)};autoHelp.SetResourceReference(TextBlock.ForegroundProperty,"Muted");top.Children.Add(autoHelp);
        var controls=new WrapPanel();controls.Children.Add(PreviewButton);controls.Children.Add(GenerateButton);controls.Children.Add(SaveSettingsButton);top.Children.Add(controls);top.Children.Add(Message);DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);
        var bottom=new WrapPanel{Margin=new Thickness(0,10,0,0)};bottom.Children.Add(OpenButton);bottom.Children.Add(export);var close=new Button{Content="关闭"};close.Click+=(_,_)=>window.Close();bottom.Children.Add(close);DockPanel.SetDock(bottom,Dock.Bottom);layout.Children.Add(bottom);layout.Children.Add(PreviewBox);window.Content=layout;
        try{var settings=WeeklySettings.Load(config);FolderInput.Text=settings.Folder;AutoCheck.IsChecked=settings.AutoEnabled;settings.ValidateTime();DayChoice.SelectedIndex=settings.Weekday;HourChoice.SelectedIndex=settings.Hour;MinuteChoice.SelectedIndex=settings.Minute;Message.Text=settings.LastMessage;savedPath=settings.LastOutput;OpenButton.IsEnabled=savedPath.Length>0&&File.Exists(savedPath);}
        catch(Exception e){FolderInput.Text="周报";Message.Text=e.Message;Message.SetResourceReference(TextBlock.ForegroundProperty,"Danger");}
        WeekPicker.SelectedDate=WeeklyWindow.Latest(DateTimeOffset.Now,ReadSchedule()).Friday.ToDateTime(TimeOnly.MinValue);
        WeekPicker.SelectedDateChanged+=(_,_)=>Invalidate();FolderInput.TextChanged+=(_,_)=>Invalidate();
        current.Click+=(_,_)=>WeekPicker.SelectedDate=DateTimeOffset.Now.ToOffset(WeeklyWindow.Beijing).Date;
        latest.Click+=(_,_)=>WeekPicker.SelectedDate=WeeklyWindow.Latest(DateTimeOffset.Now,ReadSchedule()).Friday.ToDateTime(TimeOnly.MinValue);
        PreviewButton.Click+=async(_,_)=>await PreviewAsync();GenerateButton.Click+=async(_,_)=>await GenerateAsync();
        SaveSettingsButton.Click+=(_,_)=>SaveSettings();
        OpenButton.Click+=(_,_)=>{try{if(savedPath!=null&&File.Exists(savedPath))owner.OpenWeeklyDocument(savedPath);}catch(Exception e){Error(e);}};
        export.Click+=(_,_)=>{if(CurrentPreview==null)return;var picker=new Microsoft.Win32.SaveFileDialog{Title="另存周报预览草稿",FileName=CurrentPreview.Window.Id+"-周报草稿.md",Filter="Markdown|*.md"};if(picker.ShowDialog(window)==true)try{File.WriteAllText(picker.FileName,CurrentPreview.Markdown,DiarySession.Utf8);Message.Text="预览草稿已另存："+picker.FileName;}catch(Exception e){Error(e);}};
        window.Closing+=(_,e)=>{if(IsBusy){e.Cancel=true;Message.Text="正在处理周报，请稍候再关闭。";}};
        UpdateSummary();
    }
    private WeeklySettings ReadSchedule()=>new(){Weekday=DayChoice.SelectedIndex,Hour=HourChoice.SelectedIndex,Minute=MinuteChoice.SelectedIndex};
    private WeeklyWindow Selected()
    {
        if(WeekPicker.SelectedDate==null)throw new IOException("请选择本周内的一天。");
        return WeeklyWindow.ForDate(DateOnly.FromDateTime(WeekPicker.SelectedDate.Value),DateTimeOffset.Now);
    }
    private void UpdateSummary(){if(WeekPicker.SelectedDate==null){Summary.Text="请选择日期。";return;}var w=Selected();Summary.Text=$"{w.Id} · {w.Start} 至 {w.End}（周六至下周五）";}
    private void Invalidate(){CurrentPreview=null;GenerateButton.IsEnabled=false;export.IsEnabled=false;PreviewBox.Text="日期或目录已改变，请重新预览。";UpdateSummary();}
    private void Busy(bool busy)
    {
        IsBusy=busy;WeekPicker.IsEnabled=FolderInput.IsEnabled=AutoCheck.IsEnabled=current.IsEnabled=latest.IsEnabled=SaveSettingsButton.IsEnabled=PreviewButton.IsEnabled=!busy;
        DayChoice.IsEnabled=HourChoice.IsEnabled=!busy;MinuteChoice.IsEnabled=!busy&&HourChoice.SelectedIndex!=24;
        GenerateButton.IsEnabled=!busy&&CurrentPreview!=null;export.IsEnabled=!busy&&CurrentPreview!=null;
    }
    private void Error(Exception e){Message.Text=e.Message;Message.SetResourceReference(TextBlock.ForegroundProperty,"Danger");}
    public bool SaveSettings()
    {
        try
        {
            WeeklyReport.OutputRoot(config,FolderInput.Text);
            var schedule=ReadSchedule();schedule.ValidateTime();
            var old=WeeklySettings.Load(config);
            if(old.Folder!=FolderInput.Text.Trim()||old.Weekday!=schedule.Weekday||old.Hour!=schedule.Hour||old.Minute!=schedule.Minute)old.LastCompletedWeek="";
            old.Weekday=schedule.Weekday;old.Hour=schedule.Hour;old.Minute=schedule.Minute;
            old.Folder=FolderInput.Text.Trim();old.AutoEnabled=AutoCheck.IsChecked==true;old.Save(config);
            Message.Text=old.AutoEnabled?"已保存："+old.ScheduleLabel+"，错过后补跑最近一期。":"已保存：仅手动生成周报；时间设置已保留。";
            Message.SetResourceReference(TextBlock.ForegroundProperty,"Accent");return true;
        }
        catch(Exception e){Error(e);return false;}
    }
    public async Task<bool> PreviewAsync()
    {
        if(IsBusy||owner.WeeklyBusy)return false;
        bool acquired=false;try
        {
            var selected=Selected();string folder=FolderInput.Text;
            acquired=owner.BeginWeekly();Busy(true);Message.Text="正在读取日记并生成预览…";Message.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            CurrentPreview=await Task.Run(()=>WeeklyReport.Preview(config,selected,folder));
            PreviewBox.Text=CurrentPreview.Markdown;
            Message.Text=$"{CurrentPreview.ValidNotes} 篇有效日记 · {CurrentPreview.Excerpts} 条摘录 · 排除 {CurrentPreview.EmptyNotes} 篇空模板 · 日期不明 {CurrentPreview.InvalidDates} 篇\n保存位置："+CurrentPreview.Path+(CurrentPreview.MergeRequired?"\n原周报自动区标记异常，将另存待合并文件，不覆盖原件。":"\n仅更新 AUTO 自动区，外部手工内容保留。");
            return true;
        }
        catch(Exception e){CurrentPreview=null;Error(e);return false;}
        finally{Busy(false);if(acquired)owner.EndWeekly();}
    }
    public async Task<bool> GenerateAsync()
    {
        if(IsBusy||owner.WeeklyBusy||CurrentPreview==null)return false;
        bool acquired=false;try
        {
            string folder=FolderInput.Text;var preview=CurrentPreview;
            acquired=owner.BeginWeekly();Busy(true);Message.Text="正在保存周报…";
            var fresh=await Task.Run(()=>WeeklyReport.Preview(config,preview.Window,folder));
            if(fresh.Fingerprint!=preview.Fingerprint){CurrentPreview=fresh;PreviewBox.Text=fresh.Markdown;Message.Text="日记在预览后发生变化，已刷新预览；请检查后再次生成。";return false;}
            var result=await Task.Run(()=>WeeklyReport.Save(preview));savedPath=result.Path;OpenButton.IsEnabled=true;
            owner.RecordWeekly(result);Message.Text=owner.WeeklyMessage+"\n"+result.Path;Message.SetResourceReference(TextBlock.ForegroundProperty,"Accent");return true;
        }
        catch(Exception e){Error(e);return false;}
        finally{Busy(false);if(acquired)owner.EndWeekly();}
    }
}
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace DailyDiaryCard;

public sealed class ModeSetupWindow:Window
{
    private readonly string configFile,theme;
    private readonly Func<VaultPlan,bool> apply;
    private readonly CardConfig? current;
    public RadioButton ObsidianChoice{get;}=new(){GroupName="DiaryCardMode",HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(6)};
    public RadioButton StandaloneChoice{get;}=new(){GroupName="DiaryCardMode",HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(6)};
    public Button ContinueButton{get;}=new(){Content="继续",MinWidth=112,Padding=new Thickness(18,9,18,9),IsDefault=true,IsEnabled=false};
    public Button CancelButton{get;}=new(){Content="取消",MinWidth=80,Margin=new Thickness(0,0,10,0)};
    public TextBlock ErrorText{get;}=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)};
    public CheckBox? StartupChoice{get;private set;}
    public bool StartupRequested=>StartupChoice?.IsChecked==true;
    public bool Accepted{get;private set;}
    public VaultPlan? AcceptedPlan{get;private set;}
    public ModeSetupWindow(string file,Func<VaultPlan,bool> onApply,CardConfig? current=null,string theme="fluent",string? startupError=null)
    {
        configFile=Path.GetFullPath(file);this.current=current;this.theme=theme;apply=onApply;
        Title=current==null?"欢迎使用 DiaryCard":"选择使用方式";
        Width=720;Height=Math.Min(610,SystemParameters.WorkArea.Height-30);MinWidth=520;MinHeight=470;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;WindowStyle=WindowStyle.ToolWindow;ResizeMode=ResizeMode.CanResizeWithGrip;
        foreach(var pair in Themes.DialogBrushes(theme))Resources[pair.Key]=pair.Value;
        SetResourceReference(BackgroundProperty,"WindowBg");SetResourceReference(ForegroundProperty,"Text");
        SourceInitialized+=(_,_)=>{DockNative.SettingsCaption(this);WindowEffects.ApplyDialog(this,theme=="night");};
        var layout=new DockPanel{Margin=new Thickness(28)};
        var intro=new StackPanel();
        intro.Children.Add(new TextBlock{Text=current==null?"你是否使用 Obsidian？":"选择适合你的使用方式",FontSize=27,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
        var introText=new TextBlock{Text=current==null?"选择一次，即可开始写日记。两种方式都支持任务、主题和周报，正文始终保存在本地。":"切换前会保存当前输入。原有日记留在原处，不自动搬移或覆盖。",TextWrapping=TextWrapping.Wrap,FontSize=14,Margin=new Thickness(0,0,0,20)};
        introText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");intro.Children.Add(introText);
        DockPanel.SetDock(intro,Dock.Top);layout.Children.Add(intro);
        var footer=new StackPanel();if(current==null){footer.Children.Add(StartupService.FirstChoiceRow(configFile,out var startupChoice));StartupChoice=startupChoice;}ErrorText.SetResourceReference(TextBlock.ForegroundProperty,"Danger");footer.Children.Add(ErrorText);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(CancelButton);buttons.Children.Add(ContinueButton);footer.Children.Add(buttons);
        var note=new TextBlock{Text="以后可在设置中更换使用方式和保存位置。",FontSize=12,Margin=new Thickness(0,15,0,0)};note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");footer.Children.Add(note);
        DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        var options=new StackPanel();
        options.Children.Add(ChoiceCard(ObsidianChoice,"使用 Obsidian","我有 Obsidian，继续使用自己的日记库","沿用已有模板、记录目录和命名规则，也可以新建日记库。"));
        options.Children.Add(ChoiceCard(StandaloneChoice,"独立使用 DiaryCard","我没有 Obsidian，直接在软件内使用","创建本地资料夹，在软件内写日记、查历史、编辑模板和汇总周报。"));
        layout.Children.Add(new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        Content=layout;
        ObsidianChoice.Checked+=(_,_)=>ContinueButton.IsEnabled=true;StandaloneChoice.Checked+=(_,_)=>ContinueButton.IsEnabled=true;
        System.Windows.Automation.AutomationProperties.SetName(ObsidianChoice,"使用 Obsidian");
        System.Windows.Automation.AutomationProperties.SetName(StandaloneChoice,"独立使用 DiaryCard");
        ContinueButton.Click+=(_,_)=>ContinueSelection();CancelButton.Click+=(_,_)=>Close();
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
        if(current!=null){if(current.IsStandalone)StandaloneChoice.IsChecked=true;else ObsidianChoice.IsChecked=true;}
        if(startupError!=null)ErrorText.Text="原配置暂不可用："+startupError+"\n请选择使用方式后修正；取消会保留原配置。";
    }
    private static Border ChoiceCard(RadioButton choice,string title,string subtitle,string details)
    {
        var content=new StackPanel{Margin=new Thickness(9,0,0,0)};
        content.Children.Add(new TextBlock{Text=title,FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        content.Children.Add(new TextBlock{Text=subtitle,FontSize=14,Margin=new Thickness(0,0,0,6)});
        var detail=new TextBlock{Text=details,FontSize=12,TextWrapping=TextWrapping.Wrap};detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(detail);choice.Content=content;
        var card=new Border{Child=choice,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Padding=new Thickness(14),Margin=new Thickness(0,0,0,14)};
        card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Line");
        choice.Checked+=(_,_)=>card.SetResourceReference(Border.BorderBrushProperty,"Accent");
        choice.Unchecked+=(_,_)=>card.SetResourceReference(Border.BorderBrushProperty,"Line");
        return card;
    }
    public Window CreateSelectedWindow()
    {
        if(ObsidianChoice.IsChecked!=true&&StandaloneChoice.IsChecked!=true)throw new IOException("请先选择一种使用方式。");
        Window child=ObsidianChoice.IsChecked==true
            ?new VaultSetupWindow(configFile,apply,current is {IsStandalone:false}?current:null,theme,dataOverride:current?.UserDataPath)
            :new StandaloneSetupWindow(configFile,apply,current,theme);
        child.Owner=this;child.WindowStartupLocation=WindowStartupLocation.CenterOwner;return child;
    }
    public bool ContinueSelection()
    {
        if(!ContinueButton.IsEnabled)return false;
        ContinueButton.IsEnabled=false;
        try
        {
            var child=CreateSelectedWindow();child.ShowDialog();
            bool accepted=child switch{VaultSetupWindow obs=>obs.Accepted,StandaloneSetupWindow standalone=>standalone.Accepted,_=>false};
            if(!accepted)return false;
            Accepted=true;AcceptedPlan=child is VaultSetupWindow existing?existing.AcceptedPlan:((StandaloneSetupWindow)child).AcceptedPlan;
            Close();return true;
        }
        catch(Exception e){ErrorText.Text=e.Message;return false;}
        finally{ContinueButton.IsEnabled=true;}
    }
}

public sealed class StandaloneSetupWindow:Window
{
    private readonly string configFile,install,data;
    private readonly Func<VaultPlan,bool> apply;
    private bool changing;
    private string? detectedLibrary;
    private readonly TextBlock preview=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    private readonly TextBlock detection=new(){TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,7,0,9)};
    public TextBox LibraryInput{get;}=new();
    public TextBox TemplateInput{get;}=new();
    public TextBox FolderInput{get;}=new(){Text="日记"};
    public TextBox FormatInput{get;}=new(){Text="YYYY/MM/YYYY-MM-DD"};
    public CheckBox UseDefault{get;}=new(){IsChecked=true};
    public TextBlock DefaultTemplateLabel{get;}=new(){Text="使用内置复盘模板",VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    public Button DetectButton{get;}=new(){Content="读取资料夹设置",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,0)};
    public Button ApplyButton{get;}=new(){Content="保存并开始",MinWidth=124,Padding=new Thickness(18,9,18,9),IsDefault=true};
    public Button CancelButton{get;}=new(){Content="返回",MinWidth=80,Margin=new Thickness(0,0,10,0)};
    public TextBlock ErrorText{get;}=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)};
    public CheckBox? StartupChoice{get;private set;}
    public bool StartupRequested=>StartupChoice?.IsChecked==true;
    public bool Accepted{get;private set;}
    public VaultPlan? AcceptedPlan{get;private set;}
    public StandaloneSetupWindow(string file,Func<VaultPlan,bool> onApply,CardConfig? current=null,string theme="fluent",string? startupError=null)
    {
        configFile=Path.GetFullPath(file);install=Path.GetDirectoryName(configFile)!;data=current?.UserDataPath??Path.Combine(install,"data");apply=onApply;
        Title=current?.IsStandalone==true?"本地资料夹设置":"独立使用 DiaryCard";
        Width=710;Height=Math.Min(825,SystemParameters.WorkArea.Height-30);MinWidth=520;MinHeight=470;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;WindowStyle=WindowStyle.ToolWindow;ResizeMode=ResizeMode.CanResizeWithGrip;
        foreach(var pair in Themes.DialogBrushes(theme))Resources[pair.Key]=pair.Value;
        SetResourceReference(BackgroundProperty,"WindowBg");SetResourceReference(ForegroundProperty,"Text");
        SourceInitialized+=(_,_)=>{DockNative.SettingsCaption(this);WindowEffects.ApplyDialog(this,theme=="night");};
        foreach(var input in new[]{LibraryInput,TemplateInput,FolderInput,FormatInput}){input.MinHeight=40;input.FontSize=14;input.Padding=new Thickness(10,6,10,6);}
        var layout=new DockPanel{Margin=new Thickness(26)};
        var intro=new StackPanel();intro.Children.Add(new TextBlock{Text="让日记只属于你的电脑",FontSize=25,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,7)});
        var help=new TextBlock{Text="无需安装其他笔记软件。日记、任务、模板和周报都可以在 DiaryCard 内完成。",FontSize=14,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};help.SetResourceReference(TextBlock.ForegroundProperty,"Muted");intro.Children.Add(help);
        DockPanel.SetDock(intro,Dock.Top);layout.Children.Add(intro);
        var footer=new StackPanel();ErrorText.SetResourceReference(TextBlock.ForegroundProperty,"Danger");footer.Children.Add(ErrorText);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(CancelButton);buttons.Children.Add(ApplyButton);footer.Children.Add(buttons);
        DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        var stack=new StackPanel();
        Label(stack,"本地资料夹");stack.Children.Add(Row(LibraryInput,"选择位置",BrowseLibrary));stack.Children.Add(DetectButton);detection.SetResourceReference(TextBlock.ForegroundProperty,"Muted");stack.Children.Add(detection);
        Label(stack,"日记模板");var defaultRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,6)};UseDefault.Margin=new Thickness(0,0,6,0);defaultRow.Children.Add(UseDefault);defaultRow.Children.Add(DefaultTemplateLabel);stack.Children.Add(defaultRow);
        DefaultTemplateLabel.MouseLeftButtonDown+=(_,_)=>UseDefault.IsChecked=UseDefault.IsChecked!=true;
        System.Windows.Automation.AutomationProperties.SetName(UseDefault,"使用内置复盘模板");
        stack.Children.Add(Row(TemplateInput,"选择模板",BrowseTemplate));
        var advanced=new StackPanel();Label(advanced,"日记保存子目录");advanced.Children.Add(FolderInput);Label(advanced,"文件命名规则");advanced.Children.Add(FormatInput);
        var hint=new TextBlock{Text="支持 YYYY、MM、DD。保留默认值即可按年月整理日记。",FontSize=12,Margin=new Thickness(0,6,0,0)};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");advanced.Children.Add(hint);
        stack.Children.Add(new Expander{Header="保存规则",Content=advanced,IsExpanded=false,Margin=new Thickness(0,14,0,0)});
        preview.SetResourceReference(TextBlock.ForegroundProperty,"Muted");stack.Children.Add(preview);
        var local=new TextBlock{Text="新位置只在确认后创建；已有 DiaryCard 资料夹可直接读取。其他非空文件夹不会被覆盖。",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,8)};local.SetResourceReference(TextBlock.ForegroundProperty,"Muted");stack.Children.Add(local);
        layout.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Content=layout;
        changing=true;LibraryInput.Text=Path.Combine(install,"Diary");
        if(current?.IsStandalone==true)
        {
            LibraryInput.Text=current.VaultPath;TemplateInput.Text=current.TemplatePath;FolderInput.Text=Path.GetRelativePath(current.VaultPath,current.DiaryFolder);FormatInput.Text=current.DateFormat;UseDefault.IsChecked=current.UseBundledTemplate;detectedLibrary=current.VaultPath;
        }
        changing=false;
        UseDefault.Checked+=(_,_)=>UpdateTemplate();UseDefault.Unchecked+=(_,_)=>UpdateTemplate();
        LibraryInput.TextChanged+=(_,_)=>{if(!changing){detectedLibrary=null;UpdatePreview();}};
        LibraryInput.LostKeyboardFocus+=(_,_)=>{if(!changing&&HasManifest()&&detectedLibrary!=LibraryInput.Text)Detect();};
        foreach(var input in new[]{TemplateInput,FolderInput,FormatInput})input.TextChanged+=(_,_)=>UpdatePreview();
        DetectButton.Click+=(_,_)=>Detect();CancelButton.Click+=(_,_)=>Close();ApplyButton.Click+=(_,_)=>ApplySelection();
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
        ApplyButton.Content=current==null?"保存并开始":"保存并切换";
        UpdateTemplate();if(startupError!=null)ErrorText.Text="原设置暂不可用："+startupError+"\n可重新选择资料夹；返回会保留原配置。";
    }
    private static void Label(StackPanel stack,string text)=>stack.Children.Add(new TextBlock{Text=text,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,13,0,6)});
    private static Grid Row(TextBox input,string label,Action action)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});input.MinWidth=100;grid.Children.Add(input);
        var button=new Button{Content=label,MinWidth=96,Margin=new Thickness(8,0,0,0)};button.Click+=(_,_)=>action();Grid.SetColumn(button,1);grid.Children.Add(button);return grid;
    }
    private bool HasManifest()
    {
        try{return !string.IsNullOrWhiteSpace(LibraryInput.Text)&&File.Exists(Path.Combine(Path.GetFullPath(LibraryInput.Text.Trim(),install),"diarycard-library.json"));}catch{return false;}
    }
    private void BrowseLibrary()
    {
        using var picker=new Forms.FolderBrowserDialog{Description="选择本地日记资料夹，或新建一个空文件夹",UseDescriptionForTitle=true,ShowNewFolderButton=true,SelectedPath=Directory.Exists(LibraryInput.Text)?LibraryInput.Text:install};
        if(picker.ShowDialog()==Forms.DialogResult.OK){LibraryInput.Text=picker.SelectedPath;Detect();}
    }
    private void BrowseTemplate()
    {
        var picker=new OpenFileDialog{Title="选择本地日记模板",Filter="Markdown 模板 (*.md)|*.md",CheckFileExists=true};
        if(picker.ShowDialog(this)==true){UseDefault.IsChecked=false;TemplateInput.Text=picker.FileName;UpdatePreview();}
    }
    private void UpdateTemplate(){TemplateInput.IsEnabled=UseDefault.IsChecked!=true;UpdatePreview();}
    public void Detect()
    {
        try
        {
            if(string.IsNullOrWhiteSpace(LibraryInput.Text))throw new IOException("请选择本地资料夹的位置。");
            var path=Path.GetFullPath(LibraryInput.Text.Trim(),install);changing=true;LibraryInput.Text=path;
            if(!File.Exists(Path.Combine(path,"diarycard-library.json")))
            {
                if(Directory.Exists(path)&&Directory.EnumerateFileSystemEntries(path).Any())throw new IOException("所选位置已有其他文件。请选择空位置或已有的 DiaryCard 资料夹。");
                detection.Text="此位置尚未创建资料夹，将在确认后新建。";detectedLibrary=path;ErrorText.Text="";return;
            }
            var known=SetupService.InspectStandalone(path);
            TemplateInput.Text=Path.GetFullPath(known.Template,path);FolderInput.Text=known.Folder;FormatInput.Text=known.Format;UseDefault.IsChecked=false;
            detectedLibrary=path;detection.Text="已读取本地资料夹设置。";ErrorText.Text="";
        }
        catch(Exception e){detection.Text="未能读取资料夹设置；原有文件保持不变。";ErrorText.Text=e.Message;}
        finally{changing=false;UpdateTemplate();UpdatePreview();}
    }
    public VaultPlan Selection()=>SetupService.PlanStandalone(configFile,LibraryInput.Text,TemplateInput.Text,UseDefault.IsChecked==true,FolderInput.Text,FormatInput.Text,data);
    private void UpdatePreview()
    {
        if(changing)return;
        try{var plan=Selection();preview.Text="预览 · "+TemplateReconciler.Parse(plan.TemplateText).Count+" 个栏目\n今天保存至："+plan.Config.PathFor(DateOnly.FromDateTime(DateTime.Now));}
        catch{preview.Text="选择位置后，可预览栏目和当天日记的位置。";}
    }
    public bool ApplySelection()
    {
        try
        {
            if(HasManifest()&&detectedLibrary!=LibraryInput.Text)Detect();
            var plan=Selection();ApplyButton.IsEnabled=false;
            if(!apply(plan)){ErrorText.Text="尚未切换。请处理卡片的保存提示后重试；当前输入和设置仍保留。";return false;}
            AcceptedPlan=plan;Accepted=true;Close();return true;
        }
        catch(Exception e){ErrorText.Text=e.Message;return false;}
        finally{ApplyButton.IsEnabled=true;}
    }
}
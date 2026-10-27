using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace DailyDiaryCard;

public sealed class VaultSetupWindow:Window
{
    private readonly string configFile,install,data;
    private readonly Func<VaultPlan,bool> apply;
    private readonly TextBlock preview=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private readonly TextBlock detection=new(){TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,6,0,8)};
    private string? detectedVault;
    private string previousVault="",previousTemplate="",previousFolder="日记",previousFormat="YYYY/MM/YYYY-MM-DD";
    private bool previousDefault=true;
    private Button? templatePicker;
    private bool changing;
    public RadioButton ExistingChoice{get;}=new(){Content="我已有 Obsidian 日记库",IsChecked=true,Margin=new Thickness(0,7,0,9)};
    public RadioButton NewChoice{get;}=new(){Content="我没有，创建一个本地日记库",Margin=new Thickness(0,3,0,12)};
    public TextBox VaultInput{get;}=new();
    public TextBox TemplateInput{get;}=new();
    public TextBox FolderInput{get;}=new(){Text="日记"};
    public TextBox FormatInput{get;}=new(){Text="YYYY/MM/YYYY-MM-DD"};
    public CheckBox UseDefault{get;}=new(){Content="使用内置复盘模板",IsChecked=true,Margin=new Thickness(0,8,0,8)};
    public TextBlock DefaultTemplateLabel{get;}=new(){Text="使用内置复盘模板",VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    public Button DetectButton{get;}=new(){Content="读取仓库设置",Margin=new Thickness(0,5,0,0),HorizontalAlignment=HorizontalAlignment.Left};
    public Button ApplyButton{get;}=new(){Content="保存并开始",MinWidth=124,Padding=new Thickness(18,9,18,9),IsDefault=true};
    public Button CancelButton{get;}=new(){Content="取消",MinWidth=80,Margin=new Thickness(0,0,10,0)};
    public TextBlock ErrorText{get;}=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)};
    public bool Accepted{get;private set;}
    public VaultPlan? AcceptedPlan{get;private set;}
    public VaultSetupWindow(string file,Func<VaultPlan,bool> onApply,CardConfig? current=null,string theme="fluent",string? startupError=null,string? dataOverride=null)
    {
        configFile=Path.GetFullPath(file);install=Path.GetDirectoryName(configFile)!;data=current?.UserDataPath??dataOverride??Path.Combine(install,"data");apply=onApply;
        Title=current==null?"欢迎使用 DiaryCard · 设置日记库":"日记库设置";
        Width=690;Height=Math.Min(810,SystemParameters.WorkArea.Height-30);MinWidth=520;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterScreen;WindowStyle=WindowStyle.ToolWindow;ResizeMode=ResizeMode.CanResizeWithGrip;
        foreach(var pair in Themes.DialogBrushes(theme))Resources[pair.Key]=pair.Value;
        SetResourceReference(BackgroundProperty,"WindowBg");SetResourceReference(ForegroundProperty,"Text");
        SourceInitialized+=(_,_)=>{DockNative.SettingsCaption(this);WindowEffects.ApplyDialog(this,theme=="night");};
        foreach(var input in new[]{VaultInput,TemplateInput,FolderInput,FormatInput}){input.Height=40;input.FontSize=14;input.Padding=new Thickness(10,6,10,6);}
        var layout=new DockPanel{Margin=new Thickness(26)};
        var title=new StackPanel();title.Children.Add(new TextBlock{Text=current==null?"让日记找到自己的位置":"选择你的日记库",FontSize=25,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
        title.Children.Add(new TextBlock{Text=current==null?"已有仓库继续使用；也可以从一个新的本地仓库开始。":"切换前保存当前输入，并自动重新打开卡片。原笔记保留原处。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});DockPanel.SetDock(title,Dock.Top);layout.Children.Add(title);
        var footer=new StackPanel();ErrorText.SetResourceReference(TextBlock.ForegroundProperty,"Danger");footer.Children.Add(ErrorText);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(CancelButton);buttons.Children.Add(ApplyButton);footer.Children.Add(buttons);DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        var stack=new StackPanel();stack.Children.Add(ExistingChoice);stack.Children.Add(NewChoice);
        Label(stack,"日记库文件夹");stack.Children.Add(Row(VaultInput,"选择文件夹",BrowseVault));stack.Children.Add(DetectButton);detection.SetResourceReference(TextBlock.ForegroundProperty,"Muted");stack.Children.Add(detection);
        Label(stack,"模板");var defaultRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,6)};UseDefault.Margin=new Thickness(0,0,5,0);defaultRow.Children.Add(UseDefault);defaultRow.Children.Add(DefaultTemplateLabel);DefaultTemplateLabel.MouseLeftButtonDown+=(_,_)=>{if(UseDefault.IsEnabled)UseDefault.IsChecked=UseDefault.IsChecked!=true;};System.Windows.Automation.AutomationProperties.SetName(UseDefault,"使用内置复盘模板");stack.Children.Add(defaultRow);var templateRow=Row(TemplateInput,"选择模板",BrowseTemplate);templatePicker=templateRow.Children.OfType<Button>().Single();stack.Children.Add(templateRow);
        Label(stack,"日记保存目录（相对仓库或绝对路径）");stack.Children.Add(FolderInput);Label(stack,"文件命名规则");stack.Children.Add(FormatInput);stack.Children.Add(new TextBlock{Text="支持 YYYY、MM、DD；例如 YYYY/MM/YYYY-MM-DD 或 YYYY-MM-DD。",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});
        preview.SetResourceReference(TextBlock.ForegroundProperty,"Muted");stack.Children.Add(preview);stack.Children.Add(new TextBlock{Text="所有正文保存在本地。默认附带内置模板及背景，可在主题库更换。",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,8)});
        layout.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Content=layout;
        ExistingChoice.Checked+=(_,_)=>UpdateMode();NewChoice.Checked+=(_,_)=>UpdateMode();
        UseDefault.Checked+=(_,_)=>UpdateTemplate();UseDefault.Unchecked+=(_,_)=>UpdateTemplate();
        DetectButton.Click+=(_,_)=>Detect();VaultInput.LostKeyboardFocus+=(_,_)=>{if(!changing&&ExistingChoice.IsChecked==true&&Directory.Exists(VaultInput.Text)&&detectedVault!=VaultInput.Text)Detect();};
        FolderInput.TextChanged+=(_,_)=>UpdatePreview();FormatInput.TextChanged+=(_,_)=>UpdatePreview();TemplateInput.TextChanged+=(_,_)=>UpdatePreview();
        CancelButton.Click+=(_,_)=>Close();PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
        ApplyButton.Content=current==null?"保存并开始":"保存并切换";
        ApplyButton.Click+=(_,_)=>ApplySelection();
        if(current!=null)
        {
            changing=true;VaultInput.Text=current.VaultPath;TemplateInput.Text=current.TemplatePath;FolderInput.Text=Path.GetRelativePath(current.VaultPath,current.DiaryFolder);FormatInput.Text=current.DateFormat;UseDefault.IsChecked=current.UseBundledTemplate;detectedVault=current.VaultPath;changing=false;
        }
        UpdateMode();if(startupError!=null)ErrorText.Text="原配置暂不可用："+startupError+"\n请选择有效地址后重试；取消会保留原配置。";
    }
    private static void Label(StackPanel stack,string text)=>stack.Children.Add(new TextBlock{Text=text,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,13,0,6)});
    private static Grid Row(TextBox input,string label,Action action)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});input.MinWidth=100;grid.Children.Add(input);
        var button=new Button{Content=label,Margin=new Thickness(8,0,0,0),MinWidth=96};button.Click+=(_,_)=>action();Grid.SetColumn(button,1);grid.Children.Add(button);return grid;
    }
    private void UpdateMode()
    {
        if(changing)return;changing=true;bool create=NewChoice.IsChecked==true;
        if(create&&!VaultInput.IsReadOnly){previousVault=VaultInput.Text;previousTemplate=TemplateInput.Text;previousFolder=FolderInput.Text;previousFormat=FormatInput.Text;previousDefault=UseDefault.IsChecked==true;}
        if(!create&&VaultInput.IsReadOnly){VaultInput.Text=previousVault;TemplateInput.Text=previousTemplate;FolderInput.Text=previousFolder;FormatInput.Text=previousFormat;UseDefault.IsChecked=previousDefault;}
        if(create){VaultInput.Text=Path.Combine(install,"DailyNote");UseDefault.IsChecked=true;FolderInput.Text="日记";FormatInput.Text="YYYY/MM/YYYY-MM-DD";detection.Text="将在程序所在文件夹中新建 DailyNote，不覆盖已有内容。";}
        VaultInput.IsReadOnly=create;DetectButton.IsEnabled=!create;UseDefault.IsEnabled=!create;changing=false;UpdateTemplate();UpdatePreview();
    }
    private void UpdateTemplate(){TemplateInput.IsEnabled=UseDefault.IsChecked!=true&&NewChoice.IsChecked!=true;if(templatePicker!=null)templatePicker.IsEnabled=NewChoice.IsChecked!=true;UpdatePreview();}
    private void BrowseVault()
    {
        if(NewChoice.IsChecked==true)return;
        using var picker=new Forms.FolderBrowserDialog{Description="选择 Obsidian 仓库根目录",UseDescriptionForTitle=true,SelectedPath=Directory.Exists(VaultInput.Text)?VaultInput.Text:""};
        if(picker.ShowDialog()==Forms.DialogResult.OK){VaultInput.Text=picker.SelectedPath;Detect();}
    }
    private void BrowseTemplate()
    {
        if(NewChoice.IsChecked==true)return;
        var picker=new OpenFileDialog{Title="选择日记 Markdown 模板",Filter="Markdown 模板 (*.md)|*.md",CheckFileExists=true};
        if(picker.ShowDialog(this)==true){UseDefault.IsChecked=false;TemplateInput.Text=picker.FileName;UpdatePreview();}
    }
    public void Detect()
    {
        try
        {
            if(string.IsNullOrWhiteSpace(VaultInput.Text))throw new IOException("请选择你的日记库文件夹，或选择创建新日记库。");
            var absolute=Path.GetFullPath(VaultInput.Text.Trim(),install);var detected=SetupService.Inspect(absolute);changing=true;VaultInput.Text=absolute;detectedVault=absolute;
            FolderInput.Text=detected.Folder;FormatInput.Text=detected.Format;UseDefault.IsChecked=!detected.HasTemplate;
            TemplateInput.Text=detected.HasTemplate?Path.GetFullPath(detected.Template,VaultInput.Text):"";detection.Text="已读取："+detected.Source;ErrorText.Text="";
        }
        catch(Exception e){detection.Text="无法自动读取，可手动填写下方设置。";ErrorText.Text=e.Message;detectedVault=VaultInput.Text;}
        finally{changing=false;UpdateTemplate();UpdatePreview();}
    }
    public VaultPlan Selection()=>SetupService.Plan(configFile,NewChoice.IsChecked==true,VaultInput.Text,TemplateInput.Text,UseDefault.IsChecked==true,FolderInput.Text,FormatInput.Text,data);
    private void UpdatePreview()
    {
        if(changing)return;
        try{var plan=Selection();preview.Text="预览 · "+TemplateReconciler.Parse(plan.TemplateText).Count+" 个栏目\n今天将保存至："+plan.Config.PathFor(DateOnly.FromDateTime(DateTime.Now));}
        catch{preview.Text="选择地址后，可预览栏目和当天文件位置。";}
    }
    public bool ApplySelection()
    {
        try
        {
            if(ExistingChoice.IsChecked==true&&detectedVault!=VaultInput.Text)Detect();
            var plan=Selection();ApplyButton.IsEnabled=false;
            if(!apply(plan)){ErrorText.Text="未切换。请先处理卡片中的保存问题，当前内容和设置仍保留。";return false;}
            Accepted=true;AcceptedPlan=plan;Close();return true;
        }
        catch(Exception e){ErrorText.Text=e.Message;return false;}
        finally{ApplyButton.IsEnabled=true;}
    }
}
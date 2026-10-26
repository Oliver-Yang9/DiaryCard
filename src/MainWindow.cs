using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DailyDiaryCard;

public sealed class MainWindow : Window
{
    private readonly CardConfig config;
    public bool RestartRequested { get; private set; }
    private readonly Preferences prefs;
    private readonly Func<DateOnly> today;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer;
    private DiarySession session;
    private readonly TextBlock dateLabel=new(), subLabel=new(), status=new(), pathLabel=new(), bannerText=new();
    private readonly Border banner=new(), root=new();
    private readonly BackgroundPlayer wallpaper=new();
    private readonly Border wallpaperVeil=new() {IsHitTestVisible=false,Opacity=0.32};
    private BitmapSource? wallpaperBitmap=>wallpaper.Poster;
    private bool backgroundImporting;
    private int backgroundRevision;
    private readonly EdgeDock docking;
    private MenuItem? edgeMenu;
    private int openMenus;
    private bool nativeMoved;
    private readonly StackPanel sections=new();
    private readonly Dictionary<string,SectionEditor> editors=new();
    private readonly Dictionary<string,Expander> expanders=new();
    private readonly Dictionary<Window,DialogEffectState> settingsDialogs=new();
    private readonly ScrollViewer scroll=new();
    private Button pinButton=null!,hideButton=null!,closeButton=null!,moreButton=null!;
    private MenuItem? pinMenu;
    private ResizeGrip resizeGrip=null!;
    private HwndSource? nativeSource;
    private bool refreshing,syncFailed;
    private DateTime lastEdit=DateTime.MinValue, lastDraft=DateTime.MinValue, lastPoll=DateTime.MinValue;
    private bool composing, exiting, rebuilding, draftNeeded;
    private DateTime nextDayAttempt=DateTime.MinValue;
    private string? templateCandidate;
    private DateTime templateCandidateSince;
    private Expander? extraSections;
    public DiarySession Session => session;
    public string CurrentTheme => prefs.Theme;
    public IReadOnlyDictionary<string,SectionEditor> Editors => editors;
    public string Status => status.Text;
    public int ShowRequests {get;private set;}
    public WindowEffectState Effects {get;private set;}=new(false,false,false,0,"等待窗口初始化");
    public Button HideToTrayButton=>hideButton;
    public Button PinButton=>pinButton;
    public Button CloseProgramButton=>closeButton;
    public Button MoreButton=>moreButton;
    public ContextMenu MoreMenu=>moreButton.ContextMenu!;
    public ResizeGrip ResizeHandle=>resizeGrip;
    public EdgeDock Docking=>docking;
    public bool HasPictureBackground=>wallpaper.HasBackground;
    public string BackgroundFile=>prefs.BackgroundImage;
    public double BackgroundOpacity=>prefs.BackgroundOpacity;
    public double SectionTransparency=>prefs.SectionTransparency;
    internal BackgroundPlayer BackgroundPlayback=>wallpaper;
    internal string BackgroundFailure=>bannerText.Text;
    public bool BlockEdgeCollapse=>composing||session.IsConflict||syncFailed||status.Text.Contains("失败")||openMenus>0||(IsActive&&Keyboard.FocusedElement is TextBoxBase)||OwnedWindows.Cast<Window>().Any(w=>w.IsVisible);
    public MainWindow(CardConfig cfg, Func<DateOnly>? clock=null)
    {
        config=cfg;today=clock??(()=>DateOnly.FromDateTime(DateTime.Now));
        prefs=Preferences.Load(config.UserDataPath);prefs.FontSize=Math.Clamp(prefs.FontSize,14,24);
        prefs.BackgroundOpacity=double.IsFinite(prefs.BackgroundOpacity)?Math.Clamp(prefs.BackgroundOpacity,0.15,1):0.65;
        prefs.SectionTransparency=double.IsFinite(prefs.SectionTransparency)?Math.Clamp(prefs.SectionTransparency,0,0.9):0;
        Themes.Apply(prefs.Theme);
        session=new(config,today());
        Title="日记卡片";Width=Math.Clamp(prefs.Width,380,1600);Height=Math.Clamp(prefs.Height,440,1800);MinWidth=380;MinHeight=440;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;Topmost=prefs.Topmost;
        WindowChrome.SetWindowChrome(this,new WindowChrome {CaptionHeight=52,ResizeBorderThickness=new Thickness(8),GlassFrameThickness=new Thickness(1),CornerRadius=new CornerRadius(12),UseAeroCaptionButtons=false});
        Height=Math.Min(Height,SystemParameters.WorkArea.Height-16);Width=Math.Min(Width,SystemParameters.WorkArea.Width-16);
        if(prefs.Left!=-1&&prefs.Top!=-1&&prefs.Left+Width>SystemParameters.VirtualScreenLeft+30&&prefs.Left<SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-30&&prefs.Top>=SystemParameters.VirtualScreenTop&&prefs.Top<SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-80){Left=prefs.Left;Top=prefs.Top;}
        else WindowStartupLocation=WindowStartupLocation.CenterScreen;
        BuildChrome();BuildSections();
        docking=new EdgeDock(this,prefs.EdgeAutoHide);
        wallpaper.Failed+=message=>{ApplyWindowEffects();ShowBanner(message,false);};
        if(!string.IsNullOrEmpty(prefs.BackgroundImage)&&!LocalBackground.IsVideo(prefs.BackgroundImage))
            try{wallpaper.UseRaster(new RasterBackground(config.UserDataPath,prefs.BackgroundImage));}catch(Exception e){ShowBanner("背景无法载入，已使用主题底色："+e.Message,false);}
        ApplyBackgroundLayers();
        SourceInitialized+=(_,_)=>{
            nativeSource=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            nativeSource?.AddHook(OnNativeMessage);
            ApplyWindowEffects();
        };
        Loaded+=async(_,_)=>{ApplyWindowEffects();if(LocalBackground.IsVideo(prefs.BackgroundImage))await LoadVideoBackground(prefs.BackgroundImage,false);};
        IsVisibleChanged+=(_,_)=>wallpaper.SetRunning(IsVisible&&prefs.DynamicBackgroundPlaying);
        Activated+=(_,_)=>{if(!session.Dirty&&!refreshing)RefreshFromDisk();};
        tray=new Forms.NotifyIcon {Icon=Program.MakeIcon(),Text="日记卡片 · 单击显示，右键菜单",Visible=true};
        tray.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left)Dispatcher.BeginInvoke(ShowCard);};
        tray.DoubleClick+=(_,_)=>Dispatcher.BeginInvoke(ShowCard);
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("显示卡片",null,(_,_)=>Dispatcher.BeginInvoke(ShowCard));
        menu.Items.Add("隐藏卡片",null,(_,_)=>Dispatcher.BeginInvoke(()=>HideCard()));
        menu.Items.Add("主题库",null,(_,_)=>Dispatcher.BeginInvoke(()=>{ShowCard();ShowThemes();}));
        menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出程序",null,(_,_)=>Dispatcher.BeginInvoke(()=>ExitCard()));tray.ContextMenuStrip=menu;
        TextCompositionManager.AddPreviewTextInputStartHandler(this,(_,_)=>composing=true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(this,(_,_)=>composing=true);
        TextCompositionManager.AddPreviewTextInputHandler(this,(_,_)=>composing=false);
        PreviewKeyDown+=(_,e)=>{
            if(e.Key==Key.Escape){HideCard();e.Handled=true;}
            if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.S){TrySave();e.Handled=true;}
            if(Keyboard.Modifiers==ModifierKeys.Control&&(e.Key==Key.Add||e.Key==Key.OemPlus)){SetFont(prefs.FontSize+1);e.Handled=true;}
            if(Keyboard.Modifiers==ModifierKeys.Control&&(e.Key==Key.Subtract||e.Key==Key.OemMinus)){SetFont(prefs.FontSize-1);e.Handled=true;}
        };
        Deactivated+=(_,_)=>{composing=false;TrySave();};
        Closing+=(_,e)=>{if(!exiting){if(!TrySave()){e.Cancel=true;return;}SavePreferences();exiting=true;}};
        Closed+=(_,_)=>{backgroundRevision++;wallpaper.Dispose();docking.Dispose();timer?.Stop();nativeSource?.RemoveHook(OnNativeMessage);tray.Visible=false;tray.Icon?.Dispose();tray.Dispose();};
        timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};timer.Tick+=(_,_)=>Tick();timer.Start();
        if(session.Restored)ShowBanner("已恢复上次未保存的输入；保存前仍会检查外部修改。",false);
        if(session.Error!=null)ShowBanner(session.Error,true);
        else if(session.TemplateNotice.Length>0)ShowBanner("已按新模板更新。"+session.TemplateNotice,false);
        SetIdleStatus();
    }
    private void BuildChrome()
    {
        root.CornerRadius=new CornerRadius(12);root.BorderThickness=new Thickness(1);root.SetResourceReference(Border.BorderBrushProperty,"Line");root.SetResourceReference(Border.BackgroundProperty,"WindowBg");Content=root;
        var layers=new Grid();layers.Children.Add(wallpaper);layers.Children.Add(wallpaperVeil);root.Child=layers;
        root.SizeChanged+=(_,_)=>root.Clip=new RectangleGeometry(new Rect(0,0,root.ActualWidth,root.ActualHeight),12,12);
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});layers.Children.Add(grid);
        var top=new DockPanel {Margin=new Thickness(16,10,12,7),LastChildFill=false,ToolTip="拖动顶部或日期区移动卡片；拖动边缘或右下角调整大小"};
        var brand=new TextBlock {Text="⠿  日记卡片",FontSize=13,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};
        DockPanel.SetDock(brand,Dock.Left);top.Children.Add(brand);
        var actions=new StackPanel {Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);
        WindowChrome.SetIsHitTestVisibleInChrome(actions,true);
        var library=Button("主题库",ShowThemes);library.ToolTip="主题颜色 · 本地图片背景";library.Padding=new Thickness(8,6,8,6);
        actions.Children.Add(library);
        var more=moreButton=Button("•••",()=>{});more.Padding=new Thickness(7,6,7,6);more.ToolTip="更多操作";more.ContextMenu=CreateMenu();more.Click+=(_,_)=>{more.ContextMenu.PlacementTarget=more;more.ContextMenu.Placement=PlacementMode.Bottom;more.ContextMenu.IsOpen=true;};actions.Children.Add(more);
        hideButton=WindowButton("\uE921","隐藏至托盘",()=>HideCard());
        pinButton=WindowButton("\uE718","置顶卡片",ToggleTopmost);
        closeButton=WindowButton("\uE8BB","保存并退出程序",()=>ExitCard());
        actions.Children.Add(hideButton);actions.Children.Add(pinButton);actions.Children.Add(closeButton);UpdatePinButton();top.Children.Add(actions);
        top.MouseLeftButtonDown+=DragCard;grid.Children.Add(top);
        var heading=new StackPanel {Margin=new Thickness(24,12,24,18)};Grid.SetRow(heading,1);dateLabel.FontSize=28;dateLabel.FontWeight=FontWeights.SemiBold;subLabel.FontSize=12;subLabel.Margin=new Thickness(0,7,0,0);subLabel.SetResourceReference(TextBlock.ForegroundProperty,"Muted");heading.Children.Add(dateLabel);heading.Children.Add(subLabel);grid.Children.Add(heading);
        heading.ToolTip="按住日期区域拖动，调整卡片位置";heading.MouseLeftButtonDown+=DragCard;
        banner.Margin=new Thickness(18,0,18,12);banner.Padding=new Thickness(12);banner.CornerRadius=new CornerRadius(8);banner.BorderThickness=new Thickness(1);banner.SetResourceReference(Border.BorderBrushProperty,"Accent");banner.SetResourceReference(Border.BackgroundProperty,"Surface");banner.Visibility=Visibility.Collapsed;Grid.SetRow(banner,2);
        var notice=new StackPanel();bannerText.FontSize=13;bannerText.LineHeight=21;notice.Children.Add(bannerText);
        var noticeActions=new WrapPanel {Margin=new Thickness(0,7,0,0)};noticeActions.Children.Add(Button("重试",()=>{if(!CheckDay())return;if(session.Dirty||draftNeeded)TrySave();else RefreshFromDisk();}));noticeActions.Children.Add(Button("查看与恢复",ShowRecovery));noticeActions.Children.Add(Button("保留草稿并切换今天",()=>SwitchTodayKeepingDraft()));notice.Children.Add(noticeActions);banner.Child=notice;grid.Children.Add(banner);
        scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;scroll.Padding=new Thickness(18,0,18,18);scroll.Content=sections;Grid.SetRow(scroll,3);grid.Children.Add(scroll);
        var footer=new Border {Padding=new Thickness(20,12,20,12),BorderThickness=new Thickness(0,1,0,0)};footer.SetResourceReference(Border.BorderBrushProperty,"Line");footer.SetResourceReference(Border.BackgroundProperty,"Surface");Grid.SetRow(footer,4);
        var footerGrid=new Grid();footerGrid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footerGrid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var foot=new StackPanel();status.FontSize=13;status.SetResourceReference(TextBlock.ForegroundProperty,"Accent");pathLabel.FontSize=11;pathLabel.Margin=new Thickness(0,5,0,0);pathLabel.SetResourceReference(TextBlock.ForegroundProperty,"Muted");foot.Children.Add(status);foot.Children.Add(pathLabel);footerGrid.Children.Add(foot);
        resizeGrip=new ResizeGrip {HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,ToolTip="拖动这里调整卡片大小"};WindowChrome.SetResizeGripDirection(resizeGrip,ResizeGripDirection.BottomRight);Grid.SetColumn(resizeGrip,1);footerGrid.Children.Add(resizeGrip);footer.Child=footerGrid;grid.Children.Add(footer);
    }
    private static Button Button(string label,Action action){var b=new Button {Content=label,FontSize=12};b.Click+=(_,_)=>action();return b;}
    private static Button WindowButton(string glyph,string name,Action action)
    {
        var b=Button("",action);b.Content=new TextBlock {Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontSize=13};b.Padding=new Thickness(7);b.Width=32;b.Height=32;b.ToolTip=name;
        System.Windows.Automation.AutomationProperties.SetName(b,name);return b;
    }
    private void DragCard(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left)return;
        DependencyObject? p=e.OriginalSource as DependencyObject;
        while(p!=null&&p!=sender){if(p is ButtonBase or TextBoxBase)return;p=p is Visual?VisualTreeHelper.GetParent(p):LogicalTreeHelper.GetParent(p);}
        if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
        else {DragMove();SavePreferences();}
    }
    public void ToggleTopmost(){Topmost=!Topmost;prefs.Topmost=Topmost;UpdatePinButton();SavePreferences();}
    private void UpdatePinButton()
    {
        if(pinButton==null)return;
        pinButton.SetResourceReference(Control.BackgroundProperty,Topmost?"Hover":"Surface");
        pinButton.SetResourceReference(Control.BorderBrushProperty,Topmost?"Accent":"Line");
        pinButton.ToolTip=Topmost?"已置顶 · 点击取消":"置顶卡片";
        System.Windows.Automation.AutomationProperties.SetName(pinButton,Topmost?"取消置顶卡片":"置顶卡片");
        if(pinMenu!=null)pinMenu.IsChecked=Topmost;
    }
    private ContextMenu CreateMenu()
    {
        var m=new ContextMenu();
        void Item(string label,Action a){var i=new MenuItem{Header=label};i.Click+=(_,_)=>a();m.Items.Add(i);}
        Item("主题库",ShowThemes);Item("日记库设置…",()=>CreateVaultSettingsDialog().ShowDialog());Item("展开全部栏目",()=>{foreach(var e in expanders.Values)e.IsExpanded=true;});Item("收起全部栏目",()=>{foreach(var e in expanders.Values)e.IsExpanded=false;});
        pinMenu=new MenuItem {Header="始终置顶",IsCheckable=true,IsChecked=Topmost};pinMenu.Click+=(_,_)=>ToggleTopmost();m.Items.Add(pinMenu);
        edgeMenu=new MenuItem {Header="贴边自动收起",IsCheckable=true,IsChecked=prefs.EdgeAutoHide};edgeMenu.Click+=(_,_)=>SetEdgeAutoHide(edgeMenu.IsChecked);m.Items.Add(edgeMenu);
        Item("展开并取消本次贴边",()=>docking.Detach());
        Item("栏目透明度…",()=>CreateTransparencyDialog().ShowDialog());
        m.Opened+=(_,_)=>{Themes.StyleContextMenu(m,prefs.Theme);openMenus++;};m.Closed+=(_,_)=>openMenus=Math.Max(0,openMenus-1);
        m.Items.Add(new Separator());Item("打开当天日记",()=>{if(TrySave()&&File.Exists(session.Target))OpenPath(session.Target);else if(!File.Exists(session.Target))ShowBanner("首次填写后会创建当天日记。",false);});
        Item("打开日记目录",()=>{Directory.CreateDirectory(config.DiaryFolder);OpenPath(config.DiaryFolder);});
        Item("查看与恢复草稿",ShowRecovery);Item("同步当天日记",()=>{CheckDay();RefreshFromDisk();});Item("切换到今天",()=>CheckDay());Item("查看配置",ShowConfig);
        Item("重新读取模板",()=>RefreshTemplateNow());
        m.Items.Add(new Separator());Item("隐藏卡片",()=>HideCard());Item("退出程序",()=>ExitCard());Themes.StyleContextMenu(m,prefs.Theme);return m;
    }
    private void BuildSections(bool preserveView=false)
    {
        var previousEditors=preserveView?new Dictionary<string,SectionEditor>(editors):new();
        var expanded=expanders.ToDictionary(p=>p.Key,p=>p.Value.IsExpanded);
        double offset=scroll.VerticalOffset;bool extraExpanded=extraSections?.IsExpanded==true;
        var focused=preserveView?Keyboard.FocusedElement as TextBox:null;int caret=focused?.SelectionStart??0,length=focused?.SelectionLength??0;
        var focusedEditor=focused==null?null:editors.Values.FirstOrDefault(e=>e.TextInputs.Contains(focused));
        string? focusedTitle=focusedEditor?.Title,oldText=focused?.Text;int focusedIndex=focused==null?-1:focusedEditor?.TextInputs.IndexOf(focused)??-1;
        foreach(var old in expanders.Values)old.Content=null;
        rebuilding=true;editors.Clear();expanders.Clear();sections.Children.Clear();
        var nl=new MarkdownDoc(session.Baseline??session.TemplateText).NewLine;
        int index=0;
        foreach(var t in session.Template)
        {
            var editor=previousEditors.TryGetValue(t.Title,out var retained)?retained:new SectionEditor(t.Title,session.Bodies[t.Title],nl,prefs.FontSize,body=>{if(rebuilding)return;session.Edit(t.Title,body);lastEdit=DateTime.UtcNow;draftNeeded=true;status.Text=session.IsConflict?"冲突待处理 · 输入已保留":"保存中…";status.SetResourceReference(TextBlock.ForegroundProperty,session.IsConflict?"Danger":"Accent");});
            editor.SetBody(session.Bodies[t.Title]);
            var header=new TextBlock {Text=t.Title,FontSize=17,FontWeight=FontWeights.SemiBold};
            var expander=new Expander {Header=header,Content=editor,IsExpanded=preserveView&&expanded.TryGetValue(t.Title,out bool open)?open:index==0};index++;
            expander.SetResourceReference(Control.BackgroundProperty,"SectionSurface");
            editors[t.Title]=editor;expanders[t.Title]=expander;sections.Children.Add(expander);
        }
        extraSections=null;
        if(session.ExtraSections.Count>0)
        {
            var preserved=new TextBox {Text=string.Concat(session.ExtraSections.Select(h=>h.RawHeading+h.Body)),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxHeight=380,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=14};
            extraSections=new Expander {Header="模板外已有内容（已保留）",Content=preserved,IsExpanded=preserveView&&extraExpanded};sections.Children.Add(extraSections);
        }
        dateLabel.Text=session.Date.ToString("M 月 d 日",CultureInfo.GetCultureInfo("zh-CN"));
        subLabel.Text=session.Date.ToString("yyyy 年 · dddd",CultureInfo.GetCultureInfo("zh-CN"))+"   /   "+session.Template.Count+" 个栏目 · 本地保存";
        Title="日记卡片 · "+session.Date.ToString("yyyy-MM-dd");
        pathLabel.Text=Path.GetRelativePath(config.VaultPath,session.Target);pathLabel.ToolTip=session.Target;if(!preserveView)scroll.ScrollToTop();rebuilding=false;
        if(preserveView)Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>{
            scroll.ScrollToVerticalOffset(offset);if(!IsActive||focused==null)return;
            var target=Window.GetWindow(focused)==this?focused:focusedTitle!=null&&editors.TryGetValue(focusedTitle,out var e)&&focusedIndex>=0&&focusedIndex<e.TextInputs.Count?e.TextInputs[focusedIndex]:null;
            if(target==null)return;int start=Math.Min(caret,target.Text.Length);
            if(oldText!=null&&target.Text!=oldText&&caret>0){var context=oldText.Substring(Math.Max(0,caret-40),Math.Min(caret,40));int at=target.Text.IndexOf(context,StringComparison.Ordinal);if(at>=0&&target.Text.IndexOf(context,at+context.Length,StringComparison.Ordinal)<0)start=at+context.Length;}
            target.Focus();target.Select(start,Math.Min(length,target.Text.Length-start));
        });
    }
    private void SyncEditors()
    {
        if((extraSections!=null)!=(session.ExtraSections.Count>0)){BuildSections(true);return;}
        rebuilding=true;foreach(var pair in editors)pair.Value.SetBody(session.Bodies[pair.Key]);
        if(extraSections?.Content is TextBox preserved)preserved.Text=string.Concat(session.ExtraSections.Select(h=>h.RawHeading+h.Body));rebuilding=false;
    }
    private void Tick()
    {
        if(composing)return;
        if(today()!=session.Date){if(DateTime.UtcNow>=nextDayAttempt){nextDayAttempt=DateTime.UtcNow.AddSeconds(5);CheckDay();}return;}
        if(draftNeeded&&(DateTime.UtcNow-lastEdit).TotalMilliseconds>220&&(DateTime.UtcNow-lastDraft).TotalMilliseconds>450)
        {
            try{session.PersistDraft();lastDraft=DateTime.UtcNow;draftNeeded=false;}
            catch(Exception e){ShowBanner("恢复草稿写入失败，输入仍保留在卡片："+e.Message,true);}
        }
        if(session.Dirty&&!session.IsConflict&&(DateTime.UtcNow-lastEdit).TotalMilliseconds>650){TrySave();lastEdit=DateTime.UtcNow;}
        if((DateTime.UtcNow-lastPoll).TotalSeconds>2)
        {
            lastPoll=DateTime.UtcNow;
            RefreshFromDisk(true);
        }
    }
    public bool TrySave()
    {
        if(composing)return false;
        if(!session.Dirty&&!draftNeeded)return true;
        status.Text="保存中…";
        try{if(session.Dirty)session.Save();else session.PersistDraft("没有未保存的改动");draftNeeded=false;SyncEditors();banner.Visibility=Visibility.Collapsed;SetIdleStatus();return true;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            status.Text=session.IsConflict?"检测到外部修改 · 自动保存已暂停":"保存失败 · 输入已保留";
            status.SetResourceReference(TextBlock.ForegroundProperty,"Danger");
            var reason=FriendlyError(e);
            try{session.PersistDraft(reason);reason+="\n恢复草稿已写入本机。";}
            catch(Exception draftError){reason+="\n恢复草稿也未能写入："+draftError.Message+"。请保持窗口打开并复制内容。";}
            ShowBanner(reason,true);if(!IsVisible)ShowCard(false);return false;
        }
    }
    private void SetIdleStatus()
    {
        status.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
        status.Text=session.Dirty?"保存中…":File.Exists(session.Target)?"✓ 已保存 · "+File.GetLastWriteTime(session.Target).ToString("HH:mm:ss"):"尚未填写 · 首次编辑后创建日记";
    }
    private void ShowBanner(string text,bool error){bannerText.Text=text;banner.SetResourceReference(Border.BorderBrushProperty,error?"Danger":"Accent");bannerText.SetResourceReference(TextBlock.ForegroundProperty,error?"Text":"Muted");banner.Visibility=Visibility.Visible;}
    private static string FriendlyError(Exception e)
    {
        if(e is UnauthorizedAccessException)return "没有写入权限，或文件已设为只读。请检查日记文件和目录权限。";
        int code=e.HResult&0xffff;
        if(e is IOException&&(code==32||code==33))return "文件正被其他程序占用，暂时无法保存。请稍后重试。";
        if(e is IOException&&code==112)return "磁盘空间不足，暂时无法保存。请释放空间后重试。";
        if(e is IOException&&code==19)return "目标磁盘禁止写入，请检查磁盘的写保护状态。";
        return e.Message;
    }
    public bool HideCard()
    {
        if(!TrySave())return false;SavePreferences();docking.HideToTray();Hide();return true;
    }
    public void ShowCard()=>ShowCard(true);
    private void ShowCard(bool check)
    {
        ShowRequests++;
        docking.Detach();
        if(check){CheckDay();RefreshFromDisk();}Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();
    }
    public bool RefreshFromDisk(bool preserveTyping=false)
    {
        if(refreshing||composing)return false;
        refreshing=true;
        try
        {
            RefreshTemplateCore(false);
            if(session.Dirty){if(preserveTyping&&(DateTime.UtcNow-lastEdit).TotalMilliseconds<1000)return true;return TrySave();}
            if(session.ReloadExternal()){SyncEditors();SetIdleStatus();}
            if(syncFailed){syncFailed=false;banner.Visibility=Visibility.Collapsed;SetIdleStatus();}
            return true;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {syncFailed=true;status.Text="同步失败 · 当前内容已保留";status.SetResourceReference(TextBlock.ForegroundProperty,"Danger");ShowBanner("无法同步当天日记："+FriendlyError(e),true);return false;}
        finally{refreshing=false;}
    }
    private bool RefreshTemplateCore(bool force)
    {
        var text=DiarySession.ReadText(config.TemplatePath).Replace("{{date:YYYY-MM-DD}}",session.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        if(text==session.TemplateText){templateCandidate=null;return false;}
        if(!force)
        {
            if(templateCandidate!=text){templateCandidate=text;templateCandidateSince=DateTime.UtcNow;return false;}
            if((DateTime.UtcNow-templateCandidateSince).TotalMilliseconds<500||(DateTime.UtcNow-lastEdit).TotalMilliseconds<1000)return false;
        }
        if(!session.ReloadTemplate(text))return false;
        templateCandidate=null;draftNeeded=false;BuildSections(true);SetIdleStatus();ShowBanner("模板已同步，共 "+session.Template.Count+" 个栏目。"+session.TemplateNotice,false);return true;
    }
    public bool RefreshTemplateNow()
    {
        if(composing)return false;
        try{RefreshTemplateCore(true);return true;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text="模板同步失败 · 原内容保留";ShowBanner("无法更新模板："+FriendlyError(e),true);return false;}
    }
    public bool CheckDay()
    {
        if(composing)return false;
        if(today()==session.Date)return true;
        if(!TrySave()){ShowBanner("系统日期已变化，当前仍编辑 "+session.Date+"。请先保存，或保留恢复草稿后切换今天。\n"+session.Error,true);return false;}
        try{var next=new DiarySession(config,today());next.EnsureDailyFile();session=next;BuildSections();SetIdleStatus();ShowBanner("已切换到 "+session.Date+"，并同步当天日记；上一日内容已保存。",false);return true;}
        catch(Exception e){status.Text="新一天日记准备失败 · 保留上一日";status.SetResourceReference(TextBlock.ForegroundProperty,"Danger");ShowBanner("无法创建或匹配当天日记，仍停留在 "+session.Date+"："+e.Message,true);return false;}
    }
    private void SwitchTodayKeepingDraft()
    {
        if(today()==session.Date){ShowBanner("当前已经是今天；可使用“查看与恢复”处理保存问题。",false);return;}
        try{session.ArchiveDraft("跨天前保留草稿");var next=new DiarySession(config,today());next.EnsureDailyFile();session=next;BuildSections();SetIdleStatus();ShowBanner("已保留上一日恢复草稿并切换今天。",false);}
        catch(Exception e){ShowBanner("草稿保留或切换失败："+e.Message,true);}
    }
    public bool ExitCard()
    {
        if(!TrySave()){ShowCard(false);return false;}SavePreferences();exiting=true;Close();return true;
    }
    private void SavePreferences()
    {
        try{if(WindowState==WindowState.Normal){prefs.Width=ActualWidth;prefs.Height=ActualHeight;prefs.Left=Left;prefs.Top=Top;}prefs.Topmost=Topmost;prefs.Save(config.UserDataPath);}
        catch(Exception e){ShowBanner("界面偏好未能保存："+e.Message,false);}
    }
    internal void StoreWindowPreferences()=>SavePreferences();
    internal void NotifyDockError(string message)=>ShowBanner("贴边收起暂时不可用，卡片内容保留："+message,true);
    internal void SynchronizeAfterEdgeExpand(){CheckDay();RefreshFromDisk();}
    public void SetEdgeAutoHide(bool enabled){prefs.EdgeAutoHide=enabled;docking.SetEnabled(enabled);if(edgeMenu!=null)edgeMenu.IsChecked=enabled;SavePreferences();}
    public void ApplyTheme(string id)
    {
        prefs.Theme=Themes.Get(id).Id;ApplyWindowEffects();SavePreferences();
    }
    private void ApplyWindowEffects()
    {
        if(new WindowInteropHelper(this).Handle!=IntPtr.Zero)Effects=WindowEffects.Apply(this,prefs.Theme=="glass"&&!HasPictureBackground);
        Themes.Apply(prefs.Theme,Effects.AcrylicConfigured);
        ApplyBackgroundLayers();
        if(Effects.AcrylicConfigured)Background=Brushes.Transparent;
        else SetResourceReference(BackgroundProperty,"WindowBg");
        DockNative.CustomDockStyle(this,prefs.EdgeAutoHide);
        foreach(var dialog in settingsDialogs.Keys.ToArray())ApplyDialogAppearance(dialog);
        if(moreButton?.ContextMenu is { } menu)Themes.StyleContextMenu(menu,prefs.Theme);
        docking.RefreshMenuTheme();
    }
    private IntPtr OnNativeMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message is 0x031E or 0x031A or 0x001A)Dispatcher.BeginInvoke(ApplyWindowEffects);
        if(message==0x0231){nativeMoved=false;docking.BeginMove();}
        if(message==0x0216)nativeMoved=true; // WM_MOVING，不把单纯缩放当成贴边拖动。
        if(message==0x0112&&(wParam.ToInt64()&0xfff0)==0xf030&&docking.Enabled&&docking.Moving){handled=true;return IntPtr.Zero;}
        if(message==0x0232){docking.CompleteMove(nativeMoved);SavePreferences();}
        if(message is 0x007E or 0x02E0)Dispatcher.BeginInvoke(docking.Reflow);
        return IntPtr.Zero;
    }
    private void ApplyBackgroundLayers()
    {
        wallpaper.Opacity=prefs.BackgroundOpacity;
        wallpaper.Visibility=wallpaperVeil.Visibility=HasPictureBackground?Visibility.Visible:Visibility.Collapsed;
        if(!HasPictureBackground){ApplySectionTransparency();return;}
        var p=Themes.Get(prefs.Theme);
        SolidColorBrush Opaque(string hex,byte alpha){var c=(Color)ColorConverter.ConvertFromString(hex);c.A=alpha;var b=new SolidColorBrush(c);b.Freeze();return b;}
        var resources=Application.Current.Resources;
        resources["WindowBg"]=Opaque(p.Background,255);resources["Surface"]=Opaque(p.Surface,240);resources["Field"]=Opaque(p.Field,248);
        wallpaperVeil.Background=(Brush)resources["WindowBg"];
        ApplySectionTransparency();wallpaper.SetRunning(IsVisible&&prefs.DynamicBackgroundPlaying);
    }
    private void ApplySectionTransparency()
    {
        var resources=Application.Current.Resources;
        foreach(var (source,target) in new[]{("Surface","SectionSurface"),("Field","SectionField")})
        {var color=((SolidColorBrush)resources[source]).Color;color.A=(byte)Math.Round(color.A*(1-prefs.SectionTransparency));var brush=new SolidColorBrush(color);brush.Freeze();resources[target]=brush;}
    }
    public void SetSectionTransparency(double value,bool persist=true){if(!double.IsFinite(value))return;prefs.SectionTransparency=Math.Clamp(value,0,0.9);ApplySectionTransparency();if(persist)SavePreferences();}
    public Window CreateTransparencyDialog(Window? owner=null)
    {
        var dialog=Dialog("栏目透明度",440,330);if(owner!=null)dialog.Owner=owner;
        var content=new StackPanel {Margin=new Thickness(24)};content.Children.Add(new TextBlock {Text="栏目透明程度",FontSize=20,FontWeight=FontWeights.SemiBold});
        var label=new TextBlock {Margin=new Thickness(0,12,0,5)};void Update()=>label.Text=$"{prefs.SectionTransparency:P0} · 数值越高，越能透出背景";Update();content.Children.Add(label);
        var slider=new Slider {Minimum=0,Maximum=0.9,Value=prefs.SectionTransparency,SmallChange=0.05,LargeChange=0.1,Margin=new Thickness(0,8,0,12)};System.Windows.Automation.AutomationProperties.SetName(slider,"栏目透明程度");slider.ValueChanged+=(_,_)=>{SetSectionTransparency(slider.Value,false);Update();};content.Children.Add(slider);
        content.Children.Add(new TextBlock {Text="仅调整栏目和输入区底色，文字与任务勾选保持清晰。",FontSize=12});
        var buttons=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};buttons.Children.Add(Button("恢复默认",()=>slider.Value=0));buttons.Children.Add(Button("完成",()=>dialog.Close()));content.Children.Add(buttons);
        dialog.Content=content;dialog.Closed+=(_,_)=>SavePreferences();return dialog;
    }
    public bool ImportBackground(string path)
    {
        if(LocalBackground.IsVideo(path)){ShowBanner("MP4 请通过背景选择器载入。",false);return false;}
        backgroundRevision++;wallpaper.CancelPreparation();var oldName=prefs.BackgroundImage;RasterBackground? prepared=null;
        try
        {
            var name=LocalBackground.Import(config.UserDataPath,path);prepared=new RasterBackground(config.UserDataPath,name);
            prefs.BackgroundImage=name;prefs.Save(config.UserDataPath);wallpaper.UseRaster(prepared);prepared=null;ApplyWindowEffects();return true;
        }
        catch(Exception e){prepared?.Dispose();prefs.BackgroundImage=oldName;ApplyWindowEffects();ShowBanner("背景未更换："+e.Message,true);return false;}
    }
    public async Task<bool> ImportBackgroundAsync(string path)
    {
        if(backgroundImporting){ShowBanner("正在载入背景，请稍后再试。",false);return false;}
        if(!LocalBackground.IsVideo(path))return ImportBackground(path);
        try{var name=LocalBackground.Import(config.UserDataPath,path);return await LoadVideoBackground(name,true);}
        catch(Exception e){ShowBanner("背景未更换："+e.Message,true);return false;}
    }
    private async Task<bool> LoadVideoBackground(string name,bool persist)
    {
        backgroundImporting=true;int revision=++backgroundRevision;var oldName=prefs.BackgroundImage;MediaElement? prepared=null;
        try
        {
            prepared=await wallpaper.PrepareVideo(LocalBackground.Resolve(config.UserDataPath,name));
            if(revision!=backgroundRevision||exiting){wallpaper.ReleaseVideo(prepared);return false;}
            if(persist){prefs.BackgroundImage=name;prefs.Save(config.UserDataPath);}
            wallpaper.UseVideo(prepared);prepared=null;ApplyWindowEffects();return true;
        }
        catch(Exception e){if(prepared!=null)wallpaper.ReleaseVideo(prepared);if(revision==backgroundRevision&&!exiting){prefs.BackgroundImage=oldName;ApplyWindowEffects();ShowBanner("动态背景未更换："+e.Message,false);}return false;}
        finally{backgroundImporting=false;}
    }
    public void SetDynamicBackgroundPlaying(bool value){prefs.DynamicBackgroundPlaying=value;wallpaper.SetRunning(IsVisible&&value);SavePreferences();}
    public bool RemoveBackground()
    {
        var oldName=prefs.BackgroundImage;
        try{prefs.BackgroundImage="";prefs.Save(config.UserDataPath);backgroundRevision++;wallpaper.Clear();ApplyWindowEffects();return true;}
        catch(Exception e){prefs.BackgroundImage=oldName;ShowBanner("无法移除图片背景："+e.Message,true);return false;}
    }
    public void SetBackgroundOpacity(double value,bool persist=true)
    {
        if(!double.IsFinite(value))return;
        prefs.BackgroundOpacity=Math.Clamp(value,0.15,1);wallpaper.Opacity=prefs.BackgroundOpacity;
        if(persist)SavePreferences();
    }
    private void SetFont(double size){prefs.FontSize=Math.Clamp(size,14,24);foreach(var e in editors.Values)e.SetFont(prefs.FontSize);SavePreferences();}
    public Window CreateThemeLibrary()
    {
        var dialog=Dialog("主题库",550,610);var layout=new DockPanel {Margin=new Thickness(22)};
        var intro=new StackPanel();intro.Children.Add(new TextBlock{Text="选择你的书写氛围",FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        var help=new TextBlock{Text="颜色主题可叠加本地图片背景，正文与任务状态保持不变。",FontSize=13,Margin=new Thickness(0,0,0,16)};help.SetResourceReference(TextBlock.ForegroundProperty,"Muted");intro.Children.Add(help);DockPanel.SetDock(intro,Dock.Top);layout.Children.Add(intro);
        var done=Button("完成",()=>dialog.Close());done.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(done,Dock.Bottom);layout.Children.Add(done);
        var stack=new StackPanel();
        var transparency=Button("栏目透明度…",()=>CreateTransparencyDialog(dialog).ShowDialog());transparency.HorizontalAlignment=HorizontalAlignment.Left;transparency.Margin=new Thickness(0,0,0,12);stack.Children.Add(transparency);
        stack.Children.Add(CreateBackgroundSettings(dialog));
        var themeLabels=new Dictionary<string,TextBlock>();
        foreach(var p in Themes.All)
        {
            var card=new Border{BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(11),Padding=new Thickness(14),Margin=new Thickness(0,0,0,10)};card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Line");
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var words=new StackPanel();var themeLabel=new TextBlock{Text=p.Name+(p.Id==prefs.Theme?"  ✓":""),FontSize=17,FontWeight=FontWeights.SemiBold};themeLabels[p.Id]=themeLabel;words.Children.Add(themeLabel);
            var description=new TextBlock{Text=p.Description,FontSize=12,Margin=new Thickness(0,5,8,0)};description.SetResourceReference(TextBlock.ForegroundProperty,"Muted");words.Children.Add(description);
            var swatches=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};foreach(var color in new[]{p.Background,p.Surface,p.Accent})swatches.Children.Add(new Border {Width=28,Height=6,Background=Themes.Brush(color),CornerRadius=new CornerRadius(2),Margin=new Thickness(0,0,5,0)});words.Children.Add(swatches);
            if(p.Id=="glass"){var hint=new TextBlock {Text="支持的 Windows 11 使用系统磨砂；其他环境回退实色。",FontSize=11,Margin=new Thickness(0,5,8,0)};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");words.Children.Add(hint);}
            var use=Button("应用",()=>{ApplyTheme(p.Id);dialog.Title="主题库 · 当前："+p.Name;foreach(var item in Themes.All)themeLabels[item.Id].Text=item.Name+(item.Id==prefs.Theme?"  ✓":"");});use.Tag=p.Id;use.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(use,1);row.Children.Add(words);row.Children.Add(use);card.Child=row;stack.Children.Add(card);
        }
        var list=new ScrollViewer {Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};layout.Children.Add(list);dialog.Content=layout;return dialog;
    }
    private Border CreateBackgroundSettings(Window dialog)
    {
        var panel=new StackPanel();panel.Children.Add(new TextBlock {Text="本地图片与动态背景",FontSize=18,FontWeight=FontWeights.SemiBold});
        var row=new DockPanel {Margin=new Thickness(0,12,0,10)};
        var thumbnail=new Image {Width=108,Height=80,Stretch=Stretch.UniformToFill,Margin=new Thickness(0,0,12,0)};DockPanel.SetDock(thumbnail,Dock.Left);row.Children.Add(thumbnail);
        var options=new StackPanel();var name=new TextBlock {FontSize=12};name.SetResourceReference(TextBlock.ForegroundProperty,"Muted");options.Children.Add(name);
        var buttons=new WrapPanel();Button remove=null!,play=null!;
        void Refresh(){thumbnail.Source=wallpaperBitmap;name.Text=HasPictureBackground?$"{wallpaper.Kind} · 已保存到本机":"当前使用主题底色";remove.IsEnabled=!string.IsNullOrEmpty(prefs.BackgroundImage);play.IsEnabled=wallpaper.IsDynamic;play.Content=prefs.DynamicBackgroundPlaying?"暂停动态背景":"继续播放";}
        buttons.Children.Add(Button("选择本机背景…",async()=>{
            var picker=new Microsoft.Win32.OpenFileDialog {Title="选择图片、动图或视频背景",Filter="背景（图片 / GIF / MP4）|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4",CheckFileExists=true,Multiselect=false};
            if(picker.ShowDialog(dialog)==true){buttons.IsEnabled=false;name.Text="正在载入背景…";try{bool loaded=await ImportBackgroundAsync(picker.FileName);if(!dialog.IsVisible)return;Refresh();if(!loaded)System.Windows.MessageBox.Show(dialog,bannerText.Text,"背景未更换",MessageBoxButton.OK,MessageBoxImage.Information);}finally{buttons.IsEnabled=true;}}
        }));
        remove=Button("移除",()=>{if(RemoveBackground())Refresh();});buttons.Children.Add(remove);options.Children.Add(buttons);row.Children.Add(options);panel.Children.Add(row);
        play=Button("暂停动态背景",()=>{SetDynamicBackgroundPlaying(!prefs.DynamicBackgroundPlaying);Refresh();});play.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(play);
        var strength=new TextBlock {FontSize=12};strength.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        void Label()=>strength.Text=$"背景显现度 · {prefs.BackgroundOpacity:P0}";Label();panel.Children.Add(strength);
        var slider=new Slider {Minimum=0.15,Maximum=1,Value=prefs.BackgroundOpacity,SmallChange=0.05,LargeChange=0.1,Margin=new Thickness(0,7,0,8)};
        System.Windows.Automation.AutomationProperties.SetName(slider,"背景显现度");slider.ValueChanged+=(_,_)=>{SetBackgroundOpacity(slider.Value,false);Label();};panel.Children.Add(slider);
        panel.Children.Add(new TextBlock {Text="图片／GIF 最大 20 MB；MP4 最大 100 MB，需系统支持 H.264。动态背景静音循环，卡片隐藏后暂停。文件仅保存本机。",FontSize=11});
        dialog.Closed+=(_,_)=>SavePreferences();Refresh();
        var border=new Border {Child=panel,Padding=new Thickness(14),CornerRadius=new CornerRadius(11),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,14)};border.SetResourceReference(Border.BackgroundProperty,"Surface");border.SetResourceReference(Border.BorderBrushProperty,"Line");return border;
    }
    private void ShowThemes()=>CreateThemeLibrary().ShowDialog();
    private Window Dialog(string title,double width,double height)
    {
        var dialog=new Window {Owner=this,Title=title,Width=width,Height=Math.Min(height,SystemParameters.WorkArea.Height-25),MinWidth=420,WindowStartupLocation=WindowStartupLocation.CenterOwner,WindowStyle=WindowStyle.ToolWindow,ResizeMode=ResizeMode.CanResizeWithGrip,ShowInTaskbar=false};
        settingsDialogs[dialog]=new(false,-1,-1,-1,-1);ApplyDialogAppearance(dialog);
        dialog.SourceInitialized+=(_,_)=>{DockNative.SettingsCaption(dialog);ApplyDialogAppearance(dialog);};
        dialog.Closed+=(_,_)=>settingsDialogs.Remove(dialog);
        return dialog;
    }
    private void ApplyDialogAppearance(Window dialog)
    {
        foreach(var pair in Themes.DialogBrushes(prefs.Theme))dialog.Resources[pair.Key]=pair.Value;
        dialog.SetResourceReference(BackgroundProperty,"WindowBg");dialog.SetResourceReference(ForegroundProperty,"Text");
        settingsDialogs[dialog]=WindowEffects.ApplyDialog(dialog,prefs.Theme=="night");
    }
    internal DialogEffectState DialogAppearanceFor(Window dialog)=>settingsDialogs[dialog];
    public VaultSetupWindow CreateVaultSettingsDialog()
    {
        var dialog=new VaultSetupWindow(config.ConfigPath,ApplyVaultPlan,config,prefs.Theme){Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        settingsDialogs[dialog]=new(false,-1,-1,-1,-1);ApplyDialogAppearance(dialog);
        dialog.SourceInitialized+=(_,_)=>ApplyDialogAppearance(dialog);dialog.Closed+=(_,_)=>settingsDialogs.Remove(dialog);return dialog;
    }
    public bool ApplyVaultPlan(VaultPlan plan)
    {
        if(composing){ShowBanner("请先完成当前中文输入，再切换日记库。",true);return false;}
        if(!TrySave())return false;
        if(VaultUse.Key(plan.Config)!=VaultUse.Key(config)&&VaultUse.InUse(plan.Config))throw new IOException("目标日记库正在另一张卡片中打开，请先正常退出它。");
        plan.Config.PathMappings=config.PathMappings;SavePreferences();
        using(var transaction=SetupService.Prepare(plan)){transaction.Commit();}
        RestartRequested=true;IsEnabled=false;
        Dispatcher.BeginInvoke(new Action(()=>{exiting=true;Close();}),DispatcherPriority.Background);
        return true;
    }
    private void ShowConfig()
        =>CreateConfigDialog().ShowDialog();
    public Window CreateConfigDialog()
    {
        var message="仓库："+config.VaultPath+"\n模板："+config.TemplatePath+"\n日记目录："+config.DiaryFolder+"\n命名规则："+config.DateFormat+"\n本机草稿与偏好："+config.UserDataPath+"\n配置文件："+config.ConfigPath+"\n\n可使用“日记库设置”修改地址、模板和保存规则。切换前先保存，再自动重新打开卡片。\n程序不联网，不注册开机启动，不新增跨天任务继承。";
        var dialog=Dialog("日记卡片配置",650,500);var layout=new DockPanel {Margin=new Thickness(22)};
        var done=Button("关闭",()=>dialog.Close());done.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(done,Dock.Bottom);layout.Children.Add(done);
        var change=Button("日记库设置…",()=>{dialog.Close();CreateVaultSettingsDialog().ShowDialog();});change.HorizontalAlignment=HorizontalAlignment.Left;DockPanel.SetDock(change,Dock.Bottom);layout.Children.Add(change);
        layout.Children.Add(new TextBox {Text=message,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=13});dialog.Content=layout;return dialog;
    }
    private void ShowRecovery()
        =>CreateRecoveryDialog().ShowDialog();
    public Window CreateRecoveryDialog()
    {
        var d=Dialog("查看与恢复",950,730);var grid=new Grid{Margin=new Thickness(18)};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        grid.Children.Add(new TextBlock{Text="本地输入与磁盘原文均保留。自动保存不会覆盖同一栏目的外部修改。\n"+session.Target,FontSize=13,Margin=new Thickness(0,0,0,12)});
        var panes=new Grid();panes.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});panes.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});Grid.SetRow(panes,1);
        string disk;try{disk=File.Exists(session.Target)?DiarySession.ReadText(session.Target):"（磁盘文件尚不存在或已被移动）";}catch(Exception e){disk="无法读取磁盘："+e.Message;}
        for(int i=0;i<2;i++)
        {
            var p=new DockPanel{Margin=new Thickness(i==0?0:8,0,i==0?8:0,0)};var label=new TextBlock{Text=i==0?"卡片中的本地内容":"磁盘当前内容",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(label,Dock.Top);p.Children.Add(label);
            var t=new TextBox{Text=i==0?session.LocalDocument():disk,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=14};p.Children.Add(t);Grid.SetColumn(p,i);panes.Children.Add(p);
        }
        grid.Children.Add(panes);var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};Grid.SetRow(buttons,2);
        buttons.Children.Add(Button("打开恢复目录",()=>{Directory.CreateDirectory(Path.Combine(config.UserDataPath,"recovery"));OpenPath(Path.Combine(config.UserDataPath,"recovery"));}));
        buttons.Children.Add(Button("另存我的 Markdown 草稿",()=>{try{var file=session.ArchiveDraft("手动导出恢复草稿");System.Windows.MessageBox.Show(d,"草稿已保存：\n"+file,"恢复草稿");}catch(Exception e){System.Windows.MessageBox.Show(d,e.Message,"另存失败");}}));
        buttons.Children.Add(Button("保留草稿并载入磁盘",()=>{try{LoadDiskKeepingDraft();d.Close();}catch(Exception e){System.Windows.MessageBox.Show(d,e.Message,"恢复失败");}}));
        buttons.Children.Add(Button("继续编辑",()=>d.Close()));grid.Children.Add(buttons);d.Content=grid;return d;
    }
    public void LoadDiskKeepingDraft(){session.ReloadKeepingDraft();draftNeeded=false;SyncEditors();banner.Visibility=Visibility.Collapsed;SetIdleStatus();}
    private void OpenPath(string path)
    {
        try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
        catch(Exception e){ShowBanner("无法打开："+e.Message,true);}
    }
    public void ExpandAll(){foreach(var e in expanders.Values)e.IsExpanded=true;}
    public void Capture(string file)
    {
        UpdateLayout();var dpi=VisualTreeHelper.GetDpi(this);var bmp=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bmp.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));Directory.CreateDirectory(Path.GetDirectoryName(file)!);using var stream=File.Create(file);encoder.Save(stream);
    }
    internal BitmapSource DockSnapshot()
    {
        root.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(this);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(root);bitmap.Freeze();return bitmap;
    }
}

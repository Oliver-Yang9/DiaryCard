using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DailyDiaryCard;

internal static class SettingsThemeRegression
{
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {var child=VisualTreeHelper.GetChild(parent,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}
    }
    private static Color ColorOf(Brush brush)=>((SolidColorBrush)brush).Color;
    private static bool Dark(Brush brush){var c=ColorOf(brush);return c.A==255&&c.R<65&&c.G<65&&c.B<65;}
    private static bool LightText(Brush brush){var c=ColorOf(brush);return c.A==255&&c.R>225&&c.G>225&&c.B>225;}
    public static void Run(MainWindow window,string output,Action<bool,string> assert)
    {
        var original=window.Session.LocalDocument();var background=window.BackgroundFile;double transparency=window.SectionTransparency;
        var results=new List<string>();var effects=new List<object>();
        void Check(bool ok,string label){assert(ok,label);results.Add(label);}
        window.ApplyTheme("glass");var library=window.CreateThemeLibrary();library.Height=780;library.Show();library.UpdateLayout();
        try
        {
            var apply=Descendants<Button>(library).Single(b=>Equals(b.Tag,"night"));apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));library.UpdateLayout();
            Check(window.CurrentTheme=="night"&&Dark(library.Background)&&LightText(library.Foreground),"从玻璃主题打开设置后切换深色，设置立即变为黑底白字");
            var heading=Descendants<TextBlock>(library).Single(t=>t.Text=="选择你的书写氛围");Check(LightText(heading.Foreground),"设置标题使用完整浅色文字");
            var title=Descendants<TextBlock>(library).Single(t=>t.Text=="清透卡片");Check(LightText(title.Foreground),"主题列表统一深色行与白字，不保留浅色预览卡片底色");
            var card=Descendants<Border>(library).Single(b=>ReferenceEquals(b.Child,((FrameworkElement)title.Parent).Parent));
            Check(Dark(card.Background),"主题选项卡底色跟随深色设置面板");
            var slider=Descendants<Slider>(library).Single();slider.ApplyTemplate();
            Check(slider.Template.FindName("PART_Track",slider) is Track,"深色滑块使用可操作的统一轨道模板");
            Check(DockNative.SettingsCaptionValid(library),"深色设置仍没有最小化按钮，并保留边框缩放");
            var state=window.DialogAppearanceFor(library);effects.Add(new{window="主题库",state});
            if(Environment.OSVersion.Version.Build>=22000)Check(state.Dark&&state.CaptionResult==0&&state.TextResult==0,"当前 Windows 的深色设置标题栏与文字颜色请求成功");
            UiSmoke.Capture(library,Path.Combine(output,"21-dark-settings.png"));
            var opacity=window.CreateTransparencyDialog(library);opacity.Show();opacity.UpdateLayout();
            try
            {
                Check(Dark(opacity.Background)&&LightText(opacity.Foreground),"栏目透明度窗口使用黑底白字");
                Check(Descendants<TextBlock>(opacity).Where(t=>t.Text.Contains("透明程度")||t.Text.Contains("仅调整")).All(t=>LightText(t.Foreground)),"透明度标签与说明文字在深色中清晰可读");
                UiSmoke.Capture(opacity,Path.Combine(output,"22-dark-opacity.png"));
                Descendants<Button>(library).Single(b=>Equals(b.Tag,"paper")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(ColorOf(opacity.Background).R>200&&ColorOf(opacity.Foreground).R<100,"切回浅色时已打开的子设置窗口同步恢复浅底深字");
                Descendants<Button>(library).Single(b=>Equals(b.Tag,"night")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Dark(opacity.Background)&&LightText(opacity.Foreground),"再次切回深色时子设置窗口同步更新");
            }
            finally{opacity.Close();}
            var configuration=window.CreateConfigDialog();configuration.Show();configuration.UpdateLayout();
            try
            {
                var text=Descendants<TextBox>(configuration).Single();
                Check(Dark(configuration.Background)&&Dark(text.Background)&&LightText(text.Foreground),"查看配置窗口与只读配置内容均使用深色配色");
                UiSmoke.Capture(configuration,Path.Combine(output,"23-dark-config.png"));
            }
            finally{configuration.Close();}
            var recovery=window.CreateRecoveryDialog();recovery.Show();recovery.UpdateLayout();
            try
            {
                Check(Dark(recovery.Background)&&Descendants<TextBox>(recovery).All(t=>Dark(t.Background)&&LightText(t.Foreground)),"恢复窗口及两侧原文区域同步使用黑底白字");
                UiSmoke.Capture(recovery,Path.Combine(output,"24-dark-recovery.png"));
            }
            finally{recovery.Close();}
            Check(window.Session.LocalDocument()==original&&window.BackgroundFile==background&&Math.Abs(window.SectionTransparency-transparency)<0.001,"设置深浅切换不改变日记、背景或栏目透明度");
            DiarySession.AtomicText(Path.Combine(output,"settings-theme-result.json"),JsonSerializer.Serialize(new{passed=true,results,effects},CardConfig.Json));
        }
        finally{library.Close();window.ApplyTheme("fluent");}
    }
}

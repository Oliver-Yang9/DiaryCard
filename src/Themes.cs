using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace DailyDiaryCard;

public sealed record Palette(string Id, string Name, string Description, string Background, string Surface, string Field, string Text, string Muted, string Accent, string Border, string Hover);
public sealed class Preferences
{
    public string Theme { get; set; } = "fluent";
    public double Width { get; set; } = 560;
    public double Height { get; set; } = 850;
    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
    public double FontSize { get; set; } = 16;
    public bool Topmost { get; set; }
    public string BackgroundImage { get; set; } = "";
    public double BackgroundOpacity { get; set; } = 0.65;
    public double SectionTransparency { get; set; }
    public bool DynamicBackgroundPlaying { get; set; } = true;
    public bool EdgeAutoHide { get; set; } = true;
    public static Preferences Load(string dir)
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(dir,"settings.json")), CardConfig.Json) ?? new(); }
        catch { return new(); }
    }
    public void Save(string dir) => DiarySession.AtomicText(Path.Combine(dir,"settings.json"), JsonSerializer.Serialize(this, CardConfig.Json));
}
public static class Themes
{
    public static readonly Palette[] All = {
        new("fluent", "清透卡片", "灰蓝与白 · 紧凑、清晰的日常卡片", "#EFF3F8", "#FFFFFF", "#F7F9FC", "#202B3A", "#52647C", "#2865B0", "#D8E1ED", "#E3ECF7"),
        new("paper", "暖纸", "米白与暖棕 · 适合长文与从容复盘", "#F4F0E8", "#FFFCF6", "#FFFCF6", "#302E2A", "#6F685C", "#766747", "#E4DDD0", "#EDE7DA"),
        new("night", "深夜墨色", "深灰蓝与浅绿 · 柔和的晚间书写", "#171C24", "#202833", "#242E3B", "#E4E8EF", "#AAB5C4", "#B6C9AD", "#3A4554", "#344151"),
        new("glass", "雾透玻璃", "模糊半透明 · Windows 原生磨砂", "#54EDF5FF", "#98FFFFFF", "#A6FFFFFF", "#203043", "#485E77", "#235F9F", "#A0D5E3F2", "#C0E4EEF9")
    };
    public static Palette Get(string id) => Array.Find(All, p => p.Id==id) ?? All[0];
    public static Dictionary<string,SolidColorBrush> DialogBrushes(string id)
    {
        var p=Get(id);
        var colors=new Dictionary<string,string>{{"WindowBg",p.Background},{"Surface",p.Surface},{"Field",p.Field},{"Text",p.Text},{"Muted",p.Muted},{"Accent",p.Accent},{"Line",p.Border},{"Hover",p.Hover},{"Danger",p.Id=="night"?"#FF9B9B":"#B42330"}};
        if(p.Id=="night")
        {colors["WindowBg"]="#141414";colors["Surface"]="#202020";colors["Field"]="#282828";colors["Text"]="#F5F5F5";colors["Muted"]="#B8B8B8";colors["Line"]="#3F3F3F";colors["Hover"]="#303030";}
        var result=new Dictionary<string,SolidColorBrush>();
        foreach(var pair in colors){var color=(Color)ColorConverter.ConvertFromString(pair.Value);color.A=255;var brush=new SolidColorBrush(color);brush.Freeze();result[pair.Key]=brush;}
        return result;
    }
    public static void StyleContextMenu(ContextMenu menu,string theme)
    {
        foreach(var pair in DialogBrushes(theme))menu.Resources[pair.Key]=pair.Value;
        menu.SetResourceReference(FrameworkElement.StyleProperty,"DiaryContextMenu");
        foreach(var item in menu.Items)
        {
            if(item is MenuItem entry)entry.SetResourceReference(FrameworkElement.StyleProperty,"DiaryMenuItem");
            else if(item is Separator separator)separator.SetResourceReference(FrameworkElement.StyleProperty,"DiaryMenuSeparator");
        }
    }
    public static SolidColorBrush Brush(string hex) { var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    public static void Apply(string id,bool transparentGlass=true)
    {
        var p=Get(id); var r=Application.Current.Resources;
        r["WindowBg"]=Brush(p.Background); r["Surface"]=Brush(p.Surface); r["Field"]=Brush(p.Field); r["Text"]=Brush(p.Text); r["Muted"]=Brush(p.Muted); r["Accent"]=Brush(p.Accent); r["Line"]=Brush(p.Border); r["Hover"]=Brush(p.Hover);
        r["Danger"]=Brush(p.Id=="night"?"#FF9B9B":"#B42330");
        if(p.Id=="glass"&&!transparentGlass)
            foreach(var key in new[]{"WindowBg","Surface","Field","Line","Hover"})
            {var color=((SolidColorBrush)r[key]).Color;color.A=255;var brush=new SolidColorBrush(color);brush.Freeze();r[key]=brush;}
    }
    public static void Initialize()
    {
        Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));
        Apply("fluent");
    }
    private const string Styles = """
    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Style TargetType="Window"><Setter Property="FontFamily" Value="Microsoft YaHei UI, Microsoft YaHei, Segoe UI"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource WindowBg}"/><Setter Property="FontSize" Value="14"/></Style>
      <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="TextWrapping" Value="Wrap"/></Style>
      <Style TargetType="Button"><Setter Property="Background" Value="{DynamicResource Surface}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="12,7"/><Setter Property="Margin" Value="3"/><Setter Property="Cursor" Value="Hand"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="B" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="B" Property="Background" Value="{DynamicResource Hover}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Accent}"/><Setter TargetName="B" Property="BorderThickness" Value="2"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="TextBox"><Setter Property="FontSize" Value="16"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Field}"/><Setter Property="CaretBrush" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="10,9"/><Setter Property="SelectionBrush" Value="{DynamicResource Accent}"/><Setter Property="SelectionOpacity" Value="0.3"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border x:Name="B" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7"><ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"/></Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="Cursor" Value="Hand"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><Grid MinWidth="30" MinHeight="32"><Border x:Name="B" Width="20" Height="20" HorizontalAlignment="Left" VerticalAlignment="Center" CornerRadius="4" BorderBrush="{DynamicResource Muted}" BorderThickness="1" Background="{DynamicResource Surface}"/><TextBlock x:Name="Tick" Text="✓" Width="20" TextAlignment="Center" FontSize="15" FontWeight="Bold" HorizontalAlignment="Left" VerticalAlignment="Center" Visibility="Hidden" Foreground="{DynamicResource Accent}"/></Grid><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Tick" Property="Visibility" Value="Visible"/><Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Accent}"/><Setter TargetName="B" Property="BorderThickness" Value="2"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="B" Property="BorderThickness" Value="2"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="Expander"><Setter Property="Background" Value="{DynamicResource Surface}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Margin" Value="0,0,0,10"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Expander"><Border Background="{TemplateBinding Background}" BorderBrush="{DynamicResource Line}" BorderThickness="1" CornerRadius="11"><StackPanel><ToggleButton x:Name="Head" Content="{TemplateBinding Header}" IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" Padding="16,15" HorizontalContentAlignment="Stretch" Foreground="{DynamicResource Text}" Background="Transparent" BorderThickness="0"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border x:Name="H" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" CornerRadius="10"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="24"/></Grid.ColumnDefinitions><ContentPresenter Grid.Column="0"/><TextBlock x:Name="Arrow" Grid.Column="1" Text="＋" Foreground="{DynamicResource Muted}" HorizontalAlignment="Right"/></Grid></Border><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="Text" Value="−"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="H" Property="Background" Value="{DynamicResource Hover}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="H" Property="Background" Value="{DynamicResource Hover}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter x:Name="Body" ContentSource="Content" Margin="14,0,14,14" Visibility="Collapsed"/></StackPanel></Border><ControlTemplate.Triggers><Trigger Property="IsExpanded" Value="True"><Setter TargetName="Body" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="ToolTip"><Setter Property="Background" Value="{DynamicResource Surface}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/></Style>
      <Style x:Key="DiaryContextMenu" TargetType="ContextMenu"><Setter Property="OverridesDefaultStyle" Value="True"/><Setter Property="Focusable" Value="True"/><Setter Property="Background" Value="{DynamicResource Surface}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="FontFamily" Value="Microsoft YaHei UI, Microsoft YaHei, Segoe UI"/><Setter Property="FontSize" Value="13"/><Setter Property="Padding" Value="5"/><Setter Property="MinWidth" Value="228"/><Setter Property="SnapsToDevicePixels" Value="True"/><Setter Property="KeyboardNavigation.DirectionalNavigation" Value="Cycle"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu"><Border x:Name="MenuSurface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}" CornerRadius="8"><ScrollViewer CanContentScroll="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/></ScrollViewer></Border></ControlTemplate></Setter.Value></Setter></Style>
      <Style x:Key="DiaryMenuItem" TargetType="MenuItem"><Setter Property="OverridesDefaultStyle" Value="True"/><Setter Property="Focusable" Value="True"/><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Padding" Value="9,7"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem"><Border x:Name="MenuRow" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" CornerRadius="5"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><TextBlock x:Name="Checkmark" Text="✓" Foreground="{TemplateBinding Foreground}" Visibility="Collapsed" FontWeight="Bold" VerticalAlignment="Center"/><ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center"/><TextBlock Grid.Column="2" Text="{TemplateBinding InputGestureText}" Foreground="{DynamicResource Muted}" Margin="12,0,0,0" VerticalAlignment="Center"/></Grid></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="MenuRow" Property="Background" Value="{DynamicResource Hover}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="MenuRow" Property="Background" Value="{DynamicResource Hover}"/></Trigger><Trigger Property="IsChecked" Value="True"><Setter TargetName="Checkmark" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="{DynamicResource Muted}"/><Setter Property="Opacity" Value="0.6"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      <Style x:Key="DiaryMenuSeparator" TargetType="Separator"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator"><Border Height="1" Margin="9,6" Background="{DynamicResource Line}"/></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="Slider"><Setter Property="MinHeight" Value="24"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="Transparent"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider"><Grid Background="{TemplateBinding Background}"><Track x:Name="PART_Track" Orientation="Horizontal" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" IsDirectionReversed="{TemplateBinding IsDirectionReversed}"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="4" CornerRadius="2" Background="{DynamicResource Accent}"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width="16" Height="22"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border x:Name="Grip" Background="{DynamicResource Field}" BorderBrush="{DynamicResource Accent}" BorderThickness="1.5" CornerRadius="5"/><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Grip" Property="Background" Value="{DynamicResource Hover}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="4" CornerRadius="2" Background="{DynamicResource Line}"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid><ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="ResizeGrip"><Setter Property="Width" Value="25"/><Setter Property="Height" Value="25"/><Setter Property="Cursor" Value="SizeNWSE"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ResizeGrip"><Grid Background="Transparent"><Path Data="M 5,20 L 20,5 M 11,20 L 20,11 M 17,20 L 20,17" Stroke="{DynamicResource Muted}" StrokeThickness="1.3" StrokeStartLineCap="Round" StrokeEndLineCap="Round" Opacity="0.65"/></Grid></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="ScrollBar"><Setter Property="Width" Value="9"/><Setter Property="Background" Value="Transparent"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Grid Background="Transparent" Margin="2,2,1,2"><Track x:Name="PART_Track" Orientation="{TemplateBinding Orientation}" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" ViewportSize="{TemplateBinding ViewportSize}" IsDirectionReversed="True"><Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{DynamicResource Muted}" CornerRadius="3" Opacity="0.42" MinHeight="24"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>
    </ResourceDictionary>
    """;
}

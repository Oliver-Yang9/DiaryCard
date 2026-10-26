using System.Windows;
using System.Windows.Markup;
namespace DailyDiaryCard;
internal static class WeeklyControlStyles
{
    public static void Apply(Window window)
    {
        var resources=(ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="ComboBoxItem">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/>
            <Setter Property="Padding" Value="10,5"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
              <Border x:Name="item" Background="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border>
              <ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="item" Property="Background" Value="{DynamicResource Hover}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="item" Property="Background" Value="{DynamicResource Hover}"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBox">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Margin" Value="0,0,6,0"/><Setter Property="MinHeight" Value="32"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
              <Grid>
                <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}">
                  <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{DynamicResource Field}" BorderBrush="{DynamicResource Line}" BorderThickness="1" CornerRadius="5"/></ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter Margin="9,5,20,5" IsHitTestVisible="False" Content="{TemplateBinding SelectionBoxItem}"/>
                <TextBlock Text="⌄" Foreground="{DynamicResource Muted}" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,7,0" IsHitTestVisible="False"/>
                <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False">
                  <Border Background="{DynamicResource Surface}" BorderBrush="{DynamicResource Line}" BorderThickness="1" MinWidth="{TemplateBinding ActualWidth}" Padding="3">
                    <ScrollViewer MaxHeight="260" CanContentScroll="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
                  </Border>
                </Popup>
              </Grid>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="DatePickerTextBox">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Field}"/><Setter Property="Padding" Value="5"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="DatePickerTextBox"><Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}"><ScrollViewer x:Name="PART_ContentHost"/></Border></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="CalendarDayButton">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Padding" Value="4"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CalendarDayButton">
              <Border x:Name="day" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center"/></Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsSelected" Value="True"><Setter TargetName="day" Property="Background" Value="{DynamicResource Hover}"/></Trigger>
                <Trigger Property="IsToday" Value="True"><Setter TargetName="day" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="day" Property="Background" Value="{DynamicResource Hover}"/></Trigger>
                <Trigger Property="IsInactive" Value="True"><Setter Property="Foreground" Value="{DynamicResource Muted}"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="CalendarButton">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CalendarButton"><Border Background="{DynamicResource Surface}" Padding="7"><ContentPresenter HorizontalAlignment="Center"/></Border></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="CalendarItem">
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CalendarItem">
              <Border Background="{DynamicResource Surface}" BorderBrush="{DynamicResource Line}" BorderThickness="1" Padding="8">
                <Grid x:Name="PART_Root">
                  <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
                  <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="32"/><ColumnDefinition Width="*"/><ColumnDefinition Width="32"/></Grid.ColumnDefinitions>
                    <Button x:Name="PART_PreviousButton" Content="‹" Padding="4"/>
                    <Button x:Name="PART_HeaderButton" Grid.Column="1" Padding="4"/>
                    <Button x:Name="PART_NextButton" Grid.Column="2" Content="›" Padding="4"/>
                  </Grid>
                  <Grid x:Name="PART_MonthView" Grid.Row="1" Margin="0,5,0,0">
                    <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
                    <Grid.RowDefinitions><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/></Grid.RowDefinitions>
                  </Grid>
                  <Grid x:Name="PART_YearView" Grid.Row="1" Visibility="Hidden">
                    <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
                    <Grid.RowDefinitions><RowDefinition/><RowDefinition/><RowDefinition/></Grid.RowDefinitions>
                  </Grid>
                </Grid>
              </Border>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="DatePicker"><Setter Property="Background" Value="{DynamicResource Field}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/></Style>
          <Style TargetType="Calendar"><Setter Property="CalendarItemStyle" Value="{StaticResource {x:Type CalendarItem}}"/><Setter Property="CalendarDayButtonStyle" Value="{StaticResource {x:Type CalendarDayButton}}"/><Setter Property="CalendarButtonStyle" Value="{StaticResource {x:Type CalendarButton}}"/><Setter Property="Background" Value="{DynamicResource Surface}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/></Style>
        </ResourceDictionary>
        """);
        window.Resources.MergedDictionaries.Add(resources);
    }
}
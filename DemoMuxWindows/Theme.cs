using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
namespace DemoMux;

internal static class Theme
{
    internal static readonly SolidColorBrush Background = Color("#100d11"), Surface = Color("#19151a"), Raised = Color("#29232a"),
        Border = Color("#44303b"), Text = Color("#faf2f5"), Muted = Color("#c8b6bf"), Accent = Color("#ff5b75"),
        Link = Color("#ffa3b4"), Action = Color("#8e354c");
    internal static SolidColorBrush Color(string value) => new((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
    internal static Brush Canvas => new RadialGradientBrush {
        Center = new Point(.9, .05), GradientOrigin = new Point(.9, .05), RadiusX = 1, RadiusY = .95,
        GradientStops = { new(Color("#331522").Color, 0), new(Background.Color, .88) }
    };
    private static Style Parse(string value) => (Style)XamlReader.Parse(value);
    internal static void Apply(Window window)
    {
        window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); window.FontSize = 13;
        window.Foreground = Text; window.Background = Canvas; window.UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        window.Resources[typeof(Button)] = ButtonStyle();
        window.Resources[typeof(TextBox)] = TextBoxStyle();
        window.Resources[typeof(ComboBox)] = ComboBoxStyle();
        window.Resources[typeof(CheckBox)] = CheckBoxStyle();
        window.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = ScrollBarStyle();
        window.Resources[typeof(ContextMenu)] = new Style(typeof(ContextMenu)) { Setters = {
            new Setter(Control.BackgroundProperty, Surface), new Setter(Control.ForegroundProperty, Text),
            new Setter(Control.BorderBrushProperty, Border), new Setter(Control.BorderThicknessProperty, new Thickness(1)),
            new Setter(Control.PaddingProperty, new Thickness(5)), new Setter(Control.FontSizeProperty, 13d) } };
        window.Resources[typeof(MenuItem)] = MenuItemStyle();
        window.Resources[SystemColors.ControlBrushKey] = Surface;
        window.Resources[SystemColors.ControlTextBrushKey] = Text;
        window.Resources[SystemColors.WindowBrushKey] = Surface;
        window.Resources[SystemColors.HighlightBrushKey] = Action;
        window.Resources[SystemColors.HighlightTextBrushKey] = Text;
    }
    internal static Style ButtonStyle() => Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Setter Property="Background" Value="#29232a"/><Setter Property="Foreground" Value="#faf2f5"/>
          <Setter Property="BorderBrush" Value="#00ffffff"/><Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Padding" Value="12,6"/><Setter Property="Margin" Value="0,3,8,3"/>
          <Setter Property="MinHeight" Value="32"/><Setter Property="Cursor" Value="Hand"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="HorizontalContentAlignment" Value="Center"/><Setter Property="VerticalContentAlignment" Value="Center"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
            <Border x:Name="Frame" CornerRadius="7" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
              <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}" RecognizesAccessKey="True"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#875366"/><Setter TargetName="Frame" Property="Opacity" Value="0.88"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#ffa3b4"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.65"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.36"/><Setter Property="Cursor" Value="Arrow"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    private static Style ScrollBarStyle() => Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
          <Setter Property="Width" Value="9"/><Setter Property="Background" Value="Transparent"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
            <Grid Background="Transparent" Margin="1,4,2,4">
              <Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="Vertical">
                <Track.DecreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageUpCommand}" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb MinHeight="28"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border x:Name="Grip" Background="#654353" CornerRadius="3"/><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Grip" Property="Background" Value="#a1647f"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageDownCommand}" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
              </Track>
            </Grid>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    internal static Style TextBoxStyle() => Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TextBox">
          <Setter Property="Background" Value="#19151a"/><Setter Property="Foreground" Value="#faf2f5"/><Setter Property="CaretBrush" Value="#ffa3b4"/>
          <Setter Property="BorderBrush" Value="#60404f"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="11,9"/>
          <Setter Property="SelectionBrush" Value="#8e354c"/><Setter Property="MinHeight" Value="38"/><Setter Property="Margin" Value="0,5,0,5"/>
          <Setter Property="VerticalContentAlignment" Value="Center"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox">
            <Border x:Name="Frame" CornerRadius="7" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"><ScrollViewer x:Name="PART_ContentHost"/></Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#ffa3b4"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    internal static Style CheckBoxStyle() => Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
          <Setter Property="Foreground" Value="#c8b6bf"/><Setter Property="Cursor" Value="Hand"/><Setter Property="MinHeight" Value="32"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
            <Grid Background="Transparent"><Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
              <Border x:Name="Box" Width="16" Height="16" CornerRadius="4" Background="#29232a" BorderBrush="#875366" BorderThickness="1" HorizontalAlignment="Left" VerticalAlignment="Center">
                <TextBlock x:Name="Check" Text="&#xE73E;" FontFamily="Segoe MDL2 Assets" FontSize="10" Foreground="#faf2f5" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Hidden"/>
              </Border>
              <ContentPresenter Grid.Column="1" VerticalAlignment="Center" RecognizesAccessKey="True"/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsChecked" Value="True"><Setter TargetName="Check" Property="Visibility" Value="Visible"/><Setter TargetName="Box" Property="Background" Value="#8e354c"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#ffa3b4"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    internal static Style MenuItemStyle() => Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="MenuItem">
          <Setter Property="Foreground" Value="#faf2f5"/><Setter Property="Padding" Value="13,9"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem">
            <Border x:Name="Frame" CornerRadius="5" Background="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/></Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Frame" Property="Background" Value="#52303f"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.38"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    public static Style ComboBoxStyle() => Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
          <Setter Property="Foreground" Value="#faf2f5"/><Setter Property="Background" Value="#29232a"/>
          <Setter Property="MinHeight" Value="32"/><Setter Property="Padding" Value="11,7,30,7"/>
          <Setter Property="ItemContainerStyle"><Setter.Value><Style TargetType="ComboBoxItem">
            <Setter Property="Foreground" Value="#faf2f5"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
              <Border x:Name="Item" Background="#22181e" Padding="11,9"><ContentPresenter/></Border>
              <ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Item" Property="Background" Value="#52303f"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Item" Property="Background" Value="#8e354c"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style></Setter.Value></Setter>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
            <Grid>
              <ToggleButton Focusable="False" ClickMode="Press" Background="{TemplateBinding Background}" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border CornerRadius="7" BorderBrush="#44303b" BorderThickness="1" Background="{TemplateBinding Background}"><TextBlock Text="&#xE70D;" FontFamily="Segoe MDL2 Assets" FontSize="10" Foreground="#ffa3b4" Margin="10" HorizontalAlignment="Right" VerticalAlignment="Center"/></Border></ControlTemplate></ToggleButton.Template>
              </ToggleButton>
              <ContentPresenter Margin="{TemplateBinding Padding}" IsHitTestVisible="False" VerticalAlignment="Center" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"/>
              <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False">
                <Border Background="#22181e" BorderBrush="#674052" BorderThickness="1" CornerRadius="7" Padding="3" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer MaxHeight="280" CanContentScroll="True"><ItemsPresenter/></ScrollViewer></Border>
              </Popup>
            </Grid>
            <ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter Property="Background" Value="#52303f"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
}

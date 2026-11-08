using System.Windows;
using System.Windows.Markup;
namespace DemoMux;
internal static class Theme
{
    public static Style ComboBoxStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
          <Setter Property="Foreground" Value="#faf2f5"/>
          <Setter Property="Background" Value="#22181e"/>
          <Setter Property="Padding" Value="10,7,28,7"/>
          <Setter Property="ItemContainerStyle">
            <Setter.Value><Style TargetType="ComboBoxItem">
              <Setter Property="Foreground" Value="#faf2f5"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
                <Border x:Name="Item" Background="#22181e" Padding="10,7"><ContentPresenter/></Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Item" Property="Background" Value="#8e354c"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style></Setter.Value>
          </Setter>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
            <Grid>
              <ToggleButton Focusable="False" ClickMode="Press" Background="{TemplateBinding Background}"
                  IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
                  <Border CornerRadius="8" BorderBrush="#674052" BorderThickness="1" Background="{TemplateBinding Background}">
                    <TextBlock Text="⌄" Foreground="#ffa3b4" Margin="8" HorizontalAlignment="Right" VerticalAlignment="Center"/>
                  </Border>
                </ControlTemplate></ToggleButton.Template>
              </ToggleButton>
              <ContentPresenter Margin="{TemplateBinding Padding}" IsHitTestVisible="False" VerticalAlignment="Center"
                  Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"/>
              <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False">
                <Border Background="#22181e" BorderBrush="#674052" BorderThickness="1" CornerRadius="8" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                  <ScrollViewer MaxHeight="280" CanContentScroll="True"><ItemsPresenter/></ScrollViewer>
                </Border>
              </Popup>
            </Grid>
            <ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
}

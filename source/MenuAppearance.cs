using System.Windows.Controls;
using System.Windows.Markup;
namespace ClaudeMeter {
public static class MenuAppearance {
    public static void Apply(ContextMenu menu){
        const string xml=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<Style TargetType='{x:Type ContextMenu}'>
 <Setter Property='FontFamily' Value='Segoe UI'/><Setter Property='FontSize' Value='15'/><Setter Property='Foreground' Value='#202228'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ContextMenu}'><Border Background='#F7F8FA' BorderBrush='#D5D8DE' BorderThickness='1' CornerRadius='14' Padding='7'><ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' MaxHeight='740'><StackPanel IsItemsHost='True'/></ScrollViewer></Border></ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='{x:Type Separator}'><Setter Property='Template'><Setter.Value><ControlTemplate><Border Height='1' Background='#DDE0E6' Margin='10,6'/></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='{x:Type MenuItem}'>
 <Setter Property='Foreground' Value='#202228'/><Setter Property='FontFamily' Value='Segoe UI'/><Setter Property='FontSize' Value='15'/><Setter Property='Padding' Value='9,7'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type MenuItem}'>
  <Grid><Border x:Name='surface' Background='Transparent' CornerRadius='7' Padding='{TemplateBinding Padding}'>
   <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='22'/><ColumnDefinition Width='*'/><ColumnDefinition Width='22'/></Grid.ColumnDefinitions>
    <TextBlock x:Name='check' Text='✓' Visibility='Hidden' Foreground='#0068DA'/><ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/><TextBlock x:Name='arrow' Grid.Column='2' Text='›' HorizontalAlignment='Right' Visibility='Hidden'/>
   </Grid></Border>
   <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-2' VerticalOffset='-7' AllowsTransparency='True' Focusable='False' IsOpen='{Binding IsSubmenuOpen,RelativeSource={RelativeSource TemplatedParent}}'>
    <Border Background='#F7F8FA' BorderBrush='#D5D8DE' BorderThickness='1' CornerRadius='14' Padding='7'><ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' MaxHeight='640'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/></ScrollViewer></Border>
   </Popup>
  </Grid>
  <ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='surface' Property='Background' Value='#DDEBFA'/></Trigger><Trigger Property='IsChecked' Value='True'><Setter TargetName='check' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='HasItems' Value='True'><Setter TargetName='arrow' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger></ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
</Style></ResourceDictionary>";
        menu.Resources=(System.Windows.ResourceDictionary)XamlReader.Parse(xml);
    }
}
}

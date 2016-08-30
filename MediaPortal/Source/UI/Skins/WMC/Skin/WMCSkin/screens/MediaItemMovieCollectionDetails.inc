<?xml version="1.0" encoding="utf-8"?>
<Grid
  xmlns="www.team-mediaportal.com/2008/mpf/directx"
  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
  xmlns:fanart="clr-namespace:MediaPortal.Extensions.UserServices.FanArtService.Client;assembly=FanArtService.Client"
  xmlns:media="clr-namespace:MediaPortal.UiComponents.Media.Models.AspectWrappers;assembly=Media"
  Grid.Column="2" Grid.Row="2" x:Name="MediaItemDetails"
  HorizontalAlignment="Stretch" VerticalAlignment="Stretch">

  <Grid.Resources>
    <fanart:FanArtImageSourceConverter x:Key="FanArtImageSourceConverter"/>
  </Grid.Resources>
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="*"/>
  </Grid.RowDefinitions>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="480"/>
    <ColumnDefinition Width="*"/>
  </Grid.ColumnDefinitions>

  <media:MovieCollectionAspectWrapper MediaItem="{Binding Source={StaticResource FanArtBackgroundModel},Path=MediaItem}" 
                            VerticalAlignment="Center" Grid.Column="0" Grid.ColumnSpan="2" Grid.Row="0" Margin="0,0,0,0" >
    <media:MovieCollectionAspectWrapper.Template>
      <ControlTemplate>
        <Grid DataContext="{Binding RelativeSource={RelativeSource TemplatedParent}}">
          <Label x:Name="MediaItemName" FontSize="{ThemeResource SmallFontSize}" FontFamily="SegoeUIBold"
                 Content="{Binding CollectionName}" Color="{ThemeResource TextColor}"/>
        </Grid>
      </ControlTemplate>
    </media:MovieCollectionAspectWrapper.Template>
</media:MovieCollectionAspectWrapper>
  
  <Label x:Name="MediaItemDescription" Wrap="True" Grid.Column="1" Grid.Row="1" Opacity="0.5" TextTrimming="WordEllipsis"
        HorizontalAlignment="Stretch" FontSize="{ThemeResource SmallFontSize}"
        Content="{Binding Source={StaticResource FanArtBackgroundModel}, Path=ItemDescription}"
        Color="{ThemeResource TextColor}" Margin="10,0,5,35"/>

  <StackPanel Grid.Column="0" Grid.Row="1" Margin="0,0,30,0" HorizontalAlignment="Stretch" VerticalAlignment="Stretch">
    <StackPanel Orientation="Horizontal" DataContext="{Binding Source={StaticResource FanArtBackgroundModel},Path=SelectedItem}">
      <Label Color="{ThemeResource TextColor}" FontSize="{ThemeResource SmallFontSize}" Content="{Binding AvailableMovies}"
             Margin="0,0,8,0" Opacity="0.5"/>
      <Label Color="{ThemeResource TextColor}" FontSize="{ThemeResource SmallFontSize}" Content="[Media.MoviesItemsHeader]"
             Margin="0,0,8,0" Opacity="0.5"/>
    </StackPanel>
  </StackPanel>
</Grid>
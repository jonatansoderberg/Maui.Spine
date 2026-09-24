# Plugin.Maui.Spine.Controls.DataGrid

`DataGrid` is a responsive row grid for .NET MAUI on `CollectionView`: fixed-height rows, named layouts (Wide/Narrow) over the same columns, sortable headers, grouping with expandable headers, swipe actions, load more and pull-to-refresh. Colours follow the light or dark theme; texts come in English and Swedish and can be overridden.

```bash
dotnet add package Plugin.Maui.Spine.Controls.DataGrid
```

`UseSpine()` registers it. Without Spine, call `builder.UseDataGrid()`; it keeps a row's swipe out of a scroll on Android.

```xml
<DataGrid ItemsSource="{Binding Orders}" RowTappedCommand="{Binding OpenOrderCommand}">
    <DataGridColumn Key="Number" Header="Order" BindingPath="Number" IsSortable="True" Width="Auto" />
    <DataGridColumn Key="Customer" Header="Customer" BindingPath="CustomerName" IsSortable="True" />
    <DataGridColumn Key="Total" Header="Total" BindingPath="Total" Type="Price" HorizontalTextAlignment="End" Width="Auto" />
</DataGrid>
```

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [DataGrid](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/data-grid.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop;

public partial class SqlResultView : UserControl
{
    public SqlResultView() => InitializeComponent();

    private void View_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => RebuildColumns(e.NewValue);
    private void View_Loaded(object sender, RoutedEventArgs e) => RebuildColumns(DataContext);

    private void RebuildColumns(object context)
    {
        // Inherited DataContext may arrive while the XAML children are being created.
        if (ResultGrid is null) return;
        ResultGrid.Columns.Clear();
        if (context is not SqlResultSnapshot result) return;
        if (result.IsExplain)
            ResultGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Plan indicators", Binding = new Binding(nameof(SqlResultRow.Indicators)),
                Width = new DataGridLength(220), MinWidth = 100, MaxWidth = 400
            });
        for (int index = 0; index < result.Page.Columns.Count; index++)
        {
            var column = result.Page.Columns[index];
            ResultGrid.Columns.Add(new DataGridTextColumn
            {
                Header = new TextBlock { Text = column.Name, ToolTip = column.SqlType },
                Binding = new Binding($"[{index}]") { Mode = BindingMode.OneWay },
                Width = new DataGridLength(150), MinWidth = 70, MaxWidth = 600
            });
        }
    }
}

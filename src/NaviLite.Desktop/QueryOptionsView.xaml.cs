using System.Windows;
using System.Windows.Controls;
using NaviLite.Desktop.ViewModels;

namespace NaviLite.Desktop;

public partial class QueryOptionsView : UserControl
{
    public QueryOptionsView() => InitializeComponent();

    private void RemoveQueryRow_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel owner || sender is not FrameworkElement control) return;
        if (control.DataContext is FilterDraft filter) owner.RemoveFilter(filter);
        else if (control.DataContext is SortDraft sort) owner.RemoveSort(sort);
    }

    private void MoveSort_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel owner && sender is FrameworkElement { DataContext: SortDraft sort, Tag: string value } && int.TryParse(value, out int delta))
            owner.MoveSort(sort, delta);
    }
}

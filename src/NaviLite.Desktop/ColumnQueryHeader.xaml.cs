using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NaviLite.Desktop.ViewModels;

namespace NaviLite.Desktop;

public partial class ColumnQueryHeader : UserControl
{
    public ColumnQueryHeader() => InitializeComponent();
    private ColumnQueryHeaderViewModel? Model => DataContext as ColumnQueryHeaderViewModel;
    private void Header_Unloaded(object sender, RoutedEventArgs e) => FilterButton.IsChecked = false;

    private async void Sort_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Model is { } model) await model.Owner.SortByAsync(model.Name, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }
    private void Operator_Click(object sender, RoutedEventArgs e) => FilterButton.IsChecked = true;
    private void AddCondition_Click(object sender, RoutedEventArgs e) { if (Model is { } model) model.Owner.AddColumnFilter(model.Name); }
    private void ClearConditions_Click(object sender, RoutedEventArgs e) { if (Model is { } model) model.Owner.ClearColumnFilters(model.Name); }
    private void RemoveCondition_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { } model && sender is FrameworkElement { DataContext: FilterDraft filter }) model.Owner.RemoveFilter(filter);
    }
    private void Apply_Click(object sender, RoutedEventArgs e) => FilterButton.IsChecked = false;
    private void QuickFilter_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Model is not { } model) return;
        e.Handled = true;
        if (model.Owner.ApplyQueryCommand.CanExecute(null)) model.Owner.ApplyQueryCommand.Execute(null);
    }
}

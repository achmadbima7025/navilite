using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NaviLite.Desktop.ViewModels;
using NaviLite.Desktop.Models;
using NaviLite.Desktop.Services;

namespace NaviLite.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _clearingPassword;
    private bool _canClose;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.CellInspectionRequested += inspection => new CellInspectorWindow(inspection) { Owner = this }.ShowDialog();
        _viewModel.RowsCopyRequested += text => Clipboard.SetText(text);
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.Page)) RebuildGridColumns();
        };
        _viewModel.PasswordClearRequested += () =>
        {
            _clearingPassword = true;
            ConnectionPassword.Clear();
            _clearingPassword = false;
        };
        _viewModel.PasswordRestoreRequested += password =>
        {
            _clearingPassword = true;
            ConnectionPassword.Password = password;
            _clearingPassword = false;
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    private void RebuildGridColumns()
    {
        PreviewGrid.Columns.Clear();
        if (_viewModel.Page is not { } page) return;
        for (int index = 0; index < page.Columns.Count; index++)
        {
            var column = page.Columns[index];
            var header = new TextBlock { Text = column.Name, ToolTip = column.SqlType };
            PreviewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"[{index}]") { Mode = BindingMode.OneWay },
                SortMemberPath = column.Name,
                SortDirection = _viewModel.AppliedSortColumn == column.Name
                    ? _viewModel.AppliedSortDirection == "desc" ? ListSortDirection.Descending : ListSortDirection.Ascending
                    : null,
                Width = new DataGridLength(150),
                MinWidth = 70,
                MaxWidth = 600
            });
        }
    }

    private async void PreviewGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        await _viewModel.SortByAsync(e.Column.SortMemberPath);
    }

    private async void PreviewGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && PreviewGrid.CurrentCell.Item is NativeRow row && PreviewGrid.CurrentCell.Column is { } column)
        {
            e.Handled = true;
            await _viewModel.InspectCellAsync(row, column.SortMemberPath);
        }
    }

    private async void PreviewGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not DataGridCell)
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        if (source is DataGridCell { DataContext: NativeRow row } cell)
        {
            e.Handled = true;
            await _viewModel.InspectCellAsync(row, cell.Column.SortMemberPath);
        }
    }

    private async void InspectFormField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FormField field } && _viewModel.SelectedRecord is { } row)
            await _viewModel.InspectCellAsync(row, field.Name);
    }

    private async void CopyRows_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string formatName } || !Enum.TryParse<RowCopyFormat>(formatName, out var format)) return;
        IEnumerable<NativeRow> selection = _viewModel.DataViewIndex == 0
            ? PreviewGrid.SelectedItems.OfType<NativeRow>().ToArray()
            : _viewModel.SelectedRecord is { } row ? [row] : [];
        await _viewModel.CopyRowsAsync(selection, format);
    }

    private async void Schema_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: SchemaNode node })
        {
            e.Handled = true;
            await _viewModel.ExpandNodeAsync(node);
        }
    }

    private void Schema_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => _viewModel.SelectedNode = e.NewValue as SchemaNode;

    private async void Schema_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedNode?.CanOpen == true)
        {
            e.Handled = true;
            await _viewModel.OpenSelectedAsync();
        }
    }

    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (!_clearingPassword) _viewModel.SetPassword(((PasswordBox)sender).Password);
    }

    private void BrowseCertificate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a trusted CA certificate",
            Filter = "Certificates (*.pem;*.crt;*.cer;*.der)|*.pem;*.crt;*.cer;*.der|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true) _viewModel.SetCertificate(dialog.FileName);
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        try
        {
            await _viewModel.WaitForIdleAsync();
            await _viewModel.DisconnectAsync();
        }
        catch (Exception) { /* Process exit releases any remaining native sockets. */ }
        finally { _canClose = true; Close(); }
    }
}

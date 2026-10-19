using System.ComponentModel;
using System.IO;
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
        _viewModel.DefinitionCopyRequested += text => Clipboard.SetText(text);
        _viewModel.SqlFormattingRequested += ApplySqlFormatting;
        _viewModel.SqlSelectionRequested += span => Dispatcher.BeginInvoke(new Action(() =>
        {
            ScriptEditor.Select(span.StartUtf16, span.LengthUtf16);
            ScriptEditor.ScrollToLine(span.StartLine - 1);
            ScriptEditor.Focus();
        }));
        _viewModel.ConnectionImportSelectionRequested += preview =>
        {
            var dialog = new ConnectionImportWindow(preview) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.SelectedIndices : null;
        };
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
            int priority = _viewModel.SortPriority(column.Name);
            var sort = _viewModel.AppliedSort(column.Name);
            var header = new TextBlock { Text = priority > 0 ? $"{column.Name} · #{priority}" : column.Name, ToolTip = column.SqlType };
            PreviewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"[{index}]") { Mode = BindingMode.OneWay },
                SortMemberPath = column.Name,
                SortDirection = sort is not null
                    ? sort.Direction == "desc" ? ListSortDirection.Descending : ListSortDirection.Ascending
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

    private void RemoveQueryRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement control) return;
        if (control.DataContext is FilterDraft filter) _viewModel.RemoveFilter(filter);
        else if (control.DataContext is SortDraft sort) _viewModel.RemoveSort(sort);
    }

    private void MoveSort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SortDraft sort, Tag: string value } && int.TryParse(value, out int delta))
            _viewModel.MoveSort(sort, delta);
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
        if (_viewModel.SelectedNode is { CanInspectDefinition: true, Kind: not "database" })
        {
            e.Handled = true;
            await _viewModel.OpenSelectedAsync();
        }
    }

    private async void SaveDefinition_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanExportDefinition || _viewModel.Definition is not { } definition) return;
        string stem = string.Concat($"{definition.Database}_{definition.Name}".EnumerateRunes().Take(80));
        foreach (char invalid in Path.GetInvalidFileNameChars()) stem = stem.Replace(invalid, '_');
        var dialog = new SaveFileDialog
        {
            Title = $"Save definition of {definition.Database}.{definition.Name}",
            Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
            DefaultExt = ".sql",
            FileName = $"definition_{stem}.sql",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.SaveDefinitionAsync(dialog.FileName, definition);
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

    private async void ImportConnections_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanEdit) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import connection metadata",
            Filter = "NaviLite connections (*.nvconn.json)|*.nvconn.json|JSON files (*.json)|*.json",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.ImportConnectionsAsync(dialog.FileName);
    }

    private async void ExportConnections_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanExportConnections) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export saved connection metadata",
            Filter = "NaviLite connections (*.nvconn.json)|*.nvconn.json|All files (*.*)|*.*",
            DefaultExt = ".nvconn.json",
            FileName = "connections.nvconn.json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.ExportConnectionsAsync(dialog.FileName);
    }

    private void ScriptEditor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        var editor = (TextBox)sender;
        _viewModel.SqlCaretUtf16 = editor.CaretIndex;
        _viewModel.SqlSelectionStartUtf16 = editor.SelectionStart;
        _viewModel.SqlSelectionLengthUtf16 = editor.SelectionLength;
    }

    private void ApplySqlFormatting(SqlFormatEdit edit, bool preserveSelection)
    {
        ScriptEditor.BeginChange();
        try
        {
            ScriptEditor.Select(edit.StartUtf16, edit.LengthUtf16);
            ScriptEditor.SelectedText = edit.Replacement;
            if (preserveSelection) ScriptEditor.Select(edit.StartUtf16, edit.Replacement.Length);
            else ScriptEditor.Select(edit.CaretUtf16, 0);
        }
        finally { ScriptEditor.EndChange(); }
        ScriptEditor.Focus();
        ScriptEditor.ScrollToLine(Math.Max(0, ScriptEditor.GetLineIndexFromCharacterIndex(ScriptEditor.CaretIndex)));
    }

    private async void NewSql_Click(object sender, RoutedEventArgs e) => await NewSqlAsync();
    private async void OpenSql_Click(object sender, RoutedEventArgs e) => await OpenSqlAsync();
    private async void SaveSql_Click(object sender, RoutedEventArgs e) => await SaveSqlAsync();

    private async Task NewSqlAsync()
    {
        if (_viewModel.CanUseSqlEditor && await ConfirmSqlReplacementAsync()) _viewModel.NewSqlDocument();
    }

    private async Task OpenSqlAsync()
    {
        if (!_viewModel.CanUseSqlEditor) return;
        var dialog = new OpenFileDialog
        {
            Title = "Open UTF-8 SQL script",
            Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true && await ConfirmSqlReplacementAsync())
            await _viewModel.OpenSqlFileAsync(dialog.FileName);
    }

    private async Task<bool> SaveSqlAsync()
    {
        if (!_viewModel.CanUseSqlEditor) return false;
        var dialog = new SaveFileDialog
        {
            Title = "Save UTF-8 SQL script",
            Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
            DefaultExt = ".sql",
            FileName = _viewModel.SqlPath ?? "Untitled.sql",
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(this) == true && await _viewModel.SaveSqlFileAsync(dialog.FileName);
    }

    private async Task<bool> ConfirmSqlReplacementAsync()
    {
        if (!_viewModel.IsSqlDirty) return true;
        var choice = MessageBox.Show(this, "Save changes to the SQL document before continuing?", "Unsaved SQL document", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || (choice == MessageBoxResult.Yes && await SaveSqlAsync());
    }

    private async void SqlWorkspace_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Key is not (Key.N or Key.O or Key.S)) return;
        e.Handled = true;
        if (e.Key == Key.N) await NewSqlAsync();
        else if (e.Key == Key.O) await OpenSqlAsync();
        else await SaveSqlAsync();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        if (_viewModel.StopSqlBatchCommand.CanExecute(null)) _viewModel.StopSqlBatchCommand.Execute(null);
        IsEnabled = false;
        try
        {
            await _viewModel.WaitForIdleAsync();
            if (!await ConfirmSqlReplacementAsync())
            {
                _closing = false;
                IsEnabled = true;
                return;
            }
        }
        catch (Exception)
        {
            _closing = false;
            IsEnabled = true;
            MessageBox.Show(this, "The SQL document could not be prepared for closing. Save or discard its changes and try again.", "Unable to close", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { await _viewModel.DisconnectAsync(); }
        catch (Exception) { /* Process exit releases any remaining native sockets. */ }
        _canClose = true;
        Close();
    }
}

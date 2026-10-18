using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop;

public partial class CellInspectorWindow : Window
{
    private readonly CellValue _value;
    private bool _jsonAttempted;
    private bool _ready;

    public CellInspectorWindow(CellInspection inspection)
    {
        InitializeComponent();
        _value = inspection.Value;
        Title = $"Cell value · {inspection.Title}";
        ValueTitle.Text = inspection.Title;
        ValueSummary.Text = $"{inspection.SqlType} · {_value.Bytes.Length:N0} bytes loaded" +
            (_value.OriginalBytes is { } original ? $" of {original:N0}" : " · total size unknown") + " · Read only";
        ValueSource.Text = inspection.Source;
        ValueWarning.Text = _value.IsTruncated ? "Partial value. Only the loaded bytes are shown, copied, or saved. Server reads are limited to 1 MiB." : "";
        ValueWarning.Visibility = _value.IsTruncated ? Visibility.Visible : Visibility.Collapsed;
        TextTab.IsEnabled = !_value.IsBinary;
        JsonTab.IsEnabled = !_value.IsNull && !_value.IsBinary && !_value.IsTruncated;
        HexTab.IsEnabled = !_value.IsNull;
        TextValue.Text = _value.IsBinary ? "" : _value.Text;
        HexLines.ItemsSource = Enumerable.Range(0, (_value.Bytes.Length + 15) / 16).Select(index => new HexLine(_value.Bytes, index * 16)).ToArray();
        SaveButton.IsEnabled = !_value.IsNull;
        ValueTabs.SelectedIndex = _value.IsBinary ? 2 : 0;
        _ready = true;
    }

    private void ValueTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || e.OriginalSource != ValueTabs || ValueTabs.SelectedIndex != 1 || _jsonAttempted) return;
        _jsonAttempted = true;
        if (_value.Bytes.Length > 256 * 1024)
        {
            JsonMessage.Text = "Formatting is limited to 256 KiB. Use Text for the loaded value.";
            return;
        }
        try
        {
            using var document = JsonDocument.Parse(_value.Bytes, new JsonDocumentOptions { MaxDepth = 64 });
            JsonValue.Text = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            JsonMessage.Text = "Formatted for display. Save loaded bytes preserves the original value.";
        }
        catch (JsonException error) { JsonMessage.Text = $"JSON cannot be formatted: {error.Message}"; }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string text = ValueTabs.SelectedIndex == 1 && JsonValue.Text.Length > 0 ? JsonValue.Text : _value.Text;
            Clipboard.SetText(text);
            ActionStatus.Text = _value.IsTruncated ? "Loaded portion copied." : "Value copied.";
        }
        catch (Exception error) { ActionStatus.Text = $"Copy failed: {error.Message}"; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = _value.IsTruncated ? "Save loaded portion of cell" : "Save cell value",
            FileName = _value.IsBinary ? "cell-value.bin" : "cell-value.txt",
            Filter = _value.IsBinary ? "Binary files (*.bin)|*.bin|All files (*.*)|*.*" : "Text files (*.txt)|*.txt|JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        SaveButton.IsEnabled = false;
        try
        {
            await File.WriteAllBytesAsync(dialog.FileName, _value.Bytes);
            ActionStatus.Text = $"Saved {_value.Bytes.Length:N0} loaded bytes.";
        }
        catch (Exception error) { ActionStatus.Text = $"Save failed: {error.Message}"; }
        finally { SaveButton.IsEnabled = true; }
    }

    private sealed class HexLine(byte[] bytes, int offset)
    {
        public string Display
        {
            get
            {
                var builder = new StringBuilder(80).Append(offset.ToString("X8")).Append("  ");
                int count = Math.Min(16, bytes.Length - offset);
                for (int index = 0; index < 16; index++)
                    builder.Append(index < count ? bytes[offset + index].ToString("X2") : "  ").Append(' ');
                builder.Append(" ");
                for (int index = 0; index < count; index++)
                    builder.Append(bytes[offset + index] is >= 32 and <= 126 ? (char)bytes[offset + index] : '.');
                return builder.ToString();
            }
        }
    }
}

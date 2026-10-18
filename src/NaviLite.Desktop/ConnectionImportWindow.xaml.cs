using System.ComponentModel;
using System.Windows;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop;

public partial class ConnectionImportWindow : Window
{
    public IReadOnlyList<ImportSelectionRow> Rows { get; }
    public IReadOnlyList<int> SelectedIndices { get; private set; } = [];

    public ConnectionImportWindow(ConnectionImportPreview preview)
    {
        InitializeComponent();
        Rows = preview.Candidates.Select((candidate, index) => new ImportSelectionRow(candidate, index)).ToArray();
        foreach (var row in Rows) row.PropertyChanged += (_, _) => RefreshSelection();
        DataContext = this;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        int count = Rows.Count(row => row.Selected && row.CanSelect);
        int duplicates = Rows.Count(row => !row.CanSelect);
        SelectionSummary.Text = $"{count} selected · {duplicates} duplicates · {Rows.Count} in file";
        ImportButton.IsEnabled = count > 0;
    }

    private void SelectNew_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows) row.Selected = row.CanSelect;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows) row.Selected = false;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        SelectedIndices = Rows.Where(row => row.CanSelect && row.Selected).Select(row => row.Index).ToArray();
        if (SelectedIndices.Count > 0) DialogResult = true;
    }
}

public sealed class ImportSelectionRow(ConnectionImportCandidate candidate, int index) : INotifyPropertyChanged
{
    private bool _selected = !candidate.Duplicate;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ConnectionImportCandidate Candidate { get; } = candidate;
    public int Index { get; } = index;
    public bool CanSelect => !Candidate.Duplicate;
    public string Status => Candidate.Duplicate ? "Already saved / duplicate" : Candidate.Connection.Name != Candidate.PlannedName ? "New · renamed" : "New connection";
    public string Details => $"Original name: {Candidate.Connection.Name}\nCA certificate: {Candidate.Connection.CaCertificate ?? "system trust roots"}";
    public bool Selected
    {
        get => _selected;
        set
        {
            bool next = value && CanSelect;
            if (_selected == next) return;
            _selected = next;
            PropertyChanged?.Invoke(this, new(nameof(Selected)));
        }
    }
}

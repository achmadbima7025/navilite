using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NaviLite.Desktop.ViewModels;

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

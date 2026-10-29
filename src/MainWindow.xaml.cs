using System.Diagnostics;
using System.Windows;
using StreamKeeper.Services;
using StreamKeeper.ViewModels;
using Wpf.Ui.Controls;

namespace StreamKeeper;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        // Apply the saved theme before the window is shown
        ThemeService.Apply(SettingsStore.Current.Theme);

        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        // On close: stop every running recording
        Closing += (_, _) => _vm.StopAll();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this }.ShowDialog();
    }

    private void ReportIssue_Click(object sender, RoutedEventArgs e) => OpenLink(AppInfo.IssuesUrl);
    private void Support_Click(object sender, RoutedEventArgs e) => OpenLink(AppInfo.SupportUrl);

    // "List" menu
    private void ExportList_Click(object sender, RoutedEventArgs e) => _vm.ExportListCommand.Execute(null);
    private void ImportList_Click(object sender, RoutedEventArgs e) => _vm.ImportListCommand.Execute(null);

    private static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}

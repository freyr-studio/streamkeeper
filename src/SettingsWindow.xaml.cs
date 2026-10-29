using System.ComponentModel;
using System.Windows;
using StreamKeeper.ViewModels;
using Wpf.Ui.Controls;

namespace StreamKeeper;

public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsViewModel _vm = new();
    private bool _saved;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _vm.SaveSettings();
        _saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    // Fermeture sans sauvegarder (Cancel ou croix) : on annule l'aperçu du thème
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_saved) _vm.Cancel();
        base.OnClosing(e);
    }
}
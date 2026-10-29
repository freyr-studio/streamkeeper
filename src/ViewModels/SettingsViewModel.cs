using StreamKeeper.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace StreamKeeper.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _outputFolder;
    [ObservableProperty] private string _selectedTheme;

    public string[] Themes => ThemeService.Themes;

    public SettingsViewModel()
    {
        var s = SettingsStore.Current;
        _outputFolder = s.OutputFolder;
        _selectedTheme = s.Theme;
    }

    // Aperçu en direct du thème
    partial void OnSelectedThemeChanged(string value) => ThemeService.Apply(value);

    [RelayCommand]
    private void BrowseOutput()
    {
        var dialog = new OpenFolderDialog { Title = "Recording folder", InitialDirectory = OutputFolder };
        if (dialog.ShowDialog() == true) OutputFolder = dialog.FolderName;
    }

    public void SaveSettings()
    {
        var s = SettingsStore.Current;
        s.OutputFolder = OutputFolder;
        s.Theme = SelectedTheme;
        SettingsStore.Save();

        Recorder.OutputRoot = OutputFolder; // pris en compte aux prochains enregistrements
    }

    // Annuler : on remet le thème d'origine
    public void Cancel() => ThemeService.Apply(SettingsStore.Current.Theme);
}

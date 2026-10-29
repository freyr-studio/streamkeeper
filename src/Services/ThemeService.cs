using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace StreamKeeper.Services;

public static class ThemeService
{
    public static readonly string[] Themes = { "Midnight", "Dark Purple", "Ocean", "Slate" };

    public static void Apply(string theme)
    {
        if (!Themes.Contains(theme)) theme = "Midnight";

        var fileName = theme.Replace(" ", "");   // "Dark Purple" → "DarkPurple"
        var newDict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{fileName}.xaml")
        };

        // On remplace le thème actuel (celui qui contient AppBackgroundBrush)
        var dicts = Application.Current.Resources.MergedDictionaries;
        var current = dicts.FirstOrDefault(d => d.Contains("AppBackgroundBrush"));
        if (current != null) dicts[dicts.IndexOf(current)] = newDict;
        else dicts.Add(newDict);

        // Couleur d'accent de WPF-UI (boutons Primary, interrupteurs…)
        if (newDict["AccentBrush"] is SolidColorBrush accent)
            ApplicationAccentColorManager.Apply(accent.Color, ApplicationTheme.Dark);
    }
}
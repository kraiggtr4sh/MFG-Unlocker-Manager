using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace MFGUnlockerManager;

public partial class App : Application
{
    public static readonly Color Win11Blue = Color.FromRgb(0x00, 0x67, 0xC0);

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            File.WriteAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui_error.log"),
                e.Exception.ToString());
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyWin11BlueAccent(ApplicationTheme.Dark);
    }

    public static void ApplyWin11BlueAccent(ApplicationTheme theme)
    {
        ApplicationThemeManager.Apply(theme, updateAccent: false);
        ApplicationAccentColorManager.Apply(Win11Blue, theme);

        if (Current?.Resources != null)
        {
            Current.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
            Current.Resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x19, 0x75, 0xC5));
            Current.Resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0x5A, 0x9E));
            Current.Resources["TextOnAccentFillColorPrimaryBrush"] = new SolidColorBrush(Colors.White);
            Current.Resources["TextOnAccentFillColorSecondaryBrush"] = new SolidColorBrush(Colors.White);
        }
    }
}

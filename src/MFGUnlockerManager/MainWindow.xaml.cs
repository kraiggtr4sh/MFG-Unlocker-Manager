using System.Windows;
using MFGUnlockerManager.ViewModels;
using Wpf.Ui.Controls;

namespace MFGUnlockerManager;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            App.ApplyWin11BlueAccent(
                ViewModel.SettingsIsDarkTheme
                    ? Wpf.Ui.Appearance.ApplicationTheme.Dark
                    : Wpf.Ui.Appearance.ApplicationTheme.Light);
            await ViewModel.InitializeAsync();
        };
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            ViewModel.HandleDroppedFileOrFolder(files[0]);
        }
    }
}

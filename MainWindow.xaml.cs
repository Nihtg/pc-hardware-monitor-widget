using System;
using System.Windows;
using System.Windows.Input;
using MiniHardwareWidget.ViewModels;

namespace MiniHardwareWidget;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        App.LogDebug("MainWindow constructor BEGIN");
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        App.LogDebug("MainWindow constructor END");
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        App.LogDebug("MainWindow_Loaded BEGIN");
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Top + 24;
        App.LogDebug($"MainWindow_Loaded END (Left={Left}, Top={Top})");
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch { }
        }
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ToggleAlwaysOnTop();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("MainWindow CloseButton_Click");
        Close();
    }

    private void ResetEnergy_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ResetWattHours();
        }
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        MainViewModel.RestartAsAdmin();
    }

    private void AdminBadge_Click(object sender, MouseButtonEventArgs e)
    {
        MainViewModel.RestartAsAdmin();
    }

    protected override void OnClosed(EventArgs e)
    {
        App.LogDebug("MainWindow OnClosed");
        base.OnClosed(e);
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

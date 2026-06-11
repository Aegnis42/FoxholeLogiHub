using System.Windows;
using System.Windows.Input;
using FoxholeLogiHub.App.ViewModels;

namespace FoxholeLogiHub.App;

/// <summary>Panneau overlay : demandes prises en charge (avec bouton « Livré »).</summary>
public partial class OverlayTakenWindow : Window
{
    private readonly MainViewModel _vm;

    public event Action? CloseRequested;

    public OverlayTakenWindow(MainViewModel vm, double? left, double? top)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left ?? SystemParameters.WorkArea.Right - 330 - Width - 32;
        Top = top ?? SystemParameters.WorkArea.Top + 360;
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private async void OnDeliverRequest(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ResupplyRequestViewModel req)
            await _vm.Resupply.DoneAsync(req);
    }
}

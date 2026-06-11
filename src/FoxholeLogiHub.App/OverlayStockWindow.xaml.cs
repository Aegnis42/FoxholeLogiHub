using System.Windows;
using System.Windows.Input;
using FoxholeLogiHub.App.ViewModels;

namespace FoxholeLogiHub.App;

/// <summary>Panneau overlay : contenu d'un stockpile au choix (menu déroulant).</summary>
public partial class OverlayStockWindow : Window
{
    public event Action? CloseRequested;

    public OverlayStockWindow(MainViewModel vm, double? left, double? top)
    {
        InitializeComponent();
        DataContext = vm;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left ?? SystemParameters.WorkArea.Right - Width - 16;
        Top = top ?? SystemParameters.WorkArea.Top + 360;
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}

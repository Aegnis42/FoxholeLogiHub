using System.Windows;
using System.Windows.Input;
using FoxholeLogiHub.App.ViewModels;

namespace FoxholeLogiHub.App;

/// <summary>Panneau overlay : demandes de ravitaillement ouvertes + création rapide.</summary>
public partial class OverlayResupplyWindow : Window
{
    private readonly MainViewModel _vm;

    public event Action? CloseRequested;

    public OverlayResupplyWindow(MainViewModel vm, double? left, double? top)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left ?? SystemParameters.WorkArea.Right - 330 - Width - 32;
        Top = top ?? SystemParameters.WorkArea.Top + 16;
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnAddDraftItem(object sender, RoutedEventArgs e) => _vm.Resupply.AddDraftItem();

    private async void OnCreateRequest(object sender, RoutedEventArgs e) => await _vm.Resupply.CreateFromFormAsync();

    private async void OnClaimRequest(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ResupplyRequestViewModel req)
            await _vm.Resupply.ClaimAsync(req);
    }
}

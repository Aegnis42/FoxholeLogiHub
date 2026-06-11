using System.Windows;
using System.Windows.Input;
using FoxholeLogiHub.App.ViewModels;

namespace FoxholeLogiHub.App;

/// <summary>
/// Overlay compact toujours-au-dessus : alertes de stock, comptes à rebours MPF et recherche
/// rapide, visibles par-dessus le jeu (en mode fenêtré sans bordure). Basculé par F9 ou le
/// bouton de la barre latérale ; la position est mémorisée.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly MainViewModel _vm;

    /// <summary>Demande de fermeture (croix) — le propriétaire gère l'état et la persistance.</summary>
    public event Action? CloseRequested;

    /// <summary>Bascule d'un panneau ("stock" | "resupply" | "taken") — géré par MainWindow.</summary>
    public event Action<string>? PanelToggleRequested;

    public OverlayWindow(MainViewModel vm, double? left, double? top)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        if (left is double l && top is double t)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        else
        {
            // Par défaut : coin haut-droit de l'écran de travail.
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Right - Width - 16;
            Top = SystemParameters.WorkArea.Top + 16;
        }
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnTogglePanelStock(object sender, RoutedEventArgs e) => PanelToggleRequested?.Invoke("stock");
    private void OnTogglePanelResupply(object sender, RoutedEventArgs e) => PanelToggleRequested?.Invoke("resupply");
    private void OnTogglePanelTaken(object sender, RoutedEventArgs e) => PanelToggleRequested?.Invoke("taken");

    private async void OnSearchClick(object sender, RoutedEventArgs e) =>
        await _vm.Stockpiles.SearchItemsAsync();

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await _vm.Stockpiles.SearchItemsAsync();
    }
}

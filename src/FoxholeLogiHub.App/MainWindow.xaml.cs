using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FoxholeLogiHub.App.ViewModels;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const int HotkeyId = 0xF0C;   // identifiant arbitraire
    private const int WmHotkey = 0x0312;
    private const uint VkF8 = 0x77;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly MainViewModel _vm = new();
    private HwndSource? _source;
    private IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, _) => _vm.Load();

        // Ajuste la vue de la carte à sa première apparition (le viewport n'est mesuré
        // qu'une fois l'onglet visible).
        MapViewport.IsVisibleChanged += (_, _) =>
        {
            if (MapViewport.IsVisible && !_mapViewInitialized)
            {
                _mapViewInitialized = true;
                Dispatcher.BeginInvoke(new Action(ResetMapView), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(HwndHook);
        RegisterHotKey(_hwnd, HotkeyId, 0, VkF8); // F8 global → import par capture
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            _ = _vm.Stockpiles.ImportFromCaptureAsync(0); // capture immédiate de la fenêtre au 1er plan (le jeu)
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwnd != IntPtr.Zero)
            UnregisterHotKey(_hwnd, HotkeyId);
        _source?.RemoveHook(HwndHook);
        _vm.Companion.Dispose();
        base.OnClosed(e);
    }

    private async void OnWarReset(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Fin de guerre : TOUS les stockpiles du régiment (avec leur contenu) et TOUTES les demandes de ravitaillement seront supprimés.\n\n" +
            "Une archive JSON sera d'abord enregistrée localement (dossier archives).\n\nContinuer ?",
            "Fin de guerre", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        string? archive = await _vm.WarResetAsync();
        if (archive is not null)
            MessageBox.Show($"Archive enregistrée :\n{archive}", "Fin de guerre",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------- Carte du monde (pan/zoom/sélection) ----------

    private bool _mapPanning, _mapMoved, _mapViewInitialized;
    private Point _mapStart;
    private double _mapStartTx, _mapStartTy;

    private void OnNavMap(object sender, RoutedEventArgs e) => _vm.ShowMap();

    private void OnMapReset(object sender, RoutedEventArgs e) => ResetMapView();

    private void ResetMapView()
    {
        if (_vm.Map.CanvasWidth <= 0 || MapViewport.ActualWidth <= 0 || MapViewport.ActualHeight <= 0)
            return;
        double scale = Math.Min(MapViewport.ActualWidth / _vm.Map.CanvasWidth,
                                MapViewport.ActualHeight / _vm.Map.CanvasHeight) * 0.97;
        MapScale.ScaleX = MapScale.ScaleY = scale;
        MapTranslate.X = (MapViewport.ActualWidth - _vm.Map.CanvasWidth * scale) / 2;
        MapTranslate.Y = (MapViewport.ActualHeight - _vm.Map.CanvasHeight * scale) / 2;
    }

    private void OnMapWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        double newScale = Math.Clamp(MapScale.ScaleX * factor, 0.15, 5.0);
        factor = newScale / MapScale.ScaleX;
        Point m = e.GetPosition(MapViewport);
        // garde le point sous le curseur fixe pendant le zoom
        MapTranslate.X = m.X - factor * (m.X - MapTranslate.X);
        MapTranslate.Y = m.Y - factor * (m.Y - MapTranslate.Y);
        MapScale.ScaleX = MapScale.ScaleY = newScale;
        e.Handled = true;
    }

    private void OnMapMouseDown(object sender, MouseButtonEventArgs e)
    {
        _mapPanning = true;
        _mapMoved = false;
        _mapStart = e.GetPosition(MapViewport);
        _mapStartTx = MapTranslate.X;
        _mapStartTy = MapTranslate.Y;
        MapViewport.CaptureMouse();
    }

    private void OnMapMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mapPanning)
            return;
        Point p = e.GetPosition(MapViewport);
        Vector d = p - _mapStart;
        if (Math.Abs(d.X) > 4 || Math.Abs(d.Y) > 4)
            _mapMoved = true;
        if (_mapMoved)
        {
            MapTranslate.X = _mapStartTx + d.X;
            MapTranslate.Y = _mapStartTy + d.Y;
        }
    }

    private void OnMapMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mapPanning)
            return;
        _mapPanning = false;
        MapViewport.ReleaseMouseCapture();
        if (_mapMoved)
            return;

        // Clic simple : la souris est capturée, donc on hit-teste manuellement sous le curseur.
        var hit = VisualTreeHelper.HitTest(MapRoot, e.GetPosition(MapRoot));
        object? data = (hit?.VisualHit as FrameworkElement)?.DataContext;
        MapHexViewModel? hex = data switch
        {
            MapHexViewModel h => h,
            MapTownViewModel t => t.Hex,
            MapPinViewModel p => p.Hex,
            _ => null,
        };
        if (hex is not null)
            _vm.Map.Select(hex);
    }

    private void OnNavDashboard(object sender, RoutedEventArgs e) => _vm.ShowDashboard();
    private void OnNavProfile(object sender, RoutedEventArgs e) => _vm.ShowProfile();
    private void OnNavFriends(object sender, RoutedEventArgs e) => _vm.ShowFriends();
    private void OnNavRegiment(object sender, RoutedEventArgs e) => _vm.ShowRegiment();
    private void OnNavStockpiles(object sender, RoutedEventArgs e) => _vm.ShowStockpiles();
    private void OnNavResupply(object sender, RoutedEventArgs e) => _vm.ShowResupply();
    private void OnNavTaken(object sender, RoutedEventArgs e) => _vm.ShowTaken();

    private async void OnCreateResupply(object sender, RoutedEventArgs e) => await _vm.Resupply.CreateFromFormAsync();
    private void OnAddDraftItem(object sender, RoutedEventArgs e) => _vm.Resupply.AddDraftItem();

    private void OnRemoveDraftItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResupplyItemLineViewModel item })
            _vm.Resupply.RemoveDraftItem(item);
    }

    private void OnCreateFromNeed(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResupplyNeedViewModel need })
            _vm.Resupply.AddDraftFromNeed(need);
    }

    private async void OnClaimResupply(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResupplyRequestViewModel r })
            await _vm.Resupply.ClaimAsync(r);
    }

    private async void OnDoneResupply(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResupplyRequestViewModel r })
            await _vm.Resupply.DoneAsync(r);
    }

    private async void OnReopenResupply(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResupplyRequestViewModel r })
            await _vm.Resupply.ReopenAsync(r);
    }

    private async void OnDeleteResupply(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResupplyRequestViewModel r })
            await _vm.Resupply.DeleteAsync(r);
    }

    private async void OnVisRegiment(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResupplyRequestViewModel r })
            await _vm.Resupply.SetVisibilityAsync(r, ResupplyVisibility.Regiment);
    }

    private async void OnVisAlliance(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResupplyRequestViewModel r })
            await _vm.Resupply.SetVisibilityAsync(r, ResupplyVisibility.Alliance);
    }

    private async void OnVisPublic(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResupplyRequestViewModel r })
            await _vm.Resupply.SetVisibilityAsync(r, ResupplyVisibility.Public);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _vm.Load();
    private void OnSaveProfileClick(object sender, RoutedEventArgs e) => _vm.SaveProfile();

    private async void OnSteamLoginClick(object sender, RoutedEventArgs e) =>
        await _vm.Friends.LoginWithSteamAsync();

    private async void OnLogoutClick(object sender, RoutedEventArgs e) =>
        await _vm.Friends.LogoutAsync();

    private async void OnSendRequestClick(object sender, RoutedEventArgs e) =>
        await _vm.Friends.SendRequestAsync();

    private async void OnRemoveFriendClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FriendItemViewModel friend })
            await _vm.Friends.RemoveFriendAsync(friend);
    }

    private async void OnAcceptRequestClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FriendRequestItemViewModel request })
            await _vm.Friends.AcceptRequestAsync(request);
    }

    private async void OnDeclineRequestClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FriendRequestItemViewModel request })
            await _vm.Friends.DeclineRequestAsync(request);
    }

    // --- Régiment ---

    private async void OnCreateRegiment(object sender, RoutedEventArgs e) => await _vm.Regiment.CreateAsync();
    private async void OnJoinRegiment(object sender, RoutedEventArgs e) => await _vm.Regiment.JoinAsync();
    private async void OnRegenCode(object sender, RoutedEventArgs e) => await _vm.Regiment.RegenerateCodeAsync();
    private async void OnCreateRole(object sender, RoutedEventArgs e) => await _vm.Regiment.CreateRoleAsync();
    private async void OnProposeAlliance(object sender, RoutedEventArgs e) => await _vm.Regiment.ProposeAllianceAsync();

    private async void OnLeaveRegiment(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Quitter ce régiment ?", "Confirmer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await _vm.Regiment.LeaveAsync();
    }

    private async void OnDeleteRegiment(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Supprimer définitivement le régiment ? Tous les membres seront retirés.", "Confirmer",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await _vm.Regiment.DeleteAsync();
    }

    private async void OnSaveRole(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RegimentRoleItemViewModel role })
            await _vm.Regiment.SaveRoleAsync(role);
    }

    private async void OnDeleteRole(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RegimentRoleItemViewModel role })
            await _vm.Regiment.DeleteRoleAsync(role);
    }

    private async void OnKickMember(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RegimentMemberItemViewModel member })
            await _vm.Regiment.KickAsync(member);
    }

    private async void OnMemberRoleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: RegimentMemberItemViewModel member } combo
            && combo.SelectedValue is int roleId && roleId != member.RoleId)
        {
            await _vm.Regiment.SetMemberRoleAsync(member.SteamId, roleId);
        }
    }

    private async void OnInviteFriendToReg(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FriendItemViewModel friend })
            await _vm.Regiment.InviteFriendAsync(friend.SteamId, friend.DisplayName);
    }

    private async void OnAcceptRegInvite(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RegimentInviteItemViewModel invite })
            await _vm.Regiment.RespondInviteAsync(invite, accept: true);
    }

    private async void OnDeclineRegInvite(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RegimentInviteItemViewModel invite })
            await _vm.Regiment.RespondInviteAsync(invite, accept: false);
    }

    private async void OnAcceptAlliance(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RegimentAllianceItemViewModel a })
            await _vm.Regiment.RespondAllianceAsync(a, accept: true);
    }

    private async void OnDeclineAlliance(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RegimentAllianceItemViewModel a })
            await _vm.Regiment.RespondAllianceAsync(a, accept: false);
    }

    private async void OnRemoveAlliance(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RegimentAllianceItemViewModel a })
            await _vm.Regiment.RemoveAllianceAsync(a);
    }

    // --- Stockpiles ---

    private async void OnSubmitStockpile(object sender, RoutedEventArgs e) => await _vm.Stockpiles.SubmitFormAsync();
    private void OnCancelStockpileEdit(object sender, RoutedEventArgs e) => _vm.Stockpiles.CancelEdit();

    private void OnEditStockpile(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StockpileItemViewModel s })
            _vm.Stockpiles.EditStockpile(s);
    }

    private async void OnDeleteStockpile(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StockpileItemViewModel s }
            && MessageBox.Show($"Supprimer le stockpile « {s.Name} » ?", "Confirmer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await _vm.Stockpiles.DeleteAsync(s);
    }

    private async void OnToggleShare(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StockpileShareTargetViewModel target })
            await _vm.Stockpiles.ToggleShareAsync(target);
    }

    private async void OnSelectStockpile(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StockpileItemViewModel s })
            await _vm.Stockpiles.SelectStockpileAsync(s);
    }

    private void OnCloseDetail(object sender, RoutedEventArgs e) => _vm.Stockpiles.CloseDetail();
    private async void OnSetItem(object sender, RoutedEventArgs e) => await _vm.Stockpiles.SetItemFromFormAsync();

    private async void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StockpileLineViewModel line })
            await _vm.Stockpiles.RemoveLineAsync(line);
    }

    private void OnEditItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StockpileLineViewModel line })
            _vm.Stockpiles.EditLine(line);
    }

    private void OnItemDetails(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StockpileLineViewModel line })
            _vm.Stockpiles.ShowItemDetail(line.Code);
    }

    private void OnCloseItemDetail(object sender, RoutedEventArgs e) => _vm.Stockpiles.CloseItemDetail();

    // Bouton : laisse 4 s pour basculer sur le jeu (panneau stockpile en vue-carte) avant la capture.
    private async void OnImportCapture(object sender, RoutedEventArgs e) =>
        await _vm.Stockpiles.ImportFromCaptureAsync(4);
}

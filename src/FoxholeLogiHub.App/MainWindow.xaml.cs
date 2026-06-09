using System.Windows;
using System.Windows.Controls;
using FoxholeLogiHub.App.ViewModels;

namespace FoxholeLogiHub.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, _) => _vm.Load();
    }

    private void OnNavProfile(object sender, RoutedEventArgs e) => _vm.ShowProfile();
    private void OnNavFriends(object sender, RoutedEventArgs e) => _vm.ShowFriends();
    private void OnNavRegiment(object sender, RoutedEventArgs e) => _vm.ShowRegiment();
    private void OnNavStockpiles(object sender, RoutedEventArgs e) => _vm.ShowStockpiles();

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
}

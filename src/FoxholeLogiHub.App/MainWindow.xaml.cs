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

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _vm.Load();
    private void OnSaveProfileClick(object sender, RoutedEventArgs e) => _vm.SaveProfile();

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
}

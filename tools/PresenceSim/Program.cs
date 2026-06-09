using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

// Simule un utilisateur connecté au hub de présence.
// Usage : PresenceSim <baseUrl> <steamId> [secondes]
string baseUrl = args.Length > 0 ? args[0] : "http://localhost:5080";
string steamId = args.Length > 1 ? args[1] : "76561190000000001";
int seconds = args.Length > 2 && int.TryParse(args[2], out int s) ? s : 30;

var hub = new HubConnectionBuilder()
    .WithUrl($"{baseUrl.TrimEnd('/')}/hub/presence?steamId={Uri.EscapeDataString(steamId)}")
    .Build();

hub.On<string, bool>(PresenceEvents.PresenceChanged, (id, online) =>
    Console.WriteLine($"  [presence] {id} -> {(online ? "EN LIGNE" : "hors ligne")}"));
hub.On<List<string>>(PresenceEvents.OnlineFriends, list =>
    Console.WriteLine($"  [amis en ligne] {string.Join(", ", list)}"));

await hub.StartAsync();
Console.WriteLine($"Connecté comme {steamId} sur {baseUrl} (état={hub.State}). Reste {seconds}s en ligne…");
await Task.Delay(TimeSpan.FromSeconds(seconds));
await hub.DisposeAsync();
Console.WriteLine("Déconnecté.");

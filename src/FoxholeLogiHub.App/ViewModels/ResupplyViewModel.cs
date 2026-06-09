using System.Collections.ObjectModel;
using FoxholeLogiHub.App.Services;
using FoxholeLogiHub.Contracts;
using FoxholeLogiHub.Core.Services;

namespace FoxholeLogiHub.App.ViewModels;

public sealed record ResupplyTargetOption(string Id, string Label);

/// <summary>Demandes de ravitaillement du régiment (création, prise en charge, suivi) + manques détectés.</summary>
public sealed class ResupplyViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore = new();
    private readonly TokenStore _tokenStore = new();
    private RegimentViewModel? _regiment;
    private ResupplyClient? _client;
    private StockpileClient? _stockClient;

    private bool _authed;
    private bool _busy;
    private string _status = "";

    private string _newItemName = "";
    private string _newQuantity = "";
    private string _newNote = "";
    private int _newPriority = ResupplyPriority.Normal;
    private string _newTargetId = "";

    public ObservableCollection<ResupplyRequestViewModel> Requests { get; } = new();
    public ObservableCollection<ResupplyNeedViewModel> Needs { get; } = new();
    public ObservableCollection<ResupplyTargetOption> Targets { get; } = new();
    public IReadOnlyList<string> ItemNames { get; } = FoxholeItemCatalog.Names;
    public IReadOnlyList<ResupplyPriorityOption> Priorities { get; } = new[]
    {
        new ResupplyPriorityOption(ResupplyPriority.Normal, "Normale"),
        new ResupplyPriorityOption(ResupplyPriority.High, "Haute"),
        new ResupplyPriorityOption(ResupplyPriority.Urgent, "Urgente"),
    };

    public bool Authed { get => _authed; private set { Set(ref _authed, value); RaiseViewFlags(); } }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public string NewItemName { get => _newItemName; set => Set(ref _newItemName, value); }
    public string NewQuantity { get => _newQuantity; set => Set(ref _newQuantity, value); }
    public string NewNote { get => _newNote; set => Set(ref _newNote, value); }
    public int NewPriority { get => _newPriority; set => Set(ref _newPriority, value); }
    public string NewTargetId { get => _newTargetId; set => Set(ref _newTargetId, value); }

    public bool HasRegiment => _regiment?.HasRegiment ?? false;
    public bool ShowAuthNeeded => !Authed;
    public bool ShowNoRegiment => Authed && !HasRegiment;
    public bool ShowResupply => Authed && HasRegiment;

    public int OpenCount => Requests.Count(r => r.Status == ResupplyStatus.Open);
    public int ClaimedCount => Requests.Count(r => r.Status == ResupplyStatus.Claimed);
    public bool HasRequests => Requests.Count > 0;
    public bool NoRequests => Requests.Count == 0;
    public bool HasNeeds => Needs.Count > 0;
    public string Summary => !Authed ? "Connecte-toi pour voir le ravitaillement."
        : !HasRegiment ? "Rejoins un régiment pour gérer le ravitaillement."
        : HasRequests ? $"{OpenCount} ouverte(s) · {ClaimedCount} en cours · {Requests.Count(r => r.Status == ResupplyStatus.Done)} livrée(s)"
        : "Aucune demande en cours.";

    private void RaiseViewFlags()
    {
        Raise(nameof(HasRegiment)); Raise(nameof(ShowAuthNeeded));
        Raise(nameof(ShowNoRegiment)); Raise(nameof(ShowResupply)); Raise(nameof(Summary));
    }

    private void RaiseCounts()
    {
        Raise(nameof(OpenCount)); Raise(nameof(ClaimedCount));
        Raise(nameof(HasRequests)); Raise(nameof(NoRequests));
        Raise(nameof(HasNeeds)); Raise(nameof(Summary));
    }

    public void Initialize(RegimentViewModel regiment) => _regiment = regiment;

    public void ClearAuth()
    {
        _client?.Dispose(); _client = null;
        _stockClient?.Dispose(); _stockClient = null;
        Authed = false;
        Requests.Clear(); Needs.Clear(); Targets.Clear();
        RaiseCounts();
        Status = "Connecte-toi avec Steam (onglet Amis).";
    }

    public async Task RefreshAsync()
    {
        string? token = _tokenStore.Load();
        if (token is null)
        {
            ClearAuth();
            return;
        }

        Busy = true;
        try
        {
            string baseUrl = _settingsStore.Load().ApiBaseUrl;
            _client?.Dispose(); _client = new ResupplyClient(baseUrl, token);
            _stockClient?.Dispose(); _stockClient = new StockpileClient(baseUrl, token);
            Authed = true;
            RaiseViewFlags();

            ApplyRequests(await _client.GetListAsync());
            await LoadNeedsAndTargetsAsync();
            Status = HasRegiment ? Summary : "Rejoins un régiment pour gérer le ravitaillement.";
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    private async Task LoadNeedsAndTargetsAsync()
    {
        if (_stockClient is null)
            return;
        try
        {
            var alerts = await _stockClient.GetAlertsAsync();
            Needs.Clear();
            foreach (var a in alerts.Where(a => a.IsOwn).OrderBy(a => a.Severity == "critical" ? 0 : 1).ThenBy(a => a.Name))
                Needs.Add(new ResupplyNeedViewModel(a));

            var sps = await _stockClient.GetListAsync();
            Targets.Clear();
            Targets.Add(new ResupplyTargetOption("", "(non précisé)"));
            foreach (var s in sps.Where(s => s.IsOwn))
                Targets.Add(new ResupplyTargetOption(s.Id, $"{s.Name} — {s.Hex}"));
            RaiseCounts();
        }
        catch { /* manques non bloquants */ }
    }

    private void ApplyRequests(List<ResupplyRequestDto> list)
    {
        Requests.Clear();
        foreach (var d in list)
            Requests.Add(new ResupplyRequestViewModel(d));
        RaiseCounts();
    }

    public async Task CreateFromFormAsync()
    {
        if (_client is null)
            return;
        if (string.IsNullOrWhiteSpace(NewItemName)) { Status = "Choisis un item."; return; }
        if (!int.TryParse(NewQuantity.Trim(), out int qty) || qty <= 0) { Status = "Quantité invalide."; return; }
        var cat = FoxholeItemCatalog.Resolve(NewItemName);
        Busy = true;
        try
        {
            ApplyRequests(await _client.CreateAsync(new CreateResupplyRequest(
                cat.Code, cat.Name, cat.Category, qty, NewTargetId ?? "", NewPriority, NewNote?.Trim() ?? "")));
            Status = $"Demande créée : {cat.Name} ×{qty}.";
            NewItemName = ""; NewQuantity = ""; NewNote = ""; NewPriority = ResupplyPriority.Normal; NewTargetId = "";
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    public async Task CreateFromNeedAsync(ResupplyNeedViewModel need)
    {
        if (_client is null)
            return;
        Busy = true;
        try
        {
            ApplyRequests(await _client.CreateAsync(new CreateResupplyRequest(
                need.Code, need.Name, need.Category, need.Deficit, need.StockpileId,
                need.IsCritical ? ResupplyPriority.Urgent : ResupplyPriority.Normal, "")));
            Status = $"Demande créée depuis le manque : {need.Name} ×{need.Deficit}.";
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    public Task ClaimAsync(ResupplyRequestViewModel r) => ActAsync(() => _client!.ClaimAsync(r.Id));
    public Task DoneAsync(ResupplyRequestViewModel r) => ActAsync(() => _client!.DoneAsync(r.Id));
    public Task ReopenAsync(ResupplyRequestViewModel r) => ActAsync(() => _client!.ReopenAsync(r.Id));
    public Task DeleteAsync(ResupplyRequestViewModel r) => ActAsync(() => _client!.DeleteAsync(r.Id));

    private async Task ActAsync(Func<Task<List<ResupplyRequestDto>>> action)
    {
        if (_client is null)
            return;
        Busy = true;
        try { ApplyRequests(await action()); }
        catch (FriendException fex) { Status = fex.Message; }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }
}

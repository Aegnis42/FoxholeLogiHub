using System.Collections.ObjectModel;
using FoxholeLogiHub.App.Services;
using FoxholeLogiHub.Contracts;
using FoxholeLogiHub.Core.Services;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>
/// Ravitaillement : demandes multi-items (nom + localisation) du régiment, prise en charge et suivi,
/// + manques détectés. Les demandes prises en charge ont un plan de production (crafts/récoltes/véhicules).
/// </summary>
public sealed class ResupplyViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore = new();
    private readonly TokenStore _tokenStore = new();
    private RegimentViewModel? _regiment;
    private StockpilesViewModel? _stockpiles;
    private ResupplyClient? _client;
    private string _clientKey = "";

    private bool _authed;
    private bool _busy;
    private string _status = "";

    private string _newTitle = "";
    private string _newHex = "";
    private string _newCoords = "";
    private string _newNote = "";
    private int _newPriority = ResupplyPriority.Normal;
    private int _newVisibility = ResupplyVisibility.Regiment;
    private string _draftItemName = "";
    private string _draftItemQuantity = "";

    public ObservableCollection<ResupplyItemLineViewModel> DraftItems { get; } = new();
    public ObservableCollection<ResupplyRequestViewModel> OpenRequests { get; } = new();
    public ObservableCollection<ResupplyRequestViewModel> TakenRequests { get; } = new();
    public ObservableCollection<ResupplyNeedViewModel> Needs { get; } = new();

    public IReadOnlyList<string> ItemNames { get; } = FoxholeItemCatalog.Names;
    public IReadOnlyList<string> Hexes { get; } = StockpileCatalog.Hexes;
    public IReadOnlyList<ResupplyPriorityOption> Priorities { get; } = new[]
    {
        new ResupplyPriorityOption(ResupplyPriority.Normal, "Normale"),
        new ResupplyPriorityOption(ResupplyPriority.High, "Haute"),
        new ResupplyPriorityOption(ResupplyPriority.Urgent, "Urgente"),
    };
    public IReadOnlyList<ResupplyVisibilityOption> Visibilities { get; } = new[]
    {
        new ResupplyVisibilityOption(ResupplyVisibility.Regiment, "🔒 Privé (régiment)"),
        new ResupplyVisibilityOption(ResupplyVisibility.Alliance, "🤝 Alliance"),
        new ResupplyVisibilityOption(ResupplyVisibility.Public, "🌍 Public"),
    };

    public bool Authed { get => _authed; private set { Set(ref _authed, value); RaiseViewFlags(); } }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public string NewTitle { get => _newTitle; set => Set(ref _newTitle, value); }
    public string NewHex { get => _newHex; set => Set(ref _newHex, value); }
    public string NewCoords { get => _newCoords; set => Set(ref _newCoords, value); }
    public string NewNote { get => _newNote; set => Set(ref _newNote, value); }
    public int NewPriority { get => _newPriority; set => Set(ref _newPriority, value); }
    public int NewVisibility { get => _newVisibility; set => Set(ref _newVisibility, value); }
    public string DraftItemName { get => _draftItemName; set => Set(ref _draftItemName, value); }
    public string DraftItemQuantity { get => _draftItemQuantity; set => Set(ref _draftItemQuantity, value); }

    public bool HasDraft => DraftItems.Count > 0;
    public bool HasRegiment => _regiment?.HasRegiment ?? false;
    public bool ShowAuthNeeded => !Authed;
    public bool ShowNoRegiment => Authed && !HasRegiment;
    public bool ShowResupply => Authed && HasRegiment;

    public int OpenCount => OpenRequests.Count;
    public int TakenActiveCount => TakenRequests.Count(r => r.Status == ResupplyStatus.Claimed);
    public bool HasOpen => OpenRequests.Count > 0;
    public bool NoOpen => OpenRequests.Count == 0;
    public bool HasTaken => TakenRequests.Count > 0;
    public bool NoTaken => TakenRequests.Count == 0;
    public bool HasNeeds => Needs.Count > 0;
    public string Summary => !Authed ? "Connecte-toi pour voir le ravitaillement."
        : !HasRegiment ? "Rejoins un régiment pour gérer le ravitaillement."
        : $"{OpenCount} ouverte(s) · {TakenActiveCount} en cours";

    private void RaiseViewFlags()
    {
        Raise(nameof(HasRegiment)); Raise(nameof(ShowAuthNeeded));
        Raise(nameof(ShowNoRegiment)); Raise(nameof(ShowResupply)); Raise(nameof(Summary));
    }

    private void RaiseCounts()
    {
        Raise(nameof(OpenCount)); Raise(nameof(TakenActiveCount));
        Raise(nameof(HasOpen)); Raise(nameof(NoOpen)); Raise(nameof(HasTaken)); Raise(nameof(NoTaken));
        Raise(nameof(HasNeeds)); Raise(nameof(Summary));
    }

    public void Initialize(RegimentViewModel regiment, StockpilesViewModel stockpiles)
    {
        _regiment = regiment;
        _stockpiles = stockpiles;
    }

    public void ClearAuth()
    {
        _client?.Dispose(); _client = null;
        _clientKey = "";
        Authed = false;
        OpenRequests.Clear(); TakenRequests.Clear(); Needs.Clear();
        RaiseCounts();
        Status = "Connecte-toi avec Steam (onglet Amis).";
    }

    /// <summary>« Où produire ? » : la carte doit zoomer sur l'hexagone de la demande et surligner les lieux du plan.</summary>
    public event Action<string, IReadOnlyCollection<int>>? ProduceOnMapRequested;

    public void RequestProduceOnMap(ResupplyRequestViewModel r) =>
        ProduceOnMapRequested?.Invoke(r.Hex, r.ProductionIcons);

    public async Task RefreshAsync()
    {
        string? token = _tokenStore.Load();
        if (token is null) { ClearAuth(); return; }

        Busy = true;
        try
        {
            // Client persistant : recréé seulement si l'URL ou le jeton change (pooling HTTP conservé).
            string baseUrl = _settingsStore.Load().ApiBaseUrl;
            string clientKey = $"{baseUrl}|{token}";
            if (_client is null || _clientKey != clientKey)
            {
                _client?.Dispose();
                _client = new ResupplyClient(baseUrl, token);
                _clientKey = clientKey;
            }
            Authed = true;
            RaiseViewFlags();

            ApplyRequests(await _client.GetListAsync());
            LoadNeeds();
            Status = HasRegiment ? Summary : "Rejoins un régiment pour gérer le ravitaillement.";
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    // Les manques viennent des alertes déjà chargées par le module Stockpiles (rafraîchi avant
    // nous par le shell) — pas de second appel réseau.
    private void LoadNeeds()
    {
        Needs.Clear();
        var alerts = _stockpiles?.LastAlerts ?? Array.Empty<StockpileAlertDto>();
        foreach (var a in alerts.Where(a => a.IsOwn).OrderBy(a => a.Severity == "critical" ? 0 : 1).ThenBy(a => a.Name))
            Needs.Add(new ResupplyNeedViewModel(a));
        RaiseCounts();
    }

    /// <summary>Toast Windows demandé (titre, message) — câblé par MainViewModel vers le Notifier.</summary>
    public event Action<string, string>? ToastRequested;

    // Demandes ouvertes déjà vues (toast uniquement pour les NOUVELLES ; null = pas encore de base).
    private HashSet<string>? _knownRequestIds;

    private void ApplyRequests(List<ResupplyRequestDto> list)
    {
        OpenRequests.Clear(); TakenRequests.Clear();
        foreach (var d in list)
        {
            var vm = new ResupplyRequestViewModel(d);
            if (d.Status == ResupplyStatus.Open)
                OpenRequests.Add(vm);                              // toutes les demandes ouvertes visibles
            else if (d.IsMine || d.ClaimedByMyRegiment)
                TakenRequests.Add(vm);                             // prises en charge par/pour mon régiment
            // sinon : prise par un autre régiment → pas dans ma vue
        }
        RaiseCounts();

        // Toast pour les nouvelles demandes ouvertes créées par d'autres (jamais au premier chargement).
        var openNow = list.Where(d => d.Status == ResupplyStatus.Open).ToList();
        if (_knownRequestIds is not null)
        {
            foreach (var d in openNow.Where(d => !d.IsMine && !_knownRequestIds.Contains(d.Id)).Take(3))
                ToastRequested?.Invoke("Nouvelle demande de ravitaillement",
                    $"« {d.Title} » — {d.Hex} ({d.Items.Count} item(s))");
        }
        _knownRequestIds = openNow.Select(d => d.Id).ToHashSet();
    }

    // --- Brouillon d'items ---
    public void AddDraftItem()
    {
        if (string.IsNullOrWhiteSpace(DraftItemName)) { Status = "Choisis un item."; return; }
        if (!int.TryParse(DraftItemQuantity.Trim(), out int qty) || qty <= 0) { Status = "Quantité invalide."; return; }
        var cat = FoxholeItemCatalog.Resolve(DraftItemName);
        DraftItems.Add(new ResupplyItemLineViewModel(cat.Code, cat.Name, cat.Category, qty));
        Raise(nameof(HasDraft));
        DraftItemName = ""; DraftItemQuantity = "";
    }

    public void RemoveDraftItem(ResupplyItemLineViewModel item)
    {
        DraftItems.Remove(item);
        Raise(nameof(HasDraft));
    }

    /// <summary>Remplit le brouillon depuis la calculatrice (remplace le brouillon courant).</summary>
    public void ImportDraft(List<(string Code, string Name, string Category, int Qty)> items)
    {
        DraftItems.Clear();
        foreach (var (code, name, category, qty) in items)
            DraftItems.Add(new ResupplyItemLineViewModel(code, name, category, qty));
        Raise(nameof(HasDraft));
        Status = $"Brouillon prérempli ({items.Count} item(s)) — donne un nom et un lieu, puis crée la demande.";
    }

    public void AddDraftFromNeed(ResupplyNeedViewModel need)
    {
        DraftItems.Add(new ResupplyItemLineViewModel(need.Code, need.Name, need.Category, need.Deficit));
        Raise(nameof(HasDraft));
        if (string.IsNullOrWhiteSpace(NewHex) && !string.IsNullOrWhiteSpace(need.Hex)) NewHex = need.Hex;
        if (string.IsNullOrWhiteSpace(NewTitle)) NewTitle = $"Réappro {need.StockpileName}";
        Status = $"{need.Name} ×{need.Deficit} ajouté au brouillon.";
    }

    public async Task CreateFromFormAsync()
    {
        if (_client is null || Busy) return;
        if (DraftItems.Count == 0) { Status = "Ajoute au moins un item à la demande."; return; }
        var items = DraftItems.Select(i => new ResupplyItemDto(i.Code, i.Name, i.Category, i.Quantity)).ToList();
        Busy = true;
        try
        {
            ApplyRequests(await _client.CreateAsync(new CreateResupplyRequest(
                string.IsNullOrWhiteSpace(NewTitle) ? "Demande" : NewTitle.Trim(),
                NewHex ?? "", NewCoords?.Trim() ?? "", items, NewPriority, NewNote?.Trim() ?? "", NewVisibility)));
            Status = $"Demande « {(string.IsNullOrWhiteSpace(NewTitle) ? "Demande" : NewTitle.Trim())} » créée ({items.Count} item(s)).";
            DraftItems.Clear();
            NewTitle = ""; NewHex = ""; NewCoords = ""; NewNote = ""; NewPriority = ResupplyPriority.Normal; NewVisibility = ResupplyVisibility.Regiment;
            Raise(nameof(HasDraft));
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    public Task ClaimAsync(ResupplyRequestViewModel r) => ActAsync(() => _client!.ClaimAsync(r.Id));
    public Task DoneAsync(ResupplyRequestViewModel r) => ActAsync(() => _client!.DoneAsync(r.Id));
    public Task ReopenAsync(ResupplyRequestViewModel r) => ActAsync(() => _client!.ReopenAsync(r.Id));
    public Task DeleteAsync(ResupplyRequestViewModel r) => ActAsync(() => _client!.DeleteAsync(r.Id));
    public Task SetVisibilityAsync(ResupplyRequestViewModel r, int visibility) => ActAsync(() => _client!.SetVisibilityAsync(r.Id, visibility));

    private async Task ActAsync(Func<Task<List<ResupplyRequestDto>>> action)
    {
        if (_client is null || Busy) return;
        Busy = true;
        try { ApplyRequests(await action()); }
        catch (FriendException fex) { Status = fex.Message; }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }
}

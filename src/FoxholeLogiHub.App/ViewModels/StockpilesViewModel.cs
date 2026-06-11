using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using FoxholeLogiHub.App.Services;
using FoxholeLogiHub.Contracts;
using FoxholeLogiHub.Core.Services;

namespace FoxholeLogiHub.App.ViewModels;

public sealed record StockpileTypeOption(string Value, string Label);

/// <summary>Gestion des stockpiles du régiment (création, visibilité public/privé, partage allié).</summary>
public sealed class StockpilesViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore = new();
    private readonly TokenStore _tokenStore = new();

    private readonly CaptureService _capture = new();
    private string _clientKey = "";
    private RegimentViewModel? _regiment;
    private CompanionManager? _companion;
    private StockpileClient? _client;

    private bool _authed;
    private bool _busy;
    private string _status = "";

    private string _formName = "";
    private string _formHex = "";
    private string _formTown = "";
    private string _formType = StockpileTypes.StorageDepot;
    private string _formCode = "";
    private bool _formIsPublic;
    private string? _editingId;

    private string? _selectedId;
    private string _selectedName = "";
    private bool _selectedCanManage;
    private string _newItemName = "";
    private string _newItemQuantity = "";
    private string _newItemLow = "";
    private string _newItemCritical = "";
    private string? _editingCode;   // si édition d'un item existant : on garde son code/nom/catégorie
    private string _editingName = "";
    private string _editingCategory = "";

    public ObservableCollection<StockpileItemViewModel> Stockpiles { get; } = new();
    public ObservableCollection<StockpileItemViewModel> ThreatenedStockpiles { get; } = new();
    public ObservableCollection<StockpileLineViewModel> Items { get; } = new();
    public ObservableCollection<StockpileAlertViewModel> Alerts { get; } = new();
    public ICollectionView ItemsView { get; }
    public ICollectionView AlertsView { get; }
    public IReadOnlyList<string> ItemNames { get; } = FoxholeItemCatalog.Names;

    public StockpilesViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StockpileLineViewModel.CategoryLabel)));
        AlertsView = CollectionViewSource.GetDefaultView(Alerts);
        AlertsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StockpileAlertViewModel.StockpileGroup)));
        AlertsView.SortDescriptions.Add(new SortDescription(nameof(StockpileAlertViewModel.StockpileGroup), ListSortDirection.Ascending));
        AlertsView.SortDescriptions.Add(new SortDescription(nameof(StockpileAlertViewModel.SeverityRank), ListSortDirection.Ascending));
        AlertsView.SortDescriptions.Add(new SortDescription(nameof(StockpileAlertViewModel.Name), ListSortDirection.Ascending));
    }

    public int CriticalCount => Alerts.Count(a => a.IsCritical);
    public int LowCount => Alerts.Count(a => !a.IsCritical);
    public bool HasAlerts => Alerts.Count > 0;
    public bool NoAlerts => Alerts.Count == 0;
    public bool HasCritical => CriticalCount > 0;
    public bool HasThreats => ThreatenedStockpiles.Count > 0;

    // Bannière de guerre (API publique, cache serveur) — affichée sur le Dashboard.
    private string _warBanner = "";
    public string WarBanner { get => _warBanner; private set { Set(ref _warBanner, value); Raise(nameof(HasWar)); } }
    public bool HasWar => _warBanner.Length > 0;
    public string AlertsSummary => Authed
        ? (HasAlerts ? $"{CriticalCount} critique(s) · {LowCount} bas" : "Aucune alerte — tout est au-dessus des seuils. ✅")
        : "Connecte-toi pour voir tes alertes.";

    private void RaiseAlertFlags()
    {
        Raise(nameof(CriticalCount)); Raise(nameof(LowCount));
        Raise(nameof(HasAlerts)); Raise(nameof(NoAlerts)); Raise(nameof(HasCritical)); Raise(nameof(AlertsSummary));
        Raise(nameof(HasThreats));
    }

    private ItemDetailViewModel? _itemDetail;
    public ItemDetailViewModel? ItemDetail { get => _itemDetail; private set { Set(ref _itemDetail, value); Raise(nameof(HasItemDetail)); } }
    public bool HasItemDetail => _itemDetail is not null;

    /// <summary>Ouvre la fiche détaillée (catégorie, caisse, recette) d'un item par code.</summary>
    public void ShowItemDetail(string code)
    {
        var e = FoxholeItemCatalog.Get(code);
        ItemDetail = e is null ? null : new ItemDetailViewModel(e);
        if (e is null)
            Status = "Pas de fiche pour cet item (code inconnu du catalogue).";
    }

    public void CloseItemDetail() => ItemDetail = null;

    public bool IsStockpileSelected => _selectedId is not null;
    public string SelectedName { get => _selectedName; private set => Set(ref _selectedName, value); }
    public bool SelectedCanManage { get => _selectedCanManage; private set => Set(ref _selectedCanManage, value); }
    public bool HasNoItems => Items.Count == 0;
    public string NewItemName { get => _newItemName; set => Set(ref _newItemName, value); }
    public string NewItemQuantity { get => _newItemQuantity; set => Set(ref _newItemQuantity, value); }
    public string NewItemLow { get => _newItemLow; set => Set(ref _newItemLow, value); }
    public string NewItemCritical { get => _newItemCritical; set => Set(ref _newItemCritical, value); }

    public IReadOnlyList<StockpileTypeOption> Types { get; } =
        StockpileCatalog.Types.Select(t => new StockpileTypeOption(t.Value, t.Label)).ToList();
    public IReadOnlyList<string> Hexes { get; } = StockpileCatalog.Hexes;

    public bool Authed { get => _authed; private set { Set(ref _authed, value); RaiseViewFlags(); } }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public bool HasRegiment => _regiment?.HasRegiment ?? false;
    public bool CanManage => _regiment?.CanManageStockpiles ?? false;

    public bool ShowAuthNeeded => !Authed;
    public bool ShowNoRegiment => Authed && !HasRegiment;
    public bool ShowStockpiles => Authed && HasRegiment;
    public bool ShowForm => ShowStockpiles && CanManage;
    public bool HasNoStockpiles => Stockpiles.Count == 0;

    private void RaiseViewFlags()
    {
        Raise(nameof(HasRegiment)); Raise(nameof(CanManage));
        Raise(nameof(ShowAuthNeeded)); Raise(nameof(ShowNoRegiment));
        Raise(nameof(ShowStockpiles)); Raise(nameof(ShowForm));
    }

    public string FormName { get => _formName; set => Set(ref _formName, value); }
    public string FormHex { get => _formHex; set => Set(ref _formHex, value); }
    public string FormTown { get => _formTown; set => Set(ref _formTown, value); }
    public string FormCode { get => _formCode; set => Set(ref _formCode, value); }
    public bool FormIsPublic { get => _formIsPublic; set => Set(ref _formIsPublic, value); }

    public string FormType
    {
        get => _formType;
        set { Set(ref _formType, value); Raise(nameof(FormUsesCode)); }
    }
    public bool FormUsesCode => StockpileTypes.UsesCode(FormType);

    public bool IsEditing => _editingId is not null;
    public string SubmitLabel => IsEditing ? "Enregistrer" : "Créer le stockpile";

    public void Initialize(RegimentViewModel regiment, CompanionManager companion)
    {
        _regiment = regiment;
        _companion = companion;
    }

    /// <summary>Capture le panneau stockpile en jeu, reconnaît les items via FIR et remplace le contenu.</summary>
    public async Task ImportFromCaptureAsync(int delaySeconds)
    {
        if (_client is null || _selectedId is null) { Status = "Sélectionne d'abord un stockpile."; return; }
        if (!SelectedCanManage) { Status = "Tu n'as pas la permission."; return; }
        if (Busy) return;

        _companion?.EnsureStarted();
        if (_companion is null || !_companion.Available) { Status = "Companion FIR (fic.exe) introuvable."; return; }

        Busy = true;
        try
        {
            for (int s = delaySeconds; s > 0; s--)
            {
                Status = $"Capture dans {s}s — affiche le stockpile en VUE-CARTE (clique le dépôt sur la carte)…";
                await Task.Delay(1000);
            }

            byte[]? png = _capture.CaptureForegroundWindow();
            if (png is null) { Status = "Capture impossible."; return; }

            using var fic = new FicClient(_companion.BaseUrl);
            var recognized = await fic.ExtractAsync(png);
            if (recognized.Count == 0)
            {
                Status = "Aucun item reconnu — capture le panneau stockpile en vue-carte (pas l'UI en base).";
                return;
            }

            var items = recognized.Select(r =>
            {
                var entry = FoxholeItemCatalog.Get(r.Code);
                string name = entry?.Label ?? r.Code;
                string category = entry?.Category ?? "Importé";
                // Distingue caisse / à l'unité (codes distincts → pas de collision, contenu plus exact).
                string code = r.IsCrated ? r.Code + "@crate" : r.Code;
                string displayName = r.IsCrated ? $"{name} (caisse)" : name;
                return new StockpileItemDto(code, displayName, category, r.Quantity, 0, 0);
            }).ToList();

            ApplyItems(await _client.ImportItemsAsync(_selectedId, items));
            Status = $"{items.Count} item(s) importés depuis la capture.";
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur import : {ex.Message}"; }
        finally { Busy = false; }
    }

    public void ClearAuth()
    {
        _client?.Dispose();
        _client = null;
        Authed = false;
        Stockpiles.Clear();
        ThreatenedStockpiles.Clear();
        Alerts.Clear();
        _lastAlerts = new List<StockpileAlertDto>();
        _clientKey = "";
        WarBanner = "";
        RaiseAlertFlags();
        CloseDetail();
        Raise(nameof(HasNoStockpiles));
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
            // Client persistant : on ne le recrée que si l'URL ou le jeton a changé (pooling HTTP conservé).
            string baseUrl = _settingsStore.Load().ApiBaseUrl;
            string clientKey = $"{baseUrl}|{token}";
            if (_client is null || _clientKey != clientKey)
            {
                _client?.Dispose();
                _client = new StockpileClient(baseUrl, token);
                _clientKey = clientKey;
            }
            Authed = true;
            RaiseViewFlags();
            // Les trois requêtes sont indépendantes → en parallèle (3 allers-retours → 1).
            var listTask = _client.GetListAsync();
            var alertsTask = LoadAlertsAsync();
            var warTask = LoadWarStatusAsync();
            ApplyList(await listTask);
            if (IsStockpileSelected)
                await LoadItemsAsync();
            await alertsTask;
            await warTask;
            Status = HasRegiment ? $"{Stockpiles.Count} stockpile(s)." : "Rejoins un régiment pour gérer des stockpiles.";
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    /// <summary>Dernières alertes brutes (partagées avec le ravitaillement — évite un double fetch).</summary>
    public IReadOnlyList<StockpileAlertDto> LastAlerts => _lastAlerts;
    private List<StockpileAlertDto> _lastAlerts = new();

    /// <summary>Toast Windows demandé (titre, message) — câblé par MainViewModel vers le Notifier.</summary>
    public event Action<string, string>? ToastRequested;

    // Items critiques déjà vus (toast uniquement pour les NOUVEAUX ; null = pas encore de base).
    private HashSet<string>? _knownCritical;

    private async Task LoadAlertsAsync()
    {
        if (_client is null)
            return;
        try
        {
            _lastAlerts = await _client.GetAlertsAsync();
            Alerts.Clear();
            foreach (var a in _lastAlerts)
                Alerts.Add(new StockpileAlertViewModel(a));
            RaiseAlertFlags();

            // Toast pour les items NOUVELLEMENT critiques (jamais au premier chargement).
            var criticalNow = _lastAlerts.Where(a => a.Severity == "critical")
                .Select(a => $"{a.StockpileId}|{a.Code}")
                .ToHashSet();
            if (_knownCritical is not null)
            {
                var fresh = _lastAlerts
                    .Where(a => a.Severity == "critical" && !_knownCritical.Contains($"{a.StockpileId}|{a.Code}"))
                    .Take(3);
                foreach (var a in fresh)
                    ToastRequested?.Invoke("Stock critique",
                        $"{a.Name} : {a.Quantity} (seuil {a.CriticalThreshold}) — {a.StockpileName}");
            }
            _knownCritical = criticalNow;
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch { /* les alertes ne doivent pas bloquer le reste */ }
    }

    private async Task LoadWarStatusAsync()
    {
        if (_client is null)
            return;
        try
        {
            var war = await _client.GetWarStatusAsync();
            WarBanner = war is { Available: true }
                ? $"⚔ Guerre {war.WarNumber} · Jour {war.DayOfWar} · Villes de victoire : Wardens {war.WardenVictoryTowns} — Colonials {war.ColonialVictoryTowns} (objectif {war.RequiredVictoryTowns})"
                : "";
        }
        catch { /* la bannière de guerre n'est jamais bloquante */ }
    }

    /// <summary>
    /// Archive locale (JSON) des stockpiles du régiment (avec contenu) et des demandes en cours,
    /// à appeler avant un reset de fin de guerre.
    /// </summary>
    public async Task<string?> ExportWarArchiveAsync(ResupplyViewModel resupply)
    {
        if (_client is null)
            return null;

        var stockpiles = new List<object>();
        foreach (var s in Stockpiles.Where(s => s.IsOwn))
        {
            List<StockpileItemDto> items = new();
            try { items = await _client.GetItemsAsync(s.Id); }
            catch { /* archive partielle plutôt que pas d'archive */ }
            stockpiles.Add(new { s.Name, s.Hex, s.Town, s.Type, s.Code, s.IsPublic, items });
        }

        static object Req(ResupplyRequestViewModel r) => new
        {
            r.Title, r.Hex, r.Coords, r.Status,
            items = r.Items.Select(i => new { i.Code, i.Name, i.Quantity }).ToList(),
        };
        var payload = new
        {
            exportedAt = DateTimeOffset.Now,
            war = WarBanner,
            stockpiles,
            requests = resupply.OpenRequests.Select(Req).Concat(resupply.TakenRequests.Select(Req)).ToList(),
        };

        string dir = System.IO.Path.Combine(AppPaths.DataDirectory, "archives");
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, $"guerre-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private void ApplyList(List<StockpileDto> list)
    {
        var allies = _regiment?.AcceptedAllies ?? new List<(string, string)>();
        Stockpiles.Clear();
        ThreatenedStockpiles.Clear();
        foreach (var dto in list)
        {
            var vm = new StockpileItemViewModel(dto, allies);
            Stockpiles.Add(vm);
            if (vm.IsThreatened)
                ThreatenedStockpiles.Add(vm);
        }
        Raise(nameof(HasNoStockpiles));
        Raise(nameof(HasThreats));

        if (_selectedId is not null)
        {
            var sel = list.FirstOrDefault(s => s.Id == _selectedId);
            if (sel is null)
                CloseDetail();
            else
            {
                SelectedName = sel.Name;
                SelectedCanManage = sel.CanManage;
            }
        }
    }

    /// <summary>Création hors port/dépôt : on bascule sur la carte pour choisir l'emplacement à la main.</summary>
    public event Action<PendingMapPick>? PlaceOnMapRequested;

    public async Task SubmitFormAsync()
    {
        if (_client is null || Busy)
            return;
        if (string.IsNullOrWhiteSpace(FormName) || string.IsNullOrWhiteSpace(FormHex))
        {
            Status = "Nom et hexagone requis.";
            return;
        }

        // Bunker, base de prod, usine… : structures construites/de terrain → l'emplacement précis
        // compte, on le choisit sur la carte (les ports/dépôts restent ancrés à leur ville).
        if (_editingId is null && !StockpileTypes.UsesCode(FormType))
        {
            Status = "Choisis l'emplacement sur la carte 📍";
            PlaceOnMapRequested?.Invoke(new PendingMapPick(
                FormName.Trim(), FormHex.Trim(), FormTown.Trim(), FormType, "", FormIsPublic));
            return;
        }

        Busy = true;
        try
        {
            string code = FormUsesCode ? FormCode.Trim() : "";
            List<StockpileDto> list = _editingId is null
                ? await _client.CreateAsync(new CreateStockpileRequest(FormName.Trim(), FormHex.Trim(), FormTown.Trim(), FormType, code, FormIsPublic))
                : await _client.UpdateAsync(new UpdateStockpileRequest(_editingId, FormName.Trim(), FormHex.Trim(), FormTown.Trim(), FormType, code, FormIsPublic));
            ApplyList(list);
            Status = _editingId is null ? "Stockpile créé." : "Stockpile mis à jour.";
            CancelEdit();
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    /// <summary>Le stockpile a été créé via la carte : on solde le formulaire.</summary>
    public void OnPlacedExternally()
    {
        CancelEdit();
        Status = "Stockpile placé sur la carte 📍";
    }

    // ---------- Recherche globale d'items ----------

    private string _searchQuery = "";
    public string SearchQuery { get => _searchQuery; set => Set(ref _searchQuery, value); }
    public ObservableCollection<ItemSearchResultViewModel> SearchResults { get; } = new();
    public bool HasSearchResults => SearchResults.Count > 0;
    private string _searchStatus = "";
    public string SearchStatus { get => _searchStatus; private set => Set(ref _searchStatus, value); }

    /// <summary>Cherche l'item (nom ou code) dans tous les stockpiles visibles, trié par quantité.</summary>
    public async Task SearchItemsAsync()
    {
        if (_client is null)
            return;
        string q = SearchQuery.Trim();
        SearchResults.Clear();
        Raise(nameof(HasSearchResults));
        if (q.Length < 2)
        {
            SearchStatus = q.Length == 0 ? "" : "Au moins 2 caractères.";
            return;
        }
        try
        {
            var results = await _client.SearchItemsAsync(q);
            foreach (var r in results)
                SearchResults.Add(new ItemSearchResultViewModel(r));
            Raise(nameof(HasSearchResults));
            SearchStatus = results.Count == 0
                ? $"Aucun « {q} » dans les stockpiles visibles."
                : $"{results.Count} emplacement(s) — total {results.Sum(r => (long)r.Quantity):N0} unité(s).";
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { SearchStatus = $"Erreur : {ex.Message}"; }
    }

    public void ClearSearch()
    {
        SearchQuery = "";
        SearchResults.Clear();
        Raise(nameof(HasSearchResults));
        SearchStatus = "";
    }

    public void EditStockpile(StockpileItemViewModel s)
    {
        _editingId = s.Id;
        FormName = s.Name;
        FormHex = s.Hex;
        FormTown = s.Town;
        FormType = s.Type;
        FormCode = s.Code;
        FormIsPublic = s.IsPublic;
        Raise(nameof(IsEditing));
        Raise(nameof(SubmitLabel));
        Status = $"Modification de « {s.Name} ».";
    }

    public void CancelEdit()
    {
        _editingId = null;
        FormName = ""; FormHex = ""; FormTown = ""; FormType = StockpileTypes.StorageDepot; FormCode = ""; FormIsPublic = false;
        Raise(nameof(IsEditing));
        Raise(nameof(SubmitLabel));
    }

    public async Task DeleteAsync(StockpileItemViewModel s)
    {
        if (_client is null || Busy)
            return;
        Busy = true;
        try
        {
            ApplyList(await _client.DeleteAsync(s.Id));
            Status = $"« {s.Name} » supprimé.";
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    public async Task ToggleShareAsync(StockpileShareTargetViewModel target)
    {
        if (_client is null || Busy)
            return;
        Busy = true;
        try
        {
            List<StockpileDto> list = target.IsShared
                ? await _client.UnshareAsync(target.StockpileId, target.RegimentId)
                : await _client.ShareAsync(target.StockpileId, target.RegimentId);
            ApplyList(list);
            Status = target.IsShared ? $"Partage retiré ({target.RegimentName})." : $"Partagé avec {target.RegimentName}.";
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    // --- Contenu (items) d'un stockpile ---

    public async Task SelectStockpileAsync(StockpileItemViewModel s)
    {
        _selectedId = s.Id;
        SelectedName = s.Name;
        SelectedCanManage = s.CanManage;
        Raise(nameof(IsStockpileSelected));
        await LoadItemsAsync();
        await LoadTemplatesAsync();
    }

    // ---------- Templates d'objectifs de seuils ----------

    public ObservableCollection<StockpileTemplateDto> Templates { get; } = new();
    private StockpileTemplateDto? _selectedTemplate;
    public StockpileTemplateDto? SelectedTemplate { get => _selectedTemplate; set => Set(ref _selectedTemplate, value); }
    private string _templateName = "";
    public string TemplateName { get => _templateName; set => Set(ref _templateName, value); }
    public bool HasTemplates => Templates.Count > 0;

    private async Task LoadTemplatesAsync()
    {
        if (_client is null)
            return;
        try
        {
            var list = await _client.GetTemplatesAsync();
            Templates.Clear();
            foreach (var t in list)
                Templates.Add(t);
            Raise(nameof(HasTemplates));
        }
        catch { /* meilleur effort */ }
    }

    /// <summary>Sauvegarde les seuils du stockpile ouvert comme template du régiment.</summary>
    public async Task SaveTemplateAsync()
    {
        if (_client is null || _selectedId is null || Busy)
            return;
        if (string.IsNullOrWhiteSpace(TemplateName)) { Status = "Donne un nom au template."; return; }
        Busy = true;
        try
        {
            var t = await _client.CreateTemplateAsync(new CreateTemplateFromStockpileRequest(_selectedId, TemplateName.Trim()));
            Status = $"Template « {t?.Name} » enregistré ({t?.ItemCount} seuil(s)).";
            TemplateName = "";
            await LoadTemplatesAsync();
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    /// <summary>Applique le template sélectionné au stockpile ouvert.</summary>
    public async Task ApplyTemplateAsync()
    {
        if (_client is null || _selectedId is null || Busy)
            return;
        if (SelectedTemplate is null) { Status = "Choisis un template."; return; }
        Busy = true;
        try
        {
            ApplyItems(await _client.ApplyTemplateAsync(new ApplyTemplateRequest(SelectedTemplate.Id, _selectedId)));
            Status = $"Template « {SelectedTemplate.Name} » appliqué.";
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    public async Task DeleteTemplateAsync()
    {
        if (_client is null || SelectedTemplate is null || Busy)
            return;
        Busy = true;
        try
        {
            await _client.DeleteTemplateAsync(SelectedTemplate.Id);
            Status = $"Template « {SelectedTemplate.Name} » supprimé.";
            SelectedTemplate = null;
            await LoadTemplatesAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    public void CloseDetail()
    {
        _selectedId = null;
        Items.Clear();
        NewItemName = ""; NewItemQuantity = ""; NewItemLow = ""; NewItemCritical = "";
        _editingCode = null;
        Raise(nameof(IsStockpileSelected));
        Raise(nameof(HasNoItems));
    }

    private Dictionary<string, List<HistoryPointDto>> _history = new(StringComparer.OrdinalIgnoreCase);

    private async Task LoadItemsAsync()
    {
        if (_client is null || _selectedId is null)
            return;
        try
        {
            var items = await _client.GetItemsAsync(_selectedId);
            // Historique (tendances) — meilleur effort : la vue items marche sans lui.
            try
            {
                _history = (await _client.GetHistoryAsync(_selectedId))
                    .ToDictionary(h => h.Code, h => h.Points, StringComparer.OrdinalIgnoreCase);
            }
            catch { _history = new(StringComparer.OrdinalIgnoreCase); }
            ApplyItems(items);
        }
        catch (Exception ex) { Status = $"Erreur items : {ex.Message}"; }
    }

    private void ApplyItems(List<StockpileItemDto> items)
    {
        Items.Clear();
        foreach (var i in items)
            Items.Add(new StockpileLineViewModel(i, SelectedCanManage, _history.GetValueOrDefault(i.Code)));
        Raise(nameof(HasNoItems));
    }

    public async Task SetItemFromFormAsync()
    {
        if (_client is null || _selectedId is null || Busy)
            return;
        if (string.IsNullOrWhiteSpace(NewItemName)) { Status = "Choisis un item."; return; }
        if (!int.TryParse(NewItemQuantity.Trim(), out int qty) || qty < 0) { Status = "Quantité invalide."; return; }

        // En édition : on garde le code/nom/catégorie d'origine (sinon ré-résoudre changerait le code).
        string code, name, category;
        if (_editingCode is not null)
        {
            code = _editingCode; name = _editingName; category = _editingCategory;
        }
        else
        {
            var cat = FoxholeItemCatalog.Resolve(NewItemName);
            code = cat.Code; name = cat.Name; category = cat.Category;
        }
        int.TryParse(NewItemLow.Trim(), out int low);
        int.TryParse(NewItemCritical.Trim(), out int crit);
        Busy = true;
        try
        {
            ApplyItems(await _client.SetItemAsync(new SetStockpileItemRequest(_selectedId, code, name, category, qty, Math.Max(0, low), Math.Max(0, crit))));
            Status = qty <= 0 ? $"{name} retiré." : $"{name} : {qty}";
            NewItemName = ""; NewItemQuantity = ""; NewItemLow = ""; NewItemCritical = "";
            _editingCode = null;
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    public async Task RemoveLineAsync(StockpileLineViewModel line)
    {
        if (_client is null || _selectedId is null || Busy)
            return;
        Busy = true;
        try
        {
            ApplyItems(await _client.SetItemAsync(new SetStockpileItemRequest(_selectedId, line.Code, line.Name, line.Category, 0, 0, 0)));
            Status = $"{line.Name} retiré.";
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    public void EditLine(StockpileLineViewModel line)
    {
        _editingCode = line.Code;
        _editingName = line.Name;
        _editingCategory = line.Category;
        NewItemName = line.Name;
        NewItemQuantity = line.Quantity.ToString();
        NewItemLow = line.Low > 0 ? line.Low.ToString() : "";
        NewItemCritical = line.Critical > 0 ? line.Critical.ToString() : "";
        Status = $"Édition de « {line.Name} » (modifie quantité/seuils puis Définir).";
    }
}

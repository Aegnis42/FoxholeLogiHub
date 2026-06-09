using System.Collections.ObjectModel;
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

    private RegimentViewModel? _regiment;
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

    public ObservableCollection<StockpileItemViewModel> Stockpiles { get; } = new();

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

    public void Initialize(RegimentViewModel regiment) => _regiment = regiment;

    public void ClearAuth()
    {
        _client?.Dispose();
        _client = null;
        Authed = false;
        Stockpiles.Clear();
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
            _client?.Dispose();
            _client = new StockpileClient(_settingsStore.Load().ApiBaseUrl, token);
            Authed = true;
            RaiseViewFlags();
            ApplyList(await _client.GetListAsync());
            Status = HasRegiment ? $"{Stockpiles.Count} stockpile(s)." : "Rejoins un régiment pour gérer des stockpiles.";
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    private void ApplyList(List<StockpileDto> list)
    {
        var allies = _regiment?.AcceptedAllies ?? new List<(string, string)>();
        Stockpiles.Clear();
        foreach (var dto in list)
            Stockpiles.Add(new StockpileItemViewModel(dto, allies));
        Raise(nameof(HasNoStockpiles));
    }

    public async Task SubmitFormAsync()
    {
        if (_client is null)
            return;
        if (string.IsNullOrWhiteSpace(FormName) || string.IsNullOrWhiteSpace(FormHex))
        {
            Status = "Nom et hexagone requis.";
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
        if (_client is null)
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
        if (_client is null)
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
}

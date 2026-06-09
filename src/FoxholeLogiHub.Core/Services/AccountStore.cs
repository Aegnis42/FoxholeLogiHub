using System.Text.Json;
using System.Text.Json.Serialization;
using FoxholeLogiHub.Core.Models;

namespace FoxholeLogiHub.Core.Services;

/// <summary>Persistance du compte dans account.json (lecture/écriture).</summary>
public sealed class AccountStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Account? Load()
    {
        string path = AppPaths.AccountFile;
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Account>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }

    public void Save(Account account)
    {
        account.UpdatedAt = DateTimeOffset.Now;
        File.WriteAllText(AppPaths.AccountFile, JsonSerializer.Serialize(account, Options));
    }
}

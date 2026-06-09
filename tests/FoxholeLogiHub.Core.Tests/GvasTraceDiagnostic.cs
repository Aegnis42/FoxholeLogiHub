using FoxholeLogiHub.Core.Gvas;
using FoxholeLogiHub.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace FoxholeLogiHub.Core.Tests;

public sealed class GvasTraceDiagnostic
{
    private readonly ITestOutputHelper _output;
    public GvasTraceDiagnostic(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public void Trace_until_failure()
    {
        string? path = SaveGameLocator.GetPlayerSavePath();
        Skip.If(path is null, "Pas de .sav");

        var parser = new GvasParser();
        try
        {
            parser.Parse(path!);
            _output.WriteLine("OK - parsing complet");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"ECHEC: {ex.GetType().Name}: {ex.Message}");
        }

        _output.WriteLine($"--- {parser.Trace.Count} propriétés lues ---");
        foreach (string line in parser.Trace)
            _output.WriteLine(line);
    }
}

namespace Backstop.Services.Api;

/// <summary>Tests the connection to Meta and says in plain text what is wrong. Never returns the key or any message.</summary>
public interface IMetaDiagnostics
{
    Task<string> DiagnoseAsync(CancellationToken ct);
}

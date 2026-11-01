using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;

namespace Backstop.Services;

/// <summary>
/// Who is calling, for the per-visitor cap on Meta calls. A Blazor circuit has no HTTP request after it starts, so
/// the address is taken once when the circuit opens and set for every action the circuit runs
/// (<see cref="ClientIpCircuitHandler"/>). Null when it could not be worked out.
/// </summary>
public static class ClientIdentity
{
    private static readonly AsyncLocal<string?> Slot = new();

    public static string? Current
    {
        get => Slot.Value;
        set => Slot.Value = value;
    }

    /// <summary>
    /// The visitor's address as the forwarded-headers middleware worked it out. If <paramref name="header"/> names a
    /// header the host's proxy sets itself (for example CF-Connecting-IP), that wins, but only when it holds a single
    /// valid address.
    /// </summary>
    public static string? Resolve(HttpContext? http, string? header = null)
    {
        if (http is null) return null;
        if (!string.IsNullOrWhiteSpace(header)
            && http.Request.Headers.TryGetValue(header.Trim(), out var v)
            && v.Count == 1
            && IPAddress.TryParse(v[0]?.Trim(), out var fromHeader))
            return Normal(fromHeader);
        var ip = http.Connection.RemoteIpAddress;
        return ip is null ? null : Normal(ip);
    }

    private static string Normal(IPAddress ip) => (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();

    /// <summary>A short fingerprint for logs, so two visitors can be told apart without writing an address down.</summary>
    public static string Fingerprint(string? ip) =>
        ip is null ? "none" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip)))[..8];
}

/// <summary>Captures the visitor's address when the circuit opens and makes it available to every action the circuit runs.</summary>
public sealed class ClientIpCircuitHandler(IHttpContextAccessor accessor, IConfiguration config, ILogger<ClientIpCircuitHandler> logger) : CircuitHandler
{
    private string? _ip;

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var http = accessor.HttpContext;
        _ip = ClientIdentity.Resolve(http, config["CLIENT_IP_HEADER"]);
        // A fingerprint only, never the address. Compare it across two networks to check the cap sees them as different.
        logger.LogInformation("Circuit opened. Visitor {Visitor}. Forwarded-for entries: {Entries}.",
            ClientIdentity.Fingerprint(_ip), http?.Request.Headers["X-Forwarded-For"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Length ?? 0);
        if (_ip is null) logger.LogWarning("Visitor address unknown for this circuit. Only the app-wide Meta caps apply to it.");
        return Task.CompletedTask;
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) =>
        context =>
        {
            ClientIdentity.Current = _ip;
            return next(context);
        };
}

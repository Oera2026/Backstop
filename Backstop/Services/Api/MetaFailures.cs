using System.Net;
using Backstop.Models;

namespace Backstop.Services.Api;

/// <summary>
/// Builds the two-part error for a failed Meta call: plain words for the person, detail for a technical reader.
/// Technical text names the host, model, status and timing only. Never the key, never the inspected text.
/// </summary>
internal static class MetaFailures
{
    private static string Detail(string code, string what, MetaAiOptions? o, params string[] extra)
    {
        var lines = new List<string>
        {
            $"Code: {code}",
            $"What: {what}",
            $"Service: {MetaAiScamChecker.AllowedHost}" + (o is null ? "" : $" (model {o.Model})"),
            $"Time: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
        };
        lines.AddRange(extra);
        return string.Join('\n', lines);
    }

    private static string Wait(int? seconds) =>
        seconds is > 0 ? $"Please wait about {Math.Max(1, (seconds.Value + 9) / 10 * 10)} seconds and try again." : "Please try again in a minute.";

    public static FailureInfo NoKey(MetaAiOptions? o) => new(
        "The checker is not switched on yet. Please tell the person who looks after this app.",
        Detail("NO_API_KEY", "No Meta AI key is set, so no call was made.", o,
            "Fix: set META_AI_KEY in the host's environment variables, then restart."));

    public static FailureInfo BadEndpoint(MetaAiOptions o) => new(
        "The checker is set up wrongly, so it was not used. Please tell the person who looks after this app.",
        Detail("ENDPOINT_NOT_ALLOWED", "The endpoint is not https://api.meta.ai on the default port. Nothing was sent.", o,
            "Fix: check META_AI_ENDPOINT_URL."));

    public static FailureInfo Busy(int retryAfterSeconds, int used, int limit, MetaAiOptions? o) => new(
        "Lots of people are using the checker right now. " + Wait(retryAfterSeconds),
        Detail("LOCAL_RATE_LIMIT", "This app's own call limit was reached. Meta was not called.", o,
            $"Calls in last 60s: {used} of {limit}", $"Retry after: {retryAfterSeconds}s",
            "Setting: MetaAI:MaxCallsPerMinute (env MetaAI__MaxCallsPerMinute)."));

    /// <summary>A refusal from this app's own caps. Meta was not called.</summary>
    public static FailureInfo Limited(MetaCallGuard.Denial denial, int retryAfterSeconds, int used, MetaCallGuard guard, MetaAiOptions? o) => denial switch
    {
        MetaCallGuard.Denial.PerVisitor => new("You are going a little fast. " + Wait(retryAfterSeconds),
            Detail("VISITOR_RATE_LIMIT", "This visitor's call limit was reached. Meta was not called.", o,
                $"Calls in last 60s: {used} of {guard.VisitorLimit}", $"Retry after: {retryAfterSeconds}s",
                "Setting: META_AI_MAX_CALLS_PER_IP_PER_MINUTE.")),
        MetaCallGuard.Denial.PerDay => new("The checker has reached its limit for today. Please try again tomorrow.",
            Detail("DAILY_LIMIT", "This app's daily call limit was reached. Meta was not called.", o,
                $"Calls today: {used} of {guard.DayLimit}", "Setting: META_AI_MAX_CALLS_PER_DAY.")),
        _ => Busy(retryAfterSeconds, used, guard.Limit, o),
    };

    public static FailureInfo Status(HttpStatusCode code, int? retryAfterSeconds, MetaAiOptions? o)
    {
        var n = (int)code;
        var retry = retryAfterSeconds is > 0 ? $"Retry-After: {retryAfterSeconds}s" : "Retry-After: not given";
        return n switch
        {
            429 => new("The checker is very busy right now. " + Wait(retryAfterSeconds),
                Detail("META_429", "Meta rate-limited this app. Retries were already tried.", o, "HTTP status: 429", retry)),
            401 or 403 => new("The checker could not sign in. Please tell the person who looks after this app.",
                Detail($"META_{n}", "Meta refused the key. It may be wrong, expired or not allowed to use this model.", o,
                    $"HTTP status: {n}", "Fix: check META_AI_KEY and META_AI_MODEL.")),
            404 => new("The checker could not be found. Please tell the person who looks after this app.",
                Detail("META_404", "Meta does not know this endpoint or model.", o, "HTTP status: 404", "Fix: check META_AI_ENDPOINT_URL and META_AI_MODEL.")),
            >= 500 => new("The checker is having a problem on its own side. Please try again soon.",
                Detail($"META_{n}", "Meta returned a server error. Retries were already tried.", o, $"HTTP status: {n}", retry)),
            _ => new("The checker could not finish. Please try again in a minute.",
                Detail($"META_{n}", "Meta returned an unexpected status.", o, $"HTTP status: {n}")),
        };
    }

    public static FailureInfo Timeout(MetaAiOptions? o) => new(
        "The checker took too long to answer. Please try again in a minute.",
        Detail("TIMEOUT", "No complete answer arrived in time, after any retries.", o,
            o is null ? "" : $"Timeout: {o.TimeoutSeconds}s (setting META_AI_TIMEOUT_SECONDS)"));

    public static FailureInfo CircuitOpen(MetaAiOptions? o) => new(
        "The checker is resting after some problems. Please try again in a minute.",
        Detail("CIRCUIT_OPEN", "Too many recent calls to Meta failed, so calls are paused for a short while. Meta was not called.", o));

    public static FailureInfo Network(Exception ex, MetaAiOptions? o) => new(
        "We could not reach the checker. Please check the internet connection and try again.",
        Detail("NETWORK_ERROR", "The request could not be sent or the connection dropped.", o,
            $"Exception: {ex.GetType().Name}", $"Detail: {Short(ex.InnerException?.Message ?? ex.Message)}"));

    public static FailureInfo BadReply(string note, MetaAiOptions? o) => new(
        "The checker sent back something we could not use. Please try again in a minute.",
        Detail("BAD_REPLY", note, o));

    public static FailureInfo Unexpected(Exception? ex, MetaAiOptions? o) => new(
        "Something went wrong. Please try again in a minute.",
        Detail("UNEXPECTED", ex is null ? "Unexpected error." : $"Unexpected {ex.GetType().Name}.", o,
            ex is null ? "" : $"Detail: {Short(ex.Message)}"));

    /// <summary>Maps a note from an older code path to the plain/technical pair.</summary>
    public static FailureInfo FromNote(string note, MetaAiOptions? o) => note switch
    {
        "API key not configured" => NoKey(o),
        "Meta AI API timed out" or "Timed out" => Timeout(o),
        _ => BadReply(note, o),
    };

    private static string Short(string s) => s.Length > 200 ? s[..200] : s;
}

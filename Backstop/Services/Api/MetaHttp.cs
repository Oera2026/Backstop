using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Backstop.Services.Api;

/// <summary>Shared helpers for failed Meta calls. Nothing here ever writes the key.</summary>
internal static class MetaHttp
{
    /// <summary>Logs the final URL and status. Never the reply body, which could quote what a visitor sent.</summary>
    public static Task LogFailureAsync(ILogger logger, string caller, HttpResponseMessage response, MetaAiOptions options)
    {
        logger.LogError("Meta AI ({Caller}) failed: status {StatusCode} for {Url} (model {Model}).",
            caller, (int)response.StatusCode, response.RequestMessage?.RequestUri?.ToString() ?? options.ChatUrl, options.Model);
        return Task.CompletedTask;
    }

    /// <summary>Sizes and the stop reason of a reply, for the log. Never any of the text. finish_reason=length means the reply was cut off.</summary>
    public static string Shape(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var choice = doc.RootElement.GetProperty("choices")[0];
            var finish = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
            var len = choice.GetProperty("message").TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()!.Length : -1;
            var tokens = doc.RootElement.TryGetProperty("usage", out var u) && u.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : -1;
            return $"finish_reason={finish}, content_chars={len}, completion_tokens={tokens}";
        }
        catch (Exception) { return "reply shape unreadable"; }
    }

    /// <summary>
    /// One more try when the reply could not be read. Every try is counted against the caps by the caller. Nothing more is tried
    /// once the shared time budget has gone. <paramref name="keep"/> chooses what stands when both have answered.
    /// </summary>
    public static async Task<T> OnceMoreIfUnreadableAsync<T>(Func<Task<T>> once, Func<T, bool> unreadable, Func<T, T, T> keep, Action<T> onRetry, CancellationToken budget)
    {
        var first = await once();
        if (!unreadable(first) || budget.IsCancellationRequested) return first;
        onRetry(first);
        return keep(first, await once());
    }

    /// <summary>
    /// The JSON object in a model's reply: code fences and any words around it are dropped. If more than one object is
    /// there, the span between them does not parse, so the reply fails closed like any other unreadable reply.
    /// </summary>
    public static string JsonIn(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : content.Trim();
    }

    /// <summary>For flags that only ever bring a human in: a JSON true, or the word true. Missing or anything else is false.</summary>
    public static bool IsTrue(JsonElement value) =>
        value.ValueKind == JsonValueKind.True
        || (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString()?.Trim(), "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads at most the first <paramref name="maxChars"/> characters of the body, on one line, with the key blanked out.</summary>
    public static async Task<string> SnippetAsync(HttpResponseMessage response, string? apiKey, int maxChars)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[maxChars * 4];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read), cts.Token);
                if (n == 0) break;
                read += n;
            }
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            if (!string.IsNullOrWhiteSpace(apiKey)) text = text.Replace(apiKey, "***");
            text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length > maxChars ? text[..maxChars] : text;
        }
        catch
        {
            return "(body could not be read)";
        }
    }
}

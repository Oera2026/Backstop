using Microsoft.Extensions.Logging;

namespace Backstop.Services.Api;

/// <summary>Shared helpers for failed Meta calls. Nothing here ever writes the key.</summary>
internal static class MetaHttp
{
    /// <summary>Logs the final URL, status and the first 300 characters of the reply body.</summary>
    public static async Task LogFailureAsync(ILogger logger, string caller, HttpResponseMessage response, MetaAiOptions options)
    {
        var body = await SnippetAsync(response, options.ApiKey, 300);
        logger.LogError("Meta AI ({Caller}) failed: status {StatusCode} for {Url} (model {Model}). Body: {Body}",
            caller, (int)response.StatusCode, response.RequestMessage?.RequestUri?.ToString() ?? options.ChatUrl, options.Model, body);
    }

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

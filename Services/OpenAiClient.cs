using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Globalization;

namespace RagAi.Services;

// Thin, dependency-free wrapper around the OpenAI-compatible REST API.
// Works with OpenAI directly, or any provider that mirrors the same
// /embeddings and /chat/completions endpoints (Azure OpenAI, Ollama, etc.)
// by changing OpenAI:BaseUrl in appsettings.json.
public class OpenAiClient
{
    private readonly HttpClient _http;
    private readonly string _embeddingModel;
    private readonly string _chatModel;
        private readonly Random _rng = new();

        private async Task<HttpResponseMessage> PostJsonWithRetriesAsync(string url, object payload, CancellationToken ct)
        {
            const int maxAttempts = 7; // increased attempts to be more resilient to transient rate limits

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                HttpResponseMessage? response = null;
                try
                {
                    response = await _http.PostAsJsonAsync(url, payload, ct).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                        return response;

                    // Only retry on transient status codes
                    if (response.StatusCode != (HttpStatusCode)429 && response.StatusCode != HttpStatusCode.ServiceUnavailable && response.StatusCode != HttpStatusCode.RequestTimeout)
                    {
                        return response;
                    }

                    if (attempt == maxAttempts)
                        return response;

                    // Look for Retry-After header
                    int delayMs = (int)(1000 * Math.Pow(2, Math.Min(attempt - 1, 6))); // cap exponent to avoid huge waits
                    if (response.Headers.TryGetValues("Retry-After", out var values))
                    {
                        var first = values.FirstOrDefault();
                        if (!string.IsNullOrEmpty(first))
                        {
                            if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                            {
                                delayMs = Math.Max(delayMs, seconds * 1000);
                            }
                            else if (DateTimeOffset.TryParse(first, out var date))
                            {
                                var ms = (int)Math.Max(0, (date - DateTimeOffset.UtcNow).TotalMilliseconds);
                                delayMs = Math.Max(delayMs, ms);
                            }
                        }
                    }

                    // Add some jitter
                    delayMs += _rng.Next(0, 500);

                    // Dispose the failed response before retrying
                    response.Dispose();
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    continue;
                }
                catch when (attempt < maxAttempts)
                {
                    // Transient network error - wait then retry
                    response?.Dispose();
                    var backoff = (int)(1000 * Math.Pow(2, attempt - 1)) + _rng.Next(0, 500);
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                    continue;
                }
            }

            // Shouldn't get here, but throw to satisfy the compiler
            throw new InvalidOperationException("Failed to send request after retries.");
        }

    public OpenAiClient(HttpClient http, IConfiguration config)
    {
        _http = http;

        var baseUrl = config["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1";
        var apiKey = config["OpenAI:ApiKey"];

        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        // Fail fast and give a clear actionable error when the API key is not configured.
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "OpenAI:ApiKey is not configured. Set it via 'dotnet user-secrets set \"OpenAI:ApiKey\" \"sk-...\"' or the OpenAI__ApiKey environment variable.");

        // Azure OpenAI uses an 'api-key' header rather than 'Authorization: Bearer'.
        if (baseUrl.Contains("openai.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            if (_http.DefaultRequestHeaders.Contains("Authorization"))
                _http.DefaultRequestHeaders.Remove("Authorization");
            if (!_http.DefaultRequestHeaders.Contains("api-key"))
                _http.DefaultRequestHeaders.Add("api-key", apiKey);
        }
        else
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        _embeddingModel = config["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        _chatModel = config["OpenAI:ChatModel"] ?? "gpt-4o-mini";
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var payload = new { model = _embeddingModel, input = text };
        using var response = await PostJsonWithRetriesAsync("embeddings", payload, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var vector = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");

        var result = new float[vector.GetArrayLength()];
        var i = 0;
        foreach (var v in vector.EnumerateArray())
            result[i++] = v.GetSingle();

        return result;
    }

    // Kept intentionally small: one short system instruction + the retrieved
    // context + the question. No chat history, no extra formatting — this is
    // what keeps token usage (and cost) low on every call.
    public async Task<string> GetChatCompletionAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var payload = new
        {
            model = _chatModel,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        using var response = await PostJsonWithRetriesAsync("chat/completions", payload, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}

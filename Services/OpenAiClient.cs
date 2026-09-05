using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RagAi.Services;

// Thrown whenever the upstream OpenAI-compatible API rejects a call.
// Program.cs turns this into a clean JSON error response instead of a
// raw, unhandled-exception 500.
public class LlmException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public LlmException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

// Thin, dependency-free wrapper around an OpenAI-compatible REST API.
// The default configuration targets a local Ollama server. It can also use
// any provider that exposes /embeddings and /chat/completions.
public class OpenAiClient
{
    private readonly HttpClient _http;
    private readonly string _embeddingModel;
    private readonly string _chatModel;

    // Retry knobs for transient failures (429 rate-limits, 5xx). Kept small
    // and cheap: a couple of retries with short backoff is enough to ride
    // out a burst without turning every request into a long hang.
    private const int MaxRetries = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);

    public OpenAiClient(HttpClient http, IConfiguration config)
    {
        _http = http;

        var baseUrl = config["OpenAI:BaseUrl"] ?? "http://localhost:11434/v1";
        var apiKey = config["OpenAI:ApiKey"];

        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        _embeddingModel = config["OpenAI:EmbeddingModel"] ?? "embeddinggemma";
        _chatModel = config["OpenAI:ChatModel"] ?? "llama3.2";
    }

    // Embeds a single string. Prefer GetEmbeddingsAsync for multiple texts —
    // it sends them in one request instead of one-per-text, which is what
    // usually trips a 429 during ingest of a multi-chunk document.
    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
        => (await GetEmbeddingsAsync(new[] { text }, ct))[0];

    // Embeds many texts in a single API call. The OpenAI /embeddings endpoint
    // accepts an array for "input", so this is just as cheap as one call and
    // avoids hammering the rate limit with a call per chunk.
    public async Task<List<float[]>> GetEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return new List<float[]>();

        var payload = new { model = _embeddingModel, input = texts };
        using var response = await SendWithRetryAsync("embeddings", payload, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var data = doc.RootElement.GetProperty("data");

        // The API guarantees results back in the same order as the input.
        var results = new List<float[]>(data.GetArrayLength());
        foreach (var item in data.EnumerateArray())
        {
            var vector = item.GetProperty("embedding");
            var arr = new float[vector.GetArrayLength()];
            var i = 0;
            foreach (var v in vector.EnumerateArray())
                arr[i++] = v.GetSingle();
            results.Add(arr);
        }

        return results;
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

        using var response = await SendWithRetryAsync("chat/completions", payload, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    // Sends a POST, retrying on 429 (rate limit) and 5xx (transient server
    // error) with a short backoff. Honors the API's Retry-After header when
    // it sends one. Any other failure — or running out of retries — is
    // surfaced as an LlmException with a message worth showing a caller.
    private async Task<HttpResponseMessage> SendWithRetryAsync(string endpoint, object payload, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await _http.PostAsJsonAsync(endpoint, payload, ct);

            if (response.IsSuccessStatusCode)
                return response;

            var isRetryable = response.StatusCode == HttpStatusCode.TooManyRequests
                || (int)response.StatusCode >= 500;

            if (!isRetryable || attempt >= MaxRetries)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var reason = response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "The LLM/embedding provider rate-limited this request (429 Too Many Requests). " +
                      "Slow down or batch requests, then try again."
                    : $"The LLM/embedding provider returned {(int)response.StatusCode} {response.StatusCode}.";

                response.Dispose();
                throw new LlmException(response.StatusCode, $"{reason} Details: {Truncate(body, 500)}");
            }

            var delay = response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromSeconds(BaseDelay.TotalSeconds * Math.Pow(2, attempt));

            response.Dispose();
            await Task.Delay(delay, ct);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}

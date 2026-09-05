using RagAi.Models;

namespace RagAi.Services;

// Ties chunking + embeddings + retrieval + the LLM call together.
// Every knob here (chunk size, top-K, max context length) exists to keep
// token usage down, since that's the whole point of this project.
public class RagService
{
    private readonly OpenAiClient _openAi;
    private readonly VectorStore _store;
    private readonly IConfiguration _config;

    public RagService(OpenAiClient openAi, VectorStore store, IConfiguration config)
    {
        _openAi = openAi;
        _store = store;
        _config = config;
    }

    public async Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct)
    {
        var source = string.IsNullOrWhiteSpace(request.Source) ? "unnamed" : request.Source!;
        var chunkSize = _config.GetValue<int?>("Rag:ChunkSizeChars") ?? 500;
        var overlap = _config.GetValue<int?>("Rag:ChunkOverlapChars") ?? 50;

        var chunks = SplitIntoChunks(request.Text, chunkSize, overlap);

        var added = 0;
        var perRequestDelayMs = _config.GetValue<int?>("Rag:PerRequestDelayMs") ?? 200; // default small throttle
        foreach (var text in chunks)
        {
            var embedding = await _openAi.GetEmbeddingAsync(text, ct);
            _store.Add(new DocumentChunk(Guid.NewGuid().ToString("N"), source, text, embedding));
            added++;

            if (perRequestDelayMs > 0)
                await Task.Delay(perRequestDelayMs, ct);
        }

        return new IngestResponse(added, source);
    }

    public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken ct)
    {
        var topK = request.TopK ?? _config.GetValue<int?>("Rag:TopK") ?? 3;
        var maxContextChars = _config.GetValue<int?>("Rag:MaxContextChars") ?? 1500;

        var questionEmbedding = await _openAi.GetEmbeddingAsync(request.Question, ct);
        var matches = _store.Search(questionEmbedding, topK);

        if (matches.Count == 0)
        {
            return new AskResponse(
                "I don't have any ingested documents yet, so I can't answer that. " +
                "Call POST /api/ingest first.",
                new List<string>());
        }

        // Build the context out of the top matches, but hard-cap its length
        // so a single question never balloons into a huge, expensive prompt.
        var context = new System.Text.StringBuilder();
        var sources = new List<string>();
        foreach (var (chunk, _) in matches)
        {
            if (context.Length >= maxContextChars) break;
            context.AppendLine(chunk.Text);
            sources.Add(chunk.Source);
        }

        var trimmedContext = context.ToString();
        if (trimmedContext.Length > maxContextChars)
            trimmedContext = trimmedContext[..maxContextChars];

        const string systemPrompt =
            "Answer the user's question using ONLY the provided context. " +
            "Be concise. If the answer isn't in the context, say you don't know.";

        var userPrompt = $"Context:\n{trimmedContext}\n\nQuestion: {request.Question}";

        var answer = await _openAi.GetChatCompletionAsync(systemPrompt, userPrompt, ct);

        return new AskResponse(answer, sources.Distinct().ToList());
    }

    private static List<string> SplitIntoChunks(string text, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            chunks.Add(text.Substring(start, length).Trim());

            if (start + length >= text.Length) break;
            start += chunkSize - overlap;
        }

        return chunks.Where(c => c.Length > 0).ToList();
    }
}

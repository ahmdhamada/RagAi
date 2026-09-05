using RagAi.Models;

namespace RagAi.Services;

// Plain in-memory vector store. No database, no external vector DB —
// good enough for a demo / small knowledge base and keeps the whole
// project dependency-free. Swap for a real vector DB later if needed.
public class VectorStore
{
    private readonly List<DocumentChunk> _chunks = new();
    private readonly object _lock = new();

    public void Add(DocumentChunk chunk)
    {
        lock (_lock) _chunks.Add(chunk);
    }

    public List<(DocumentChunk Chunk, float Score)> Search(float[] queryEmbedding, int topK)
    {
        List<DocumentChunk> snapshot;
        lock (_lock) snapshot = _chunks.ToList();

        return snapshot
            .Select(c => (Chunk: c, Score: CosineSimilarity(queryEmbedding, c.Embedding)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 0;
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}

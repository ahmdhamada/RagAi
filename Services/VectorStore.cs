using RagAi.Models;
using RagAi.Data;
using Microsoft.EntityFrameworkCore;

namespace RagAi.Services;

// Stores chunks permanently in SQL Server. The embedding is kept as JSON so
// this works with LocalDB, SQL Server Express, and older SQL Server versions.
// Similarity calculation runs in the app for portability; use SQL Server 2025
// vector search when the knowledge base grows very large.
public class VectorStore
{
    private readonly RagDbContext _db;

    public VectorStore(RagDbContext db)
    {
        _db = db;
    }

    public async Task AddRangeAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default)
    {
        _db.DocumentChunks.AddRange(chunks.Select(StoredDocumentChunk.FromDomain));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<(DocumentChunk Chunk, float Score)>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken ct = default)
    {
        var chunks = await _db.DocumentChunks
            .AsNoTracking()
            .ToListAsync(ct);

        return chunks
            .Select(c => (Chunk: c.ToDomain(), Score: CosineSimilarity(queryEmbedding, c.Embedding)))
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

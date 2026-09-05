using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RagAi.Models;

namespace RagAi.Data;

public sealed class RagDbContext(DbContextOptions<RagDbContext> options) : DbContext(options)
{
    public DbSet<StoredDocumentChunk> DocumentChunks => Set<StoredDocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var chunk = modelBuilder.Entity<StoredDocumentChunk>();
        chunk.HasKey(x => x.Id);
        chunk.Property(x => x.Source).HasMaxLength(500).IsRequired();
        chunk.Property(x => x.Text).HasColumnType("nvarchar(max)").IsRequired();
        chunk.Property(x => x.EmbeddingJson).HasColumnType("nvarchar(max)").IsRequired();
        chunk.HasIndex(x => x.Source);
    }
}

public sealed class StoredDocumentChunk
{
    public string Id { get; set; } = null!;
    public string Source { get; set; } = null!;
    public string Text { get; set; } = null!;
    public string EmbeddingJson { get; set; } = null!;

    public float[] Embedding => JsonSerializer.Deserialize<float[]>(EmbeddingJson)
        ?? throw new InvalidOperationException("The stored embedding is invalid.");

    public static StoredDocumentChunk FromDomain(DocumentChunk chunk) => new()
    {
        Id = chunk.Id,
        Source = chunk.Source,
        Text = chunk.Text,
        EmbeddingJson = JsonSerializer.Serialize(chunk.Embedding)
    };

    public DocumentChunk ToDomain() => new(Id, Source, Text, Embedding);
}

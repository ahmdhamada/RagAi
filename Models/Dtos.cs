namespace RagAi.Models;

// A single chunk of a source document, together with its embedding vector.
public record DocumentChunk(string Id, string Source, string Text, float[] Embedding);

// Request to add a document's text to the knowledge base.
public record IngestRequest(string Text, string? Source);

public record IngestResponse(int ChunksAdded, string Source);

// Request to ask a question against the knowledge base.
public record AskRequest(string Question, int? TopK);

public record AskResponse(string Answer, List<string> Sources);

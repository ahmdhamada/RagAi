using RagAi.Models;
using RagAi.Services;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<OpenAiClient>();
builder.Services.AddSingleton<VectorStore>();
builder.Services.AddScoped<RagService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Add a document (or a chunk of text) to the in-memory knowledge base.
app.MapPost("/api/ingest", async (IngestRequest request, RagService rag, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest("Text is required.");

    var result = await rag.IngestAsync(request, ct);
    return Results.Ok(result);
});

// Ask a question; the API retrieves the most relevant chunks and asks the LLM.
app.MapPost("/api/ask", async (AskRequest request, RagService rag, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
        return Results.BadRequest("Question is required.");

    var result = await rag.AskAsync(request, ct);
    return Results.Ok(result);
});

app.MapGet("/", () => "RagAi is running. See /swagger for the API.");

app.Run();

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RagAi.Data;
using RagAi.Models;
using RagAi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<OpenAiClient>();
builder.Services.AddDbContext<RagDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("RagDatabase"),
        sqlOptions => sqlOptions.EnableRetryOnFailure()));
builder.Services.AddScoped<VectorStore>();
builder.Services.AddScoped<RagService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// This educational app creates its database/table on first run. Once the
// schema starts changing, replace EnsureCreated with EF Core migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RagDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseSwagger();
app.UseSwaggerUI();

// Global error handling: turns any unhandled exception into a clean JSON
// response instead of a raw stack trace.
//   - BadHttpRequestException (thrown by ASP.NET Core itself when the
//     request body isn't valid JSON, or is missing a required field) keeps
//     its real 400 status and a message that says what was wrong with the
//     request — this must stay a 400, not get flattened into a generic 500.
//   - LlmException (thrown by OpenAiClient when the provider rejects a
//     call — e.g. 429 Too Many Requests) is passed through with its real
//     status code and a friendly message.
//   - Anything else becomes a generic 500.
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;

        if (ex is LlmException llmEx)
        {
            context.Response.StatusCode = (int)llmEx.StatusCode;
            await context.Response.WriteAsJsonAsync(new { error = llmEx.Message });
            return;
        }

        if (ex is BadHttpRequestException badRequestEx)
        {
            context.Response.StatusCode = badRequestEx.StatusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "The request body could not be read. Make sure it's valid JSON wrapped in { }, " +
                        "with Content-Type: application/json.",
                details = badRequestEx.Message
            });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong processing the request." });
    });
});

// Add a document (or a chunk of text) to the in-memory knowledge base.
// Splits the text into chunks, embeds each one, and stores it for later
// retrieval by /api/ask.
app.MapPost("/api/ingest", async (IngestRequest request, RagService rag, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest("Text is required.");

    var result = await rag.IngestAsync(request, ct);
    return Results.Ok(result);
});

// Ask a question. Embeds the question, retrieves the closest matching
// chunks from the knowledge base, and asks the LLM to answer using only
// that retrieved context.
app.MapPost("/api/ask", async (AskRequest request, RagService rag, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
        return Results.BadRequest("Question is required.");

    var result = await rag.AskAsync(request, ct);
    return Results.Ok(result);
});

// Simple liveness check — confirms the API is up without calling the LLM.
app.MapGet("/", () => "RagAi is running. See /swagger for the API.");

app.Run();

# RagAi

A minimal ASP.NET Core (.NET 9) Web API that does Retrieval-Augmented Generation (RAG) with an LLM, kept deliberately small and cheap to run.

## What's here

- **No database, no vector DB** — chunks and their embeddings are kept in memory (`VectorStore`). Restarting the app clears the knowledge base.
- **No controllers, no MVC** — everything lives in `Program.cs` as two minimal-API endpoints.
- **One HTTP client wrapper** (`OpenAiClient`) that talks to any OpenAI-compatible `/embeddings` and `/chat/completions` API (OpenAI, Azure OpenAI, a local Ollama server, etc. — just change `OpenAI:BaseUrl`).

## Endpoints

- `POST /api/ingest` — `{ "text": "...", "source": "my-doc" }` → splits the text into chunks, embeds each one, stores it.
- `POST /api/ask` — `{ "question": "...", "topK": 3 }` → embeds the question, retrieves the closest chunks, asks the LLM, returns the answer + which sources it used.
- `GET /swagger` — try it from the browser.

## Setup

1. Set your API key (don't commit it — use `dotnet user-secrets` or an environment variable):
   ```
   dotnet user-secrets init
   dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
   ```
   or
   ```
   export OpenAI__ApiKey=sk-...
   ```
2. `dotnet run`
3. Open `/swagger`, call `/api/ingest` with some text, then `/api/ask` with a question about it.

## Why this stays cheap on tokens

Every knob that controls token usage lives in `appsettings.json` under `Rag`:

- `ChunkSizeChars` (default 500) — smaller chunks mean smaller embedding calls.
- `TopK` (default 3) — only the 3 best-matching chunks are ever sent to the LLM, not the whole knowledge base.
- `MaxContextChars` (default 1500) — hard cap on how much retrieved text goes into the prompt, even if `TopK` matches are larger.
- The chat prompt is just one short system instruction + the trimmed context + the question — no chat history, no few-shot examples, no extra formatting.
- Default models are the cheap ones: `text-embedding-3-small` and `gpt-4o-mini`. Change `OpenAI:EmbeddingModel` / `OpenAI:ChatModel` in `appsettings.json` if you need something bigger.

## Extending it later

- Swap `VectorStore` for a real vector database (pgvector, Qdrant, Azure AI Search) once you outgrow in-memory.
- Add auth, logging, or persistence as needed — this project intentionally leaves those out to stay small.

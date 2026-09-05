# RagAi

A minimal ASP.NET Core (.NET 9) Web API that does Retrieval-Augmented Generation (RAG) with an LLM, kept deliberately small and cheap to run.

## What's here

- **No database, no vector DB** — chunks and their embeddings are kept in memory (`VectorStore`). Restarting the app clears the knowledge base.
- **No controllers, no MVC** — everything lives in `Program.cs` as two minimal-API endpoints.
- **One HTTP client wrapper** (`OpenAiClient`) that talks to any OpenAI-compatible `/embeddings` and `/chat/completions` API. It defaults to a local Ollama server, so it does not require a paid cloud API key.

## Endpoints

- `POST /api/ingest` — `{ "text": "...", "source": "my-doc" }` → splits the text into chunks, embeds each one, stores it.
- `POST /api/ask` — `{ "question": "...", "topK": 3 }` → embeds the question, retrieves the closest chunks, asks the LLM, returns the answer + which sources it used.
- `GET /swagger` — try it from the browser.

## Setup

1. Install [Ollama](https://ollama.com/download), then pull the two local models the app uses:
   ```powershell
   ollama pull embeddinggemma
   ollama pull llama3.2
   ```
   Ollama normally runs at `http://localhost:11434`; the checked-in `appsettings.json` already targets its OpenAI-compatible `/v1` API. The value `ollama` for `OpenAI:ApiKey` is a harmless placeholder and is ignored by local Ollama.
2. `dotnet run` (or F5 in Visual Studio). Check the console output / `Properties/launchSettings.json` for the actual URL and port — it varies per machine.
3. Open `/swagger` at that URL, call `/api/ingest` with some text, then `/api/ask` with a question about it. Or use `RagAi.http` / `test-api.sh` — update the base URL at the top of whichever one you use to match your actual port first.

## Why this stays cheap on tokens

Every knob that controls token usage lives in `appsettings.json` under `Rag`:

- `ChunkSizeChars` (default 500) — smaller chunks mean smaller embedding calls.
- `TopK` (default 3) — only the 3 best-matching chunks are ever sent to the LLM, not the whole knowledge base.
- `MaxContextChars` (default 1500) — hard cap on how much retrieved text goes into the prompt, even if `TopK` matches are larger.
- The chat prompt is just one short system instruction + the trimmed context + the question — no chat history, no few-shot examples, no extra formatting.
- Default models are local: `embeddinggemma` for retrieval and `llama3.2` for chat. Change `OpenAI:EmbeddingModel` / `OpenAI:ChatModel` in `appsettings.json` to names you have pulled locally if you need something different.

## Extending it later

- Swap `VectorStore` for a real vector database (pgvector, Qdrant, Azure AI Search) once you outgrow in-memory.
- Add auth, logging, or persistence as needed — this project intentionally leaves those out to stay small.

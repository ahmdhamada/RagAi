#!/usr/bin/env bash
# Quick smoke test for the RagAi API. Run the app first (dotnet run, or F5 in
# Visual Studio), then run this script. It defaults to the port Visual
# Studio's launchSettings.json uses (http://localhost:60110). Override it if
# your console prints a different URL on startup (dotnet run from the CLI
# usually picks http://localhost:5000 / https://localhost:5001 instead):
#   BASE_URL=http://localhost:5000 ./test-api.sh

set -e
BASE_URL="${BASE_URL:-http://localhost:60110}"

echo "== Ingest doc 1 (company overview) =="
curl -s -X POST "$BASE_URL/api/ingest" \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Invento is a company based in Saudi Arabia. Our flagship product is an inventory management platform used by retail chains to track stock levels, automate reordering, and forecast demand across multiple warehouses.",
    "source": "company-overview"
  }' | python3 -m json.tool

echo
echo "== Ingest doc 2 (support info) =="
curl -s -X POST "$BASE_URL/api/ingest" \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Our support hours are Sunday to Thursday, 9 AM to 6 PM Riyadh time. Customers can reach support by email at support@invento.sa or through the in-app chat widget.",
    "source": "support-info"
  }' | python3 -m json.tool

echo
echo "== Ask: what does the product do? =="
curl -s -X POST "$BASE_URL/api/ask" \
  -H "Content-Type: application/json" \
  -d '{ "question": "What does Invento'"'"'s product do?", "topK": 3 }' | python3 -m json.tool

echo
echo "== Ask: support hours? =="
curl -s -X POST "$BASE_URL/api/ask" \
  -H "Content-Type: application/json" \
  -d '{ "question": "What are the support hours?" }' | python3 -m json.tool

echo
echo "== Ask: something not in the knowledge base =="
curl -s -X POST "$BASE_URL/api/ask" \
  -H "Content-Type: application/json" \
  -d '{ "question": "What is the CEO'"'"'s favorite color?" }' | python3 -m json.tool

echo
echo "== Ingest with missing text (expect 400) =="
curl -s -o /dev/null -w "HTTP %{http_code}\n" -X POST "$BASE_URL/api/ingest" \
  -H "Content-Type: application/json" \
  -d '{ "source": "empty-test" }'

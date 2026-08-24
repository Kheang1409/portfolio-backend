# RAG evaluation

The offline suite makes retrieval quality reproducible without Gemini, credentials, HTTP, MongoDB, or paid calls. Its 35 cases are grounded in the five knowledge records in `DemoDataSeedHostedService`. They cover exact identifiers, project names, semantic experience, architecture concepts, technology names, negative questions, and conversational follow-ups. Cases carry evidence IDs, expected terms/facts, a no-answer label, notes, and a deterministic tuning/holdout split.

## Run

```powershell
dotnet test KaiAssistant.RagEvaluation/KaiAssistant.RagEvaluation.csproj --no-restore --logger "console;verbosity=detailed"
```

The detailed output compares keyword, semantic, hybrid RRF, and hybrid RRF plus deterministic reranking. It reports Recall@1/3/5/10, Precision@3/5/10, MRR, failed case IDs, category results, and local mean/p50/p95 retrieval latency. Latencies are micro-benchmark observations from an in-memory five-document corpus; they are useful for relative local comparison, not production capacity claims.

## Ground truth and metrics

Relevance is labeled per document/chunk rather than treating every chunk in a matching document as relevant. Recall measures how much expected evidence appears in the first K results, precision measures the relevant share of returned results, and MRR rewards placing the first useful result early. Exact generated strings are deliberately not compared. Expected/forbidden answer facts are reserved for a separate optional answer-quality layer.

The initial pre-tuning baseline was: keyword, hybrid, and reranked Recall@5 `0.900`, Precision@5 `0.767`, MRR `0.883`; semantic-only was `0.000` for all retrieval metrics. The semantic result is expected because the current SHA-256 projection is deterministic but not meaning-aware and produces no results over the `0.7` threshold.

This remains the historical SHA-projection baseline. A separate 10-case semantic challenge set covers paraphrases, synonyms, concepts, and low lexical overlap. Offline CI verifies that SHA projection performs poorly on it, preventing a deterministic hash from being presented as semantic retrieval.

Evidence-driven tuning added title/source matching, stop-word removal, and a small deterministic vocabulary normalization for operational metrics and durable state. The post-tuning offline result is: keyword, hybrid, and reranked Recall@5 `1.000`, Precision@5 `0.833`, MRR `1.000`; semantic-only remains `0.000`. Keyword is therefore the best current strategy on both quality and local latency. Reranking did not improve ranking. Hybrid inherits keyword quality but receives no semantic contribution.

The holdout regression gate requires keyword Recall@5 and MRR of at least `0.90`. Thresholds are intentionally below the measured `1.00` baseline and are evaluated only on holdout cases. Reports calculate metrics dynamically; expected output values are not hard-coded into the evaluator.

## Reading failures and limitations

Failures print case IDs; detailed case objects retain the question, expected evidence, top chunks, ranks, and scores for debugger inspection. The checked-in corpus is intentionally tiny, so perfect post-tuning scores do not establish general retrieval quality. Add cases when real indexed knowledge is added, keeping new facts grounded in that corpus.

Conversation context is limited to the case's explicit prior fact; complete transcripts are not embedded. Current evidence says deterministic context-aware expansion is useful, but does not justify LLM query rewriting. The zero semantic baseline does justify replacing the hash projection with a real embedding model in a future measured phase. It does not justify an external reranker: the current reranker provides no quality gain.

The secured Gemini provider check ran on 2026-08-23. The 10 semantic challenge cases achieved Recall@1 `1.000`, Recall@5 `1.000`, and MRR `1.000`, with 172.3 ms mean and 189.9 ms p95 query-embedding latency. This is a provider/integration sanity result, not a production-corpus strategy comparison.

The configured live database had zero active documents, so real keyword-versus-semantic-versus-hybrid metrics, tuning/holdout results, reranker comparison, negative-query distributions, and threshold tuning could not be measured. `knowledge-gemini-001-v2` remains inactive, keyword remains the default, and query routing or rewriting is still not justified.

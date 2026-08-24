using System.Diagnostics;
using System.Runtime.CompilerServices;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.DTOs;
using KaiAssistant.Domain.Entities;
using Microsoft.Extensions.Logging;
namespace KaiAssistant.Application.Services;
public class AssistantOrchestrator : IAssistantOrchestrator
{
    private readonly IAssistantService _assistantService;
    private readonly IAssistantContextBuilder _contextBuilder;
    private readonly IResponseCacheService _responseCache;
    private readonly ITokenBudgetService _tokenBudget;
    private readonly IConversationRepository _conversationRepo;
    private readonly ILogger<AssistantOrchestrator> _logger;
    private long _lastLatencyMs;
    private int _activeStreamCount;
    private DateTimeOffset _lastAiCallAt = DateTimeOffset.UtcNow;
    public AssistantOrchestrator(
        IAssistantService assistantService,
        IAssistantContextBuilder contextBuilder,
        IResponseCacheService responseCache,
        ITokenBudgetService tokenBudget,
        IConversationRepository conversationRepo,
        ILogger<AssistantOrchestrator> logger)
    {
        _assistantService = assistantService;
        _contextBuilder = contextBuilder;
        _responseCache = responseCache;
        _tokenBudget = tokenBudget;
        _conversationRepo = conversationRepo;
        _logger = logger;
    }
    public async IAsyncEnumerable<AssistantStreamEvent> OrchestrateStreamAsync(
        string userMessage,
        string conversationId,
        string? userId = null,
        [EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _activeStreamCount++;
        var terminalEmitted = false;
        try
        {
            _logger.LogInformation(
                "Orchestrate: RequestId={RequestId}, ConversationId={ConversationId}, MessageLen={Len}",
                requestId, conversationId, userMessage.Length);
            // 1. Validate input
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                yield return new AssistantStreamEvent { Type = StreamEventType.Error, Error = "User message cannot be empty" };
                yield break;
            }
            if (userMessage.Length > 10000)
            {
                yield return new AssistantStreamEvent { Type = StreamEventType.Error, Error = "Message too long (max 10000 chars)" };
                yield break;
            }
            // 2. Build composed context
            var context = await _contextBuilder.BuildContextAsync(
                conversationId,
                userMessage,
                userId,
                null,
                cancellationToken)
                .ConfigureAwait(false);
            // 3. Check response cache
            var cacheKey = _responseCache.GenerateCacheKey(userMessage);
            var cachedResponse = await _responseCache.GetCachedResponseAsync(cacheKey, cancellationToken)
                .ConfigureAwait(false);
            if (cachedResponse != null)
            {
                _logger.LogInformation("Cache hit: RequestId={RequestId}, Key={Key}", requestId, cacheKey);
                yield return new AssistantStreamEvent { Type = StreamEventType.Start };
                yield return new AssistantStreamEvent { Type = StreamEventType.Token, Content = cachedResponse };
                yield return new AssistantStreamEvent { Type = StreamEventType.End };
                yield break;
            }
            _logger.LogInformation("Cache miss: RequestId={RequestId}", requestId);
            // 4. Stream from AI, collecting response
            var stopwatch = Stopwatch.StartNew();
            var responseBuilder = new System.Text.StringBuilder();
            var tokenCount = 0;
            _lastAiCallAt = DateTimeOffset.UtcNow;
            await foreach (var chunk in _assistantService.StreamAsync(
                userMessage,
                context.ContextMessages.ToArray(),
                new AssistantContext {  },
                cancellationToken)
                .ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return new AssistantStreamEvent { Type = StreamEventType.Error, Error = "request cancelled" };
                    terminalEmitted = true;
                    yield break;
                }
                yield return new AssistantStreamEvent { Type = StreamEventType.Token, Content = chunk.Text };
                responseBuilder.Append(chunk.Text);
                tokenCount++;
            }
            stopwatch.Stop();
            _lastLatencyMs = stopwatch.ElapsedMilliseconds;
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            if (responseBuilder.Length > 0)
            {
                var fullResponse = responseBuilder.ToString();
                var conversation = await _conversationRepo.GetByIdAsync(conversationId, cancellationToken)
                    .ConfigureAwait(false);
                if (conversation == null && userId != null)
                {
                    conversation = await _conversationRepo.CreateAsync(userId, cancellationToken)
                        .ConfigureAwait(false);
                    conversationId = conversation.ConversationId;
                }
                if (conversation != null)
                {
                    conversation.Messages.Add(new ConversationMessage
                    {
                        Role = "user",
                        Content = userMessage
                    });
                    conversation.Messages.Add(new ConversationMessage
                    {
                        Role = "assistant",
                        Content = fullResponse
                    });
                    conversation.UpdatedAt = DateTimeOffset.UtcNow;
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await _conversationRepo.UpdateAsync(conversation, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                // 6. Cache response
                if (!cancellationToken.IsCancellationRequested)
                {
                    await _responseCache.CacheResponseAsync(
                        cacheKey,
                        fullResponse,
                        TimeSpan.FromMinutes(10),
                        cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            if (!terminalEmitted)
            {
                yield return new AssistantStreamEvent { Type = StreamEventType.End };
            }
            _logger.LogInformation(
                "Orchestrate complete: RequestId={RequestId}, Latency={Latency}ms, Tokens={Tokens}",
                requestId, _lastLatencyMs, tokenCount);
        }
        finally
        {
            _activeStreamCount--;
        }
    }
    public async Task<OrchestratorDiagnostics> GetDiagnosticsAsync(
        System.Threading.CancellationToken cancellationToken = default)
    {
        var (hits, misses, hitRate) = await _responseCache.GetMetricsAsync(cancellationToken)
            .ConfigureAwait(false);
        return new OrchestratorDiagnostics
        {
            LastLatencyMs = _lastLatencyMs,
            CacheHitRate = hitRate,
            ActiveStreamCount = _activeStreamCount,
            LastAiCallAt = _lastAiCallAt
        };
    }
}

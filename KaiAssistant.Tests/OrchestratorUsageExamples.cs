using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.DTOs;
using KaiAssistant.Domain.Entities;
using Xunit;
namespace KaiAssistant.Tests;
public class OrchestratorUsageExample
{
    private readonly IAssistantOrchestrator _orchestrator;
    private readonly IResponseCacheService _cache;
    private readonly IConversationRepository _conversationRepo;
    private readonly IVisitDeduplicationService _dedup;
    private readonly ITokenBudgetService _tokenBudget;
    public OrchestratorUsageExample(
        IAssistantOrchestrator orchestrator,
        IResponseCacheService cache,
        IConversationRepository conversationRepo,
        IVisitDeduplicationService dedup,
        ITokenBudgetService tokenBudget)
    {
        _orchestrator = orchestrator;
        _cache = cache;
        _conversationRepo = conversationRepo;
        _dedup = dedup;
        _tokenBudget = tokenBudget;
    }
    public async IAsyncEnumerable<AssistantStreamEvent> StreamAssistantResponse(
        string conversationId,
        string userMessage,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in _orchestrator.OrchestrateStreamAsync(
            userMessage,
            conversationId,
            userId,
            cancellationToken)
            .ConfigureAwait(false))
        {
            yield return chunk;
        }
    }
    public async Task<List<ConversationMessage>> GetConversationContext(
        string conversationId,
        int maxMessages = 10,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepo
            .GetByIdAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);
        if (conversation == null)
            return new();
        return conversation.GetContextWindow(maxMessages);
    }
    public async Task<string?> TryGetCachedResponse(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var key = _cache.GenerateCacheKey(userMessage);
        return await _cache.GetCachedResponseAsync(key, cancellationToken)
            .ConfigureAwait(false);
    }
    public int EstimateRequestTokens(
        string systemPrompt,
        string userMessage,
        List<ConversationMessage> history)
    {
        var context = new ConversationContext
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            ContextMessages = history
        };
        return _tokenBudget.CalculateContextTokens(context);
    }
    public async Task<bool> IsNewVisit(
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        return await _dedup.ShouldCountVisitAsync(
            ipAddress,
            userAgent,
            TimeSpan.FromSeconds(30),
            cancellationToken)
            .ConfigureAwait(false);
    }
    public async Task PrintDiagnostics(CancellationToken cancellationToken = default)
    {
        var diags = await _orchestrator.GetDiagnosticsAsync(cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"=== Orchestrator Diagnostics ===");
        Console.WriteLine($"Last Latency: {diags.LastLatencyMs}ms");
        Console.WriteLine($"Cache Hit Rate: {diags.CacheHitRate:P}");
        Console.WriteLine($"Active Streams: {diags.ActiveStreamCount}");
        Console.WriteLine($"Last AI Call: {diags.LastAiCallAt}");
        var (hits, misses, _) = await _cache.GetMetricsAsync(cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"Total Cache Hits: {hits}");
        Console.WriteLine($"Total Cache Misses: {misses}");
    }
}
public class OrchestratorIntegrationTests
{
    [Fact(Skip = "Example test file; requires a full DI container setup.")]
    public async Task OrchestrateStreamAsync_WithValidInput_ReturnsStream()
    {
        // Arrange
        var orchestrator = CreateTestOrchestrator();
        var conversationId = Guid.NewGuid().ToString("N");
        var userMessage = "Hello, how are you?";
        // Act
        var chunks = new List<AssistantStreamEvent>();
        await foreach (var chunk in orchestrator.OrchestrateStreamAsync(
            userMessage,
            conversationId,
            "test-user",
            CancellationToken.None))
        {
            chunks.Add(chunk);
        }
        // Assert
        Assert.NotEmpty(chunks);
        Assert.Equal(StreamEventType.Token, chunks[0].Type);
    }
    [Fact(Skip = "Example test file; requires a full DI container setup.")]
    public async Task OrchestrateStreamAsync_WithLongMessage_TrimmsContext()
    {
        // Arrange
        var orchestrator = CreateTestOrchestrator();
        var conversationId = Guid.NewGuid().ToString("N");
        var longMessage = new string('a', 10000); // Valid but large
        // Act
        var chunks = new List<AssistantStreamEvent>();
        await foreach (var chunk in orchestrator.OrchestrateStreamAsync(
            longMessage,
            conversationId,
            cancellationToken: CancellationToken.None))
        {
            chunks.Add(chunk);
        }
        // Assert
        Assert.NotEmpty(chunks);
    }
    [Fact(Skip = "Example test file; requires a full DI container setup.")]
    public async Task OrchestrateStreamAsync_WithEmptyMessage_ReturnsError()
    {
        // Arrange
        var orchestrator = CreateTestOrchestrator();
        // Act
        var chunks = new List<AssistantStreamEvent>();
        await foreach (var chunk in orchestrator.OrchestrateStreamAsync(
            "",
            Guid.NewGuid().ToString("N"),
            cancellationToken: CancellationToken.None))
        {
            chunks.Add(chunk);
        }
        // Assert
        Assert.Equal(StreamEventType.Error, chunks[0].Type);
    }
    private static IAssistantOrchestrator CreateTestOrchestrator()
    {
        // Mock dependencies for testing
        throw new NotImplementedException("Use actual DI container in integration tests");
    }
}

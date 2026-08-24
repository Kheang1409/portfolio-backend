namespace KaiAssistant.Application.Services;
using global::KaiAssistant.Application.DTOs;
using global::KaiAssistant.Application.Interfaces;
using global::KaiAssistant.Domain.Entities;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
public sealed class EnhancedAssistantOrchestrator : IEnhancedAssistantOrchestrator
{
    private readonly AssistantOrchestrator _baseOrchestrator;
    private readonly EnhancedContextBuilder _contextBuilder;
    private readonly IToolRegistry _toolRegistry;
    private readonly IConversationSummaryService _summaryService;
    private readonly IObservabilityService _observability;
    private readonly IConversationRepository _conversationRepo;
    private readonly ILogger<EnhancedAssistantOrchestrator> _logger;
    public EnhancedAssistantOrchestrator(
        AssistantOrchestrator baseOrchestrator,
        EnhancedContextBuilder contextBuilder,
        IToolRegistry toolRegistry,
        IConversationSummaryService summaryService,
        IObservabilityService observability,
        IConversationRepository conversationRepo,
        ILogger<EnhancedAssistantOrchestrator> logger)
    {
        _baseOrchestrator = baseOrchestrator;
        _contextBuilder = contextBuilder;
        _toolRegistry = toolRegistry;
        _summaryService = summaryService;
        _observability = observability;
        _conversationRepo = conversationRepo;
        _logger = logger;
    }
    public async IAsyncEnumerable<AssistantStreamEvent> OrchestrateWithEnhancedStreamingAsync(
        string userMessage,
        string conversationId,
        string? userId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        var startTime = Stopwatch.StartNew();
        var toolCalls = new List<ToolCall>();
        var ragDocCount = 0;
            _logger.LogInformation(
                "Enhanced stream start: RequestId={RequestId}, ConversationId={ConversationId}",
                requestId, conversationId);
            // PHASE 1: VALIDATE & BUILD CONTEXT WITH RAG
            if (string.IsNullOrWhiteSpace(userMessage) || userMessage.Length > 10000)
            {
                yield return new AssistantStreamEvent { Type = StreamEventType.Error, Error = "Invalid input" };
                yield break;
            }
            var context = await _contextBuilder.BuildContextWithRagAsync(
                conversationId,
                userMessage,
                userId,
                null,
                cancellationToken)
                .ConfigureAwait(false);
            ragDocCount = context.Metadata.TryGetValue("ragDocumentsCount", out var ragObj)
                ? Convert.ToInt32(ragObj)
                : 0;
            // Emit metadata chunk
            yield return new AssistantStreamEvent
            {
                Type = StreamEventType.Metadata,
                Metadata = new StreamMetadata
                {
                    RagDocumentsCount = ragDocCount,
                    ToolCallsCount = 0
                }
            };
            // PHASE 2: STREAM FROM AI WITH TOOL CALL DETECTION
            var responseBuilder = new StringBuilder();
            var tokenCount = 0;
            // Stream base orchestrator (which handles cache, AI calls, etc.)
            await foreach (var chunk in _baseOrchestrator.OrchestrateStreamAsync(
                userMessage,
                conversationId,
                userId,
                cancellationToken)
                .ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // ensure terminal event
                    yield return new AssistantStreamEvent { Type = StreamEventType.Error, Error = "request cancelled" };
                    yield break;
                }
                // Strongly-typed events from base orchestrator
                switch (chunk.Type)
                {
                    case StreamEventType.Token:
                        responseBuilder.Append(chunk.Content ?? string.Empty);
                        tokenCount++;
                        var toolCall = DetectToolCall(chunk.Content ?? string.Empty);
                        if (toolCall != null)
                        {
                            toolCalls.Add(toolCall);
                            yield return new AssistantStreamEvent { Type = StreamEventType.ToolCall, ToolCall = toolCall };
                            var toolResult = await ExecuteToolAsync(toolCall.Name, toolCall.Arguments ?? new(), cancellationToken)
                                .ConfigureAwait(false);
                            yield return new AssistantStreamEvent
                            {
                                Type = StreamEventType.ToolResult,
                                ToolResult = new ToolExecutionResult
                                {
                                    ToolName = toolCall.Name,
                                    Success = toolResult != null,
                                    Result = toolResult
                                }
                            };
                            _observability.RecordAiCallMetric(0, success: true);
                        }
                        else
                        {
                            yield return new AssistantStreamEvent { Type = StreamEventType.Token, Content = chunk.Content };
                        }
                        break;
                    case StreamEventType.Error:
                        yield return new AssistantStreamEvent { Type = StreamEventType.Error, Error = chunk.Error };
                        break;
                    case StreamEventType.End:
                        // Propagate end event from base orchestrator
                        yield return chunk;
                        break;
                }
            }
            startTime.Stop();
            // PHASE 3: PERSIST & SUMMARIZE
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            if (responseBuilder.Length > 0)
            {
                var conversation = await _conversationRepo.GetByIdAsync(conversationId, cancellationToken)
                    .ConfigureAwait(false);
                if (conversation != null)
                {
                    // Add messages
                    conversation.Messages.Add(new ConversationMessage
                    {
                        Role = "user",
                        Content = userMessage
                    });
                    conversation.Messages.Add(new ConversationMessage
                    {
                        Role = "assistant",
                        Content = responseBuilder.ToString()
                    });
                    // Auto-summarize if threshold reached
                    if (_summaryService.ShouldSummarize(conversation.Messages.Count))
                    {
                        await _summaryService.GenerateAndUpdateSummaryAsync(
                            conversation,
                            cancellationToken)
                            .ConfigureAwait(false);
                    }
                    conversation.UpdatedAt = DateTimeOffset.UtcNow;
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await _conversationRepo.UpdateAsync(conversation, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            // PHASE 4: EMIT FINAL METADATA
            yield return new AssistantStreamEvent
            {
                Type = StreamEventType.Metadata,
                Metadata = new StreamMetadata
                {
                    LatencyMs = startTime.ElapsedMilliseconds,
                    TokensEstimated = tokenCount,
                    ToolCallsCount = toolCalls.Count,
                    RagDocumentsCount = ragDocCount
                }
            };
            // Ensure terminal end event is emitted
            yield return new AssistantStreamEvent { Type = StreamEventType.End };
            _observability.RecordAiCallMetric(startTime.ElapsedMilliseconds, success: true);
            _logger.LogInformation(
                "Enhanced stream complete: RequestId={RequestId}, Latency={Latency}ms, ToolCalls={ToolCalls}",
                requestId,
                startTime.ElapsedMilliseconds,
                toolCalls.Count);
    }
    public async Task<object> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tool = _toolRegistry.GetTool(toolName);
            if (tool == null)
            {
                _logger.LogWarning("Tool not found: {ToolName}", toolName);
                return new { error = $"Tool '{toolName}' not found" };
            }
            if (!tool.ValidateArguments(arguments))
            {
                _logger.LogWarning("Invalid arguments for tool: {ToolName}", toolName);
                return new { error = "Invalid arguments" };
            }
            var result = await tool.ExecuteAsync(arguments, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Tool executed: {ToolName}", toolName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", toolName);
            return new { error = ex.Message };
        }
    }
    public IAsyncEnumerable<AssistantStreamEvent> OrchestrateStreamAsync(
        string userMessage,
        string conversationId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        return _baseOrchestrator.OrchestrateStreamAsync(
            userMessage,
            conversationId,
            userId,
            cancellationToken);
    }
    public async Task<OrchestratorDiagnostics> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _baseOrchestrator.GetDiagnosticsAsync(cancellationToken);
    }
    private ToolCall? DetectToolCall(string content)
    {
        // Heuristic: detect patterns like "CALL[tool_name](arg1=val1, arg2=val2)"
        if (content.Contains("CALL[") && content.Contains("]("))
        {
            var start = content.IndexOf("CALL[");
            var end = content.IndexOf("](", start);
            if (start >= 0 && end > start)
            {
                var toolName = content.Substring(start + 5, end - start - 5);
                var argsStr = content.Substring(end + 2);
                var argsEnd = argsStr.IndexOf(")");
                if (argsEnd > 0)
                {
                    var argsString = argsStr.Substring(0, argsEnd);
                    var args = ParseToolArguments(argsString);
                    return new ToolCall
                    {
                        Name = toolName,
                        Arguments = args
                    };
                }
            }
        }
        return null;
    }
    private Dictionary<string, object> ParseToolArguments(string argsString)
    {
        var args = new Dictionary<string, object>();
        var pairs = argsString.Split(',');
        foreach (var pair in pairs)
        {
            var kv = pair.Trim().Split('=');
            if (kv.Length == 2)
            {
                args[kv[0].Trim()] = kv[1].Trim();
            }
        }
        return args;
    }
}

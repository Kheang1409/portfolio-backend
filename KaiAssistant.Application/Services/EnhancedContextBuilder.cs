namespace KaiAssistant.Application.Services;
using global::KaiAssistant.Application.Interfaces;
using global::KaiAssistant.Domain.Entities;
using System.Text;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
public sealed class EnhancedContextBuilder
{
    private readonly IConversationRepository _conversationRepo;
    private readonly ITokenBudgetService _tokenBudget;
    private readonly ISemanticSearchService _semanticSearch;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<EnhancedContextBuilder> _logger;
    private const int MAX_CONTEXT_TOKENS = 6000;
    private const int MAX_HISTORY_MESSAGES = 15;
    private const int RAG_TOP_K = 3;
    private const float RAG_MIN_SIMILARITY = 0.5f;
    private const int SUMMARY_UPDATE_INTERVAL = 10;  // After N messages
    public EnhancedContextBuilder(
        IConversationRepository conversationRepo,
        ITokenBudgetService tokenBudget,
        ISemanticSearchService semanticSearch,
        IToolRegistry toolRegistry,
        ILogger<EnhancedContextBuilder> logger)
    {
        _conversationRepo = conversationRepo;
        _tokenBudget = tokenBudget;
        _semanticSearch = semanticSearch;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }
    public async Task<ConversationContext> BuildContextWithRagAsync(
        string conversationId,
        string userMessage,
        string? userId = null,
        Dictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = GetEnhancedSystemPrompt();
        var contextMessages = new List<ConversationMessage>();
        var ragDocuments = new List<string>();
        // 1. LOAD CONVERSATION HISTORY (with summaries)
        var conversation = await _conversationRepo.GetByIdAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);
        if (conversation != null)
        {
            // If conversation has summary and many messages, use summary + recent messages
            if (!string.IsNullOrEmpty(conversation.Summary) && conversation.Messages.Count > SUMMARY_UPDATE_INTERVAL)
            {
                // Add summary as context
                contextMessages.Add(new ConversationMessage
                {
                    Role = "system",
                    Content = $"Previous conversation summary:\n{conversation.Summary}"
                });
                // Add only recent messages
                var recentMessages = conversation.Messages
                    .TakeLast(Math.Max(3, MAX_HISTORY_MESSAGES / 2))
                    .ToList();
                contextMessages.AddRange(recentMessages);
                _logger.LogInformation(
                    "Using conversation summary: ConversationId={ConversationId}, SummaryLength={Length}",
                    conversationId,
                    conversation.Summary.Length);
            }
            else
            {
                // Use full sliding window
                var window = conversation.GetContextWindow(MAX_HISTORY_MESSAGES);
                contextMessages.AddRange(window);
            }
            // Trim to token budget
            contextMessages = _tokenBudget.TrimContextToTokenBudget(
                contextMessages,
                MAX_CONTEXT_TOKENS,
                keepOldest: true);
        }
        // 2. RETRIEVE RELEVANT DOCUMENTS VIA SEMANTIC SEARCH
        try
        {
            var searchResults = await _semanticSearch.SearchAsync(
                userMessage,
                topK: RAG_TOP_K,
                minSimilarity: RAG_MIN_SIMILARITY,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (var result in searchResults)
            {
                ragDocuments.Add(
                    $"[{result.Document.Source}] {result.Document.Content[..Math.Min(300, result.Document.Content.Length)]}... " +
                    $"(relevance: {result.Similarity:P0})");
            }
            if (ragDocuments.Count > 0)
            {
                // Prepend RAG context to system message
                systemPrompt = PrependRagContext(systemPrompt, ragDocuments);
                _logger.LogInformation("RAG: Retrieved {Count} relevant documents", ragDocuments.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG search failed, continuing without retrieval");
        }
        // 3. BUILD METADATA
        var meta = metadata ?? new();
        meta["ragDocumentsCount"] = ragDocuments.Count;
        meta["toolsAvailable"] = _toolRegistry.GetAllTools().Count();
        var context = new ConversationContext
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            ContextMessages = contextMessages,
            Metadata = meta
        };
        context.EstimatedTokens = _tokenBudget.CalculateContextTokens(context);
        _logger.LogInformation(
            "Built enhanced context: ConversationId={ConversationId}, History={History}, Rag={Rag}, Tokens≈{Tokens}",
            conversationId,
            contextMessages.Count,
            ragDocuments.Count,
            context.EstimatedTokens);
        return context;
    }
    private string GetEnhancedSystemPrompt()
    {
        var toolManifest = _toolRegistry.GetFormattedToolManifest();
        return $@"You are Kai, a thoughtful AI assistant and AI engineer specializing in portfolio consulting and system design.
Your role is to help users with their questions clearly and concisely. You have access to the following tools:
{toolManifest}
When answering questions:
- Be direct and honest
- Ask clarifying questions if needed
- Use available tools to retrieve information when appropriate
- Provide practical examples and code snippets
- Acknowledge limitations
- Stay focused on the user's actual question
- For portfolio/experience questions, use get_portfolio_projects and get_resume tools
- For system status questions, use get_system_status tool";
    }
    private string PrependRagContext(string systemPrompt, List<string> documents)
    {
        var ragSection = new StringBuilder();
        ragSection.AppendLine("\nRelevant context from knowledge base:");
        ragSection.AppendLine();
        foreach (var doc in documents)
        {
            ragSection.AppendLine($"- {doc}");
        }
        return systemPrompt + "\n" + ragSection.ToString();
    }
}
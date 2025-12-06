using System.Text.Json;
using System.Text.Json.Nodes;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KaiAssistant.Application.Services;

public class AssistantService : IAssistantService
{
    private readonly IResumeContextProvider _resumeProvider;
    private readonly IAiPromptBuilder _promptBuilder;
    private readonly IAiModelGateway _gateway;
    private readonly IOptions<GeminiSettings> _options;
    private readonly ILogger<AssistantService> _logger;

    private ResumeChunk[] _resumeChunks = Array.Empty<ResumeChunk>();

    public AssistantService(IResumeContextProvider resumeProvider, IAiPromptBuilder promptBuilder, IAiModelGateway gateway, IOptions<GeminiSettings> options, ILogger<AssistantService> logger)
    {
        _resumeProvider = resumeProvider;
        _promptBuilder = promptBuilder;
        _gateway = gateway;
        _options = options;
        _logger = logger;
    }

    public async Task<string> AskQuestionAsync(string question, CancellationToken cancellationToken = default)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(question))
        {
            return "Please provide a question.";
        }

        var snapshot = _resumeChunks.Length > 0 ? _resumeChunks : await _resumeProvider.GetResumeChunksAsync(cancellationToken).ConfigureAwait(false);
        var resumeMissing = snapshot.Length == 0;
        if (resumeMissing)
        {
            _logger.LogInformation("No resume loaded; answering without resume context.");
        }

        bool includePersonalDetails = _options.Value.IncludePersonalDetails;
        var relevantChunks = await _resumeProvider.GetRelevantChunksAsync(question, cancellationToken).ConfigureAwait(false);
        var filteredChunks = includePersonalDetails
            ? relevantChunks
            : relevantChunks.Where(c => !c.Label.Contains("Personal", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (!includePersonalDetails && filteredChunks.Length < relevantChunks.Length)
        {
            _logger.LogInformation("Personal resume fields filtered per settings");
        }
        else if (filteredChunks.Length > 0)
        {
            _logger.LogInformation("Resume chunks loaded");
        }

        var combinedResume = string.Join("\n\n", filteredChunks.Select(c => $"{c.Label}: {c.Content}"));

        int maxChars = _options.Value.PromptMaxChars;
        if (combinedResume.Length > maxChars)
        {
            combinedResume = combinedResume.Substring(0, maxChars) + "\n\n...[truncated resume context]";
        }

        var systemPrompt = _promptBuilder.BuildSystemPrompt();
        if (resumeMissing)
        {
            systemPrompt += "\n\nNote: I don't have access to the user's resume. Answer based on general knowledge and be explicit when information is missing. Offer concise suggestions for follow-up questions to get more details.";
        }

        var genConfig = _promptBuilder.BuildGenerationConfig(question);
        var genConfigNode = JsonSerializer.SerializeToNode(genConfig) as JsonObject ?? new JsonObject();

        // MCP-compliant request: contents array with role/parts structure
        var contentsArray = new JsonArray(
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = systemPrompt })
            },
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = $"Resume context:\n{combinedResume}\n\nUser question:\n{question}" })
            }
        );

        var generationConfig = new JsonObject();
        if (genConfigNode.TryGetPropertyValue("temperature", out var tempNode)) 
            generationConfig["temperature"] = JsonNode.Parse(tempNode!.ToJsonString());
        if (genConfigNode.TryGetPropertyValue("topK", out var topKNode)) 
            generationConfig["topK"] = JsonNode.Parse(topKNode!.ToJsonString());
        if (genConfigNode.TryGetPropertyValue("topP", out var topPNode)) 
            generationConfig["topP"] = JsonNode.Parse(topPNode!.ToJsonString());
        if (genConfigNode.TryGetPropertyValue("maxOutputTokens", out var maxNode)) 
            generationConfig["maxOutputTokens"] = JsonNode.Parse(maxNode!.ToJsonString());
        if (genConfigNode.TryGetPropertyValue("candidateCount", out var candNode)) 
            generationConfig["candidateCount"] = JsonNode.Parse(candNode!.ToJsonString());

        var payloadNode = new JsonObject
        {
            ["contents"] = contentsArray,
            ["generationConfig"] = generationConfig,
            ["safetySettings"] = new JsonArray(
                new JsonObject { ["category"] = "HARM_CATEGORY_HARASSMENT", ["threshold"] = "BLOCK_MEDIUM_AND_ABOVE" },
                new JsonObject { ["category"] = "HARM_CATEGORY_HATE_SPEECH", ["threshold"] = "BLOCK_MEDIUM_AND_ABOVE" },
                new JsonObject { ["category"] = "HARM_CATEGORY_SEXUALLY_EXPLICIT", ["threshold"] = "BLOCK_MEDIUM_AND_ABOVE" },
                new JsonObject { ["category"] = "HARM_CATEGORY_DANGEROUS_CONTENT", ["threshold"] = "BLOCK_MEDIUM_AND_ABOVE" }
            )
        };

        var json = payloadNode.ToJsonString();
        _logger.LogInformation("Gemini request payload: {Payload}", json);

        var modelsToTry = _options.Value.ModelNames ?? new List<string>();
        if (modelsToTry.Count == 0)
        {
            _logger.LogError("No Gemini models configured");
            return "Service configuration error. Please try again later.";
        }

        (string? responseBody, string? usedModel) = await _gateway.SendGenerationRequestAsync(json, modelsToTry.Distinct(StringComparer.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            _logger.LogWarning("No successful response received from Gemini models");
            return "I'm temporarily unavailable. Please try again later.";
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return "I couldn't generate a suitable response right now.";
            }
            var first = candidates[0];
            if (!first.TryGetProperty("content", out var contentElement)) return "I couldn't generate a suitable response right now.";
            if (!contentElement.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0) return "I couldn't generate a suitable response right now.";
            string? reply = null;
            try { reply = parts[0].GetProperty("text").GetString(); } catch { reply = parts[0].ToString(); }
            var finalReply = string.IsNullOrWhiteSpace(reply) ? "I couldn't generate a suitable response right now." : reply.Trim();
            return finalReply;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse model response");
            return "I couldn't parse the response from the AI model.";
        }
    }
}
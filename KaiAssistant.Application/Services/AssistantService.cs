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

    public async Task<string> AskQuestionAsync(string question, ConversationMessage[]? history = null, CancellationToken cancellationToken = default)
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

        // Gemini payload: contents array with role/parts structure
        var contentsArray = new JsonArray();

        // Add system prompt as a model message (system-equivalent)
        contentsArray.Add(new JsonObject
        {
            ["role"] = "model",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = systemPrompt })
        });

        // Add conversation history if provided (limit to last 10 messages to avoid token limits)
        int historyChars = 0;
        if (history != null && history.Length > 0)
        {
            var recentHistory = history.Skip(Math.Max(0, history.Length - 10));
            foreach (var msg in recentHistory)
            {
                if (string.IsNullOrWhiteSpace(msg.Content)) continue;
                historyChars += msg.Content.Length;

                // Normalize roles: Gemini accepts only 'user' and 'model'.
                string normalizedRole;
                if (string.Equals(msg.Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedRole = "user";
                }
                else if (string.Equals(msg.Role, "model", StringComparison.OrdinalIgnoreCase) || string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedRole = "model";
                }
                else
                {
                    // Unexpected role value - coerce to 'model' and log a warning
                    _logger.LogWarning("Unrecognized role '{Role}' in conversation history; coercing to 'model'.", msg.Role);
                    normalizedRole = "model";
                }

                contentsArray.Add(new JsonObject
                {
                    ["role"] = normalizedRole,
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = msg.Content })
                });
            }
        }

        // Simple payload-size estimate and guard. If payload seems too large, ask user to clear chat to continue.
        var estimatedSize = systemPrompt.Length + question.Length + historyChars + combinedResume.Length;
        var sizeThreshold = Math.Max(20000, maxChars * 2);
        if (estimatedSize > sizeThreshold)
        {
            return "Our conversation is getting long and may exceed server limits. Please clear chat history to start a fresh conversation (sorry, we're still poor xD).";
        }

        // Add resume/context as a model message (system-equivalent), then add the user's question as a user message
        if (!string.IsNullOrWhiteSpace(combinedResume))
        {
            contentsArray.Add(new JsonObject
            {
                ["role"] = "model",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = $"Resume context:\n{combinedResume}" })
            });
        }

        // Add current user question
        contentsArray.Add(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = question })
        });

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
            _logger.LogDebug("Gemini response body: {Body}", responseBody);
            using var doc = JsonDocument.Parse(responseBody);

            // Try to extract a text reply from multiple possible response shapes returned by Gemini
            string? TryExtractText(JsonElement el, int depth = 0)
            {
                if (depth > 10) return null;
                switch (el.ValueKind)
                {
                    case JsonValueKind.Object:
                        if (el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                            return t.GetString();
                        if (el.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
                            return ot.GetString();
                        if (el.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0)
                        {
                            foreach (var p in parts.EnumerateArray())
                            {
                                var r = TryExtractText(p, depth + 1);
                                if (!string.IsNullOrWhiteSpace(r)) return r;
                            }
                        }
                        if (el.TryGetProperty("content", out var content) )
                        {
                            var r = TryExtractText(content, depth + 1);
                            if (!string.IsNullOrWhiteSpace(r)) return r;
                        }
                        if (el.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var o in output.EnumerateArray())
                            {
                                var r = TryExtractText(o, depth + 1);
                                if (!string.IsNullOrWhiteSpace(r)) return r;
                            }
                        }
                        if (el.TryGetProperty("candidates", out var cands) && cands.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var c in cands.EnumerateArray())
                            {
                                var r = TryExtractText(c, depth + 1);
                                if (!string.IsNullOrWhiteSpace(r)) return r;
                            }
                        }
                        break;
                    case JsonValueKind.Array:
                        foreach (var item in el.EnumerateArray())
                        {
                            var r = TryExtractText(item, depth + 1);
                            if (!string.IsNullOrWhiteSpace(r)) return r;
                        }
                        break;
                }
                return null;
            }

            var root = doc.RootElement;
            var replyText = TryExtractText(root);
            if (string.IsNullOrWhiteSpace(replyText))
            {
                _logger.LogWarning("Could not extract reply text from model response");
                return "I couldn't generate a suitable response right now.";
            }
            return replyText.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse model response");
            return "I couldn't parse the response from the AI model.";
        }
    }
}
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Domain.Entities;
using Microsoft.Extensions.Options;

namespace KaiAssistant.Application.Services;

public class AiPromptBuilder : IAiPromptBuilder
{
    private readonly IOptions<GeminiSettings> _options;

    public AiPromptBuilder(IOptions<GeminiSettings> options)
    {
        _options = options;
    }

    public string BuildSystemPrompt()
    {
        var basePrompt = string.IsNullOrWhiteSpace(_options.Value.SystemPrompt)
            ? "You are Kai Taing's professional AI assistant. Represent Kai professionally and help visitors learn about his background, skills, and experience."
            : _options.Value.SystemPrompt;

        var guardrails = @"
        STYLE RULES:
        - Keep a friendly, concise, and conversational tone.
        - You have full access to the conversation history provided in this request.
        - Answer using the provided resume context and the full conversation history.
        - Use information from previous messages in the conversation to provide personalized responses, including user names and details.
        - If information about the user (like their name) is mentioned in the conversation history, acknowledge and use it.
        - If info is missing from both resume and conversation history, say so politely.
        - Keep responses concise (2-4 short paragraphs max); use bullet points for lists.
        - Be direct and helpful.
        - Always remember and reference details from the ongoing conversation.";

        return basePrompt + "\n\n" + guardrails.Trim();
    }

    public List<object> BuildContents(string resumeContext, string question)
    {
        var contents = new List<object>();
        contents.Add(new
        {
            role = "user",
            parts = new[] { new { text = BuildSystemPrompt() } }
        });
        var userTurn = $"Resume context:\n{resumeContext}\n\nUser question: {question}";
        contents.Add(new
        {
            role = "user",
            parts = new[] { new { text = userTurn } }
        });
        return contents;
    }

    public object BuildGenerationConfig(string question)
    {
        var temperature = question.Length < 80 ? 0.3 : 0.45;
        var topK = 20;
        var topP = 0.85;
        var maxOutputTokens = question.Length < 100 ? 512 : 768;
        var candidateCount = 1;

        var tempEnv = Environment.GetEnvironmentVariable("GEMINI_TEMPERATURE")
            ?? _options.Value.Temperature?.ToString();
        if (!string.IsNullOrWhiteSpace(tempEnv) && double.TryParse(tempEnv, out var tempParsed))
            temperature = Math.Clamp(tempParsed, 0.0, 2.0);

        var topKEnv = Environment.GetEnvironmentVariable("GEMINI_TOPK")
            ?? _options.Value.TopK?.ToString();
        if (!string.IsNullOrWhiteSpace(topKEnv) && int.TryParse(topKEnv, out var topKParsed) && topKParsed >= 0)
            topK = topKParsed;

        var topPEnv = Environment.GetEnvironmentVariable("GEMINI_TOPP")
            ?? _options.Value.TopP?.ToString();
        if (!string.IsNullOrWhiteSpace(topPEnv) && double.TryParse(topPEnv, out var topPParsed))
            topP = Math.Clamp(topPParsed, 0.0, 1.0);

        var maxTokensEnv = Environment.GetEnvironmentVariable("GEMINI_MAX_OUTPUT_TOKENS")
            ?? _options.Value.MaxOutputTokens?.ToString();
        if (!string.IsNullOrWhiteSpace(maxTokensEnv) && int.TryParse(maxTokensEnv, out var maxTokensParsed) && maxTokensParsed > 0)
            maxOutputTokens = Math.Min(maxTokensParsed, 2048);

        var candidateEnv = Environment.GetEnvironmentVariable("GEMINI_CANDIDATE_COUNT")
            ?? _options.Value.CandidateCount?.ToString();
        if (!string.IsNullOrWhiteSpace(candidateEnv) && int.TryParse(candidateEnv, out var candidateParsed) && candidateParsed > 0)
            candidateCount = Math.Min(candidateParsed, 5);

        return new
        {
            temperature,
            topK,
            topP,
            maxOutputTokens,
            candidateCount
        };
    }
}

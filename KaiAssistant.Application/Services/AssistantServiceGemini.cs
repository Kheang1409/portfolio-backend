using KaiAssistant.Domain.Entities;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text;
using System.Net;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace KaiAssistant.Application.Services
{
    public class AssistantServiceGemini : IAssistantService
    {
        private readonly List<ResumeChunk> _resumeChunks = new();
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _modelName;
        private readonly string _systemPrompt;
        private const int ChunkSize = 500;

        public AssistantServiceGemini(IOptions<GeminiSettings> options, HttpClient httpClient)
        {
            var settings = options.Value;
            _apiKey = settings.ApiKey ?? throw new ArgumentException("Gemini API key is missing.");
            _endpoint = settings.Endpoint ?? throw new ArgumentException("Gemini endpoint is missing.");
            _systemPrompt = settings.SystemPrompt;
            _modelName = settings.ModelName;
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.Remove("x-goog-api-key");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-goog-api-key", _apiKey);
        }

        public void LoadResume(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Resume not found.", filePath);

            var resume = JsonSerializer.Deserialize<Resume>(File.ReadAllText(filePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _resumeChunks.Clear();
            if (resume == null)
                throw new Exception("Failed to deserialize resume.");

            AddChunk("Summary", resume.Summary ?? string.Empty);
            AddChunk("Skills", string.Join(", ", resume.Skills?.LanguagesFrameworks ?? new List<string>()));
            if (resume.Experience != null)
            {
                foreach (var job in resume.Experience)
                    AddChunk($"Experience at {job.Company}", string.Join("\n", job.Highlights ?? new List<string>()));
            }
            if (resume.Projects != null)
            {
                foreach (var project in resume.Projects)
                    AddChunk($"Project: {project.Name}", project.Description ?? string.Empty);
            }
        }

        public async Task<string> AskQuestionAsync(string question, string? userId = null)
        {
            if (_resumeChunks.Count == 0)
                throw new InvalidOperationException("Resume is not loaded. Please load the resume before asking questions.");

            var relevantChunks = FindRelevantChunks(question);
            var combinedResume = string.Join("\n\n", relevantChunks.Select(c => $"{c.Label}: {c.Content}"));

            var contents = BuildContents(combinedResume, question);

            var payload = new
            {
                systemInstruction = new
                {
                    role = "system",
                    parts = new[]
                    {
                        new { text = BuildSystemPrompt() }
                    }
                },
                contents,
                generationConfig = new
                {
                    temperature = 0.4,
                    topK = 40,
                    topP = 0.9,
                    maxOutputTokens = 1024,
                    candidateCount = 1
                },
                safetySettings = new[]
                {
                    new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                    new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                    new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                    new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" }
                }
            };

            var json = JsonSerializer.Serialize(payload);

            // Multi-model retry with exponential backoff + jitter
            var modelsToTry = new List<string> { _modelName, "gemini-1.5-flash-8b:generateContent", "gemini-1.5-flash:generateContent" };
            HttpResponseMessage? response = null;
            string? usedModel = null;
            foreach (var model in modelsToTry.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var url = $"{_endpoint}{model}?key={_apiKey}";
                    response = await _httpClient.PostAsync(url, content);
                    if (response.IsSuccessStatusCode)
                    {
                        usedModel = model;
                        break;
                    }

                    if (response.StatusCode == HttpStatusCode.ServiceUnavailable || (int)response.StatusCode == 429)
                    {
                        var baseDelay = (int)Math.Min(3000, 250 * Math.Pow(2, attempt - 1));
                        var jitter = Random.Shared.Next(0, 200);
                        var waitMs = baseDelay + jitter;
                        Console.WriteLine($"[Gemini] Transient error {response.StatusCode} on {model}. Attempt {attempt}/5. Retrying in {waitMs}ms");
                        await Task.Delay(waitMs);
                        continue;
                    }

                    var bodyErr = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Gemini] API error ({response.StatusCode}) calling {url}: {bodyErr}");
                    break; // non-retryable for this model; try next model
                }
                if (response != null && response.IsSuccessStatusCode)
                    break; // success
            }

            if (response == null)
            {
                Console.WriteLine("[Gemini] No HTTP response received from any model");
                return LocalFallbackOrError(question);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[Gemini] API error after retries across models: {body}");
                return LocalFallbackOrError(question);
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(usedModel))
            {
                Console.WriteLine($"[Gemini] Response from model: {usedModel}");
            }
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                // Try to surface model-level errors if any
                if (doc.RootElement.TryGetProperty("promptFeedback", out var feedback))
                {
                    Console.WriteLine($"[Gemini] promptFeedback: {feedback}");
                }
                Console.WriteLine($"[Gemini] Unexpected response: {responseBody}");
                return ErrorReply();
            }
            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var contentElement))
            {
                Console.WriteLine($"[Gemini] Missing content in candidate: {firstCandidate}");
                return ErrorReply();
            }
            if (!contentElement.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
            {
                Console.WriteLine($"[Gemini] Missing parts in candidate content: {contentElement}");
                return ErrorReply();
            }
            string? reply = null;
            try
            {
                // Standard text part
                reply = parts[0].GetProperty("text").GetString();
            }
            catch
            {
                // Fallback: sometimes parts may include other payload types; try to stringify
                reply = parts[0].ToString();
            }
            var finalReply = string.IsNullOrWhiteSpace(reply) ? ErrorReply() : reply.Trim();
            return finalReply;
        }



        public async Task<string> AskQuestionAsync(string question)
        {
            return await AskQuestionAsync(question, "default");
        }

        private string BuildSystemPrompt()
        {
            var basePrompt = string.IsNullOrWhiteSpace(_systemPrompt)
                ? "You are Kai Taing's professional AI assistant. Represent Kai professionally and help visitors learn about his background, skills, and experience."
                : _systemPrompt;

            var guardrails = @"
STYLE RULES:
- Keep a friendly, concise, and conversational tone.
- Answer ONLY using the provided resume context; if info is missing, say so politely.
- Keep responses concise (2-4 short paragraphs max); use bullet points for lists.
- Be direct and helpful.";

            return basePrompt + "\n\n" + guardrails.Trim();
        }

        private List<object> BuildContents(string resumeContext, string question)
        {
            var contents = new List<object>();
            var userTurn = $"Here's some context from my resume:\n{resumeContext}\n\n{question}";
            contents.Add(new
            {
                role = "user",
                parts = new[] { new { text = userTurn } }
            });
            return contents;
        }

        private string ErrorReply() =>
            "I apologize, but I'm having trouble generating a response right now. Please try rephrasing your question or use the contact form to reach out directly.";

        private string LocalFallbackOrError(string userInput)
        {
            return "I'm temporarily overloaded. I can cover Kai's background, skills, and notable projects. Tell me which area you'd like, or try again in a moment.";
        }

        private List<ResumeChunk> FindRelevantChunks(string question)
        {
            var questionLower = question.ToLowerInvariant();
            var keywordMappings = new Dictionary<string, string[]>
            {
                { "experience", new[] { "experience", "work", "job", "role", "company", "worked", "career" } },
                { "skills", new[] { "skill", "technology", "tech", "know", "programming", "language", "framework", "tool" } },
                { "education", new[] { "education", "degree", "university", "school", "study", "studied", "graduate" } },
                { "project", new[] { "project", "built", "created", "developed", "portfolio" } },
                { "summary", new[] { "about", "who", "background", "introduction", "profile" } }
            };

            var scoredChunks = _resumeChunks.Select(chunk =>
            {
                int score = 0;
                var chunkLower = (chunk.Label + " " + chunk.Content).ToLowerInvariant();
                foreach (var mapping in keywordMappings)
                {
                    if (chunk.Label.ToLowerInvariant().Contains(mapping.Key))
                    {
                        if (mapping.Value.Any(keyword => questionLower.Contains(keyword)))
                            score += 10;
                    }
                }
                var questionWords = questionLower
                    .Split(new[] { ' ', '?', '!', '.' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 3)
                    .ToArray();
                foreach (var word in questionWords)
                {
                    if (chunkLower.Contains(word))
                        score += 2;
                }
                return new { Chunk = chunk, Score = score };
            })
            .OrderByDescending(x => x.Score)
            .ToList();

            var topChunks = scoredChunks
                .Where(x => x.Score > 0 || x.Chunk.Label.Contains("Summary"))
                .Take(8)
                .Select(x => x.Chunk)
                .ToList();
            return topChunks.Any() ? topChunks : _resumeChunks.Take(10).ToList();
        }

        private void AddChunk(string label, string content, string source = "")
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            for (int i = 0; i < content.Length; i += ChunkSize)
            {
                _resumeChunks.Add(new ResumeChunk
                {
                    Label = label,
                    Source = source,
                    Content = content.Substring(i, Math.Min(ChunkSize, content.Length - i))
                });
            }
        }
    }
}
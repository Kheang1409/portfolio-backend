using KaiAssistant.Domain.Entities;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Net;

namespace KaiAssistant.Application.Services
{
    public class AssistantServiceGemini : IAssistantService
    {
        private readonly List<ResumeChunk> _resumeChunks = new();
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _modelName;
        private readonly string _systemPrompt;
        private readonly HttpClient _httpClient;

        private const int ChunkSize = 500;

        public AssistantServiceGemini(IOptions<GeminiSettings> options, HttpClient httpClient)
        {
            var settings = options.Value;

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
                throw new ArgumentException("Gemini API key is missing.");

            if (string.IsNullOrWhiteSpace(settings.Endpoint))
                throw new ArgumentException("Gemini endpoint is missing.");

            _apiKey = settings.ApiKey;
            _endpoint = settings.Endpoint;
            _systemPrompt = settings.SystemPrompt;
            _modelName = settings.ModelName;
            _httpClient = httpClient;

            // Gemini requires x-goog-api-key
            _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);
        }

        public void LoadResume(string filePath)
        {
            Console.WriteLine($"Loading resume from: {filePath}");
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Resume not found.", filePath);

            var jsonText = File.ReadAllText(filePath);
            var resume = JsonSerializer.Deserialize<Resume>(jsonText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _resumeChunks.Clear();

            if (resume is null)
                throw new Exception("Failed to deserialize resume.");

            AddChunk("Summary", resume.Summary);
            AddChunk("Skills", string.Join(", ", resume.Skills.LanguagesFrameworks));

            foreach (var job in resume.Experience)
                AddChunk($"Experience at {job.Company}", string.Join("\n", job.Highlights));

            foreach (var project in resume.Projects)
                AddChunk($"Project: {project.Name}", project.Description);
        }

        public async Task<string> AskQuestionAsync(string question)
        {
            if (_resumeChunks.Count == 0)
                throw new InvalidOperationException("Resume is not loaded. Please load the resume before asking questions.");

            string combinedResume = string.Join("\n\n", _resumeChunks.Select(c => $"{c.Label}: {c.Content}"));

            // Merge system prompt with resume
            string prompt = $"{_systemPrompt}\n\nResume context:\n{combinedResume}\n\nUser Question: {question}";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_endpoint}{_modelName}", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini API error ({response.StatusCode}): {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseBody);

            // Correct parsing for Gemini response
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return "No response from model.";

            var firstCandidate = candidates[0];

            if (!firstCandidate.TryGetProperty("content", out var contentElement))
                return "No response from model.";

            if (!contentElement.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
                return "No response from model.";

            var reply = parts[0].GetProperty("text").GetString();

            return reply ?? "No response from model.";
        }

        private void AddChunk(string label, string content, string source = "")
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            for (int i = 0; i < content.Length; i += ChunkSize)
            {
                var chunk = content.Substring(i, Math.Min(ChunkSize, content.Length - i));
                _resumeChunks.Add(new ResumeChunk
                {
                    Label = label,
                    Source = source,
                    Content = chunk
                });
            }
        }
    }
}
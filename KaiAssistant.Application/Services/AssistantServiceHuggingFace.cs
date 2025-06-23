using KaiAssistant.Domain.Entities;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Net;

namespace KaiAssistant.Application.Services
{
    public class AssistantServiceHuggingFace : IAssistantService
    {
        private readonly List<ResumeChunk> _resumeChunks = new();
        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly HttpClient _httpClient;

        private const int ChunkSize = 500;

        public AssistantServiceHuggingFace(IOptions<HuggingFaceSettings> options, HttpClient httpClient)
        {
            var settings = options.Value;

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
                throw new ArgumentException("HuggingFace API key is missing.");

            if (string.IsNullOrWhiteSpace(settings.ModelName))
                throw new ArgumentException("HuggingFace model name is missing.");

            _apiKey = settings.ApiKey;
            _modelName = settings.ModelName;
            _httpClient = httpClient;

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public void LoadResume(string filePath)
        {
            Console.WriteLine($"Loading resume from: {filePath}");
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Resume not found.", filePath);

            string resumeText = File.ReadAllText(filePath);
            _resumeChunks.Clear();

            for (int i = 0; i < resumeText.Length; i += ChunkSize)
            {
                string chunk = resumeText.Substring(i, Math.Min(ChunkSize, resumeText.Length - i));
                _resumeChunks.Add(new ResumeChunk { Content = chunk });
            }
        }

        public async Task<string> AskQuestionAsync(string question)
        {
            if (_resumeChunks.Count == 0)
                throw new InvalidOperationException("Resume is not loaded. Please load the resume before asking questions.");

            // Concatenate all resume chunks into one system message
            string combinedResume = string.Join("\n\n", _resumeChunks.Select(c => c.Content));

            var messages = new[]
            {
                new { role = "system", content = $"You are a helpful assistant. Use the following resume information to answer questions:\n{combinedResume}" },
                new { role = "user", content = question }
            };

            var requestPayload = new
            {
                model = _modelName,
                messages = messages,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var endpoint = "https://router.huggingface.co/novita/v3/openai/chat/completions";

            var response = await _httpClient.PostAsync(endpoint, content);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new Exception($"HuggingFace API error: NotFound Not Found - model '{_modelName}' does not exist or is inaccessible.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"HuggingFace API error ({response.StatusCode}): {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseBody);
            var reply = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();

            return reply ?? "No response from model.";
        }
    }
}
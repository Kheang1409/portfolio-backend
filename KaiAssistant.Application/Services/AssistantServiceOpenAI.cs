using KaiAssistant.Domain.Entities;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Net;

namespace KaiAssistant.Application.Services
{
    public class AssistantServiceOpenAI : IAssistantService
    {
        private readonly List<ResumeChunk> _resumeChunks = new();
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;

        private const int MaxTokensEstimate = 3000;
        private const int ChunkSize = 500;

        public AssistantServiceOpenAI(IOptions<OpenAiSettings> options, HttpClient httpClient)
        {
            _apiKey = options.Value.ApiKey ?? throw new ArgumentException("API key is missing.");
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public void LoadResume(string filePath)
        {
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

        private List<ResumeChunk> FindRelevantChunks(string question)
        {
            var queryWords = question
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.ToLowerInvariant());

            return _resumeChunks
                .Select(chunk => new
                {
                    Chunk = chunk,
                    Score = queryWords.Count(w => chunk.Content.ToLowerInvariant().Contains(w))
                })
                .OrderByDescending(x => x.Score)
                .Take(5) // Select more initially, truncate later based on token size
                .Select(x => x.Chunk)
                .ToList();
        }

        private string BuildContext(List<ResumeChunk> chunks)
        {
            var sb = new StringBuilder();
            int tokenCount = 0;

            foreach (var chunk in chunks)
            {
                int estimatedTokens = chunk.Content.Length / 4; // Rough token estimate
                if (tokenCount + estimatedTokens > MaxTokensEstimate)
                    break;

                sb.AppendLine(chunk.Content);
                sb.AppendLine("\n---\n");
                tokenCount += estimatedTokens;
            }

            return sb.ToString();
        }

        public async Task<string> AskQuestionAsync(string question)
        {
            var contextChunks = FindRelevantChunks(question);
            string context = BuildContext(contextChunks);

            var messages = new[]
            {
                new Message("system", "You are Kai Taing's personal assistant. Only answer using the provided context."),
                new Message("user", $"Context:\n{context}\n\nUser question:\n{question}")
            };

            var chatRequest = new ChatRequest(messages, "gpt-3.5-turbo");
            var json = JsonSerializer.Serialize(chatRequest);

            return await SendWithRetryAsync(json);
        }

        private async Task<string> SendWithRetryAsync(string json)
        {
            const int maxRetries = 8;
            int retryCount = 0;
            int delay = 1500;

            while (retryCount <= maxRetries)
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    Console.WriteLine($"[Retry {retryCount}] 429 Too Many Requests.");

                    if (retryCount > maxRetries)
                        throw new Exception("Too many requests. Retry limit exceeded.");

                    if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
                    {
                        if (double.TryParse(retryAfterValues.FirstOrDefault(), out double retryAfterSeconds))
                        {
                            await Task.Delay(TimeSpan.FromSeconds(retryAfterSeconds));
                        }
                        else
                        {
                            await Task.Delay(delay);
                            delay *= 2; // exponential backoff
                        }
                    }
                    else
                    {
                        await Task.Delay(delay);
                        delay *= 2;
                    }

                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"OpenAI API error ({response.StatusCode}): {error}");
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);

                var choice = doc.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message").GetProperty("content").GetString();

                return message ?? "I'm not sure how to answer that.";
            }

            throw new Exception("Unexpected error: retry logic fell through.");
        }
    }

    public class Message
    {
        public string Role { get; set; }
        public string Content { get; set; }

        public Message(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    public class ChatRequest
    {
        public Message[] Messages { get; set; }
        public string Model { get; set; }

        public ChatRequest(Message[] messages, string model)
        {
            Messages = messages;
            Model = model;
        }
    }
}
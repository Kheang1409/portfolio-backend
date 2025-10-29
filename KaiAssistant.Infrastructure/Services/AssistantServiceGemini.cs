using KaiAssistant.Domain.Entities;
using KaiAssistant.Domain.Entities.Resumes;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text;
using System.Net;
using KaiAssistant.Domain.Interfaces.Repositories;
using System.Diagnostics.Metrics;
using System.Diagnostics;

namespace KaiAssistant.Infrastructure.Services;

public class AssistantServiceGemini : IAssistantService
{
    private ResumeChunk[] _resumeChunks = Array.Empty<ResumeChunk>();
    private readonly HttpClient _httpClient;
    private readonly IResumeRepository _resumeRepository;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly string _modelName;
    private readonly string _systemPrompt;
    private readonly ILogger<AssistantServiceGemini> _logger;
    private readonly SemaphoreSlim _throttle;
    private static readonly Meter _meter = new("KaiAssistant.AssistantService", "1.0");
    private static readonly Counter<long> _requestsCounter = _meter.CreateCounter<long>("assistant.requests.total");
    private static readonly Histogram<double> _latencyHistogram = _meter.CreateHistogram<double>("assistant.request.latency.ms");
    private readonly ObservableGauge<long>? _inFlightObservable;
    private long _inFlightCount = 0;
    private const int ChunkSize = 500;

    public AssistantServiceGemini(IOptions<GeminiSettings> options, HttpClient httpClient, IResumeRepository resumeRepository, ILogger<AssistantServiceGemini> logger)
    {
        var settings = options.Value;
        _apiKey = settings.ApiKey;
        _endpoint = settings.Endpoint;
        _systemPrompt = settings.SystemPrompt;
        _modelName = settings.ModelName;
        _httpClient = httpClient;
        _resumeRepository = resumeRepository;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        int maxConcurrent = 4;
        var envMax = Environment.GetEnvironmentVariable("GEMINI_MAX_CONCURRENT");
        if (!string.IsNullOrWhiteSpace(envMax) && int.TryParse(envMax, out var parsed) && parsed > 0)
            maxConcurrent = parsed;

        _throttle = new SemaphoreSlim(maxConcurrent);

        _inFlightObservable = _meter.CreateObservableGauge<long>("assistant.requests.inflight", () => new[] { new Measurement<long>(Interlocked.Read(ref _inFlightCount)) });
    }

    private object BuildGenerationConfig(string question)
    {
        double temperature = question.Length < 80 ? 0.3 : 0.45;
        int topK = 20;
        double topP = 0.85;
        int maxOutputTokens = question.Length < 100 ? 512 : 768;
        int candidateCount = 1;

        var tEnv = Environment.GetEnvironmentVariable("GEMINI_TEMPERATURE");
        if (!string.IsNullOrWhiteSpace(tEnv) && double.TryParse(tEnv, out var tParsed))
            temperature = Math.Clamp(tParsed, 0.0, 2.0);

        var kEnv = Environment.GetEnvironmentVariable("GEMINI_TOPK");
        if (!string.IsNullOrWhiteSpace(kEnv) && int.TryParse(kEnv, out var kParsed) && kParsed >= 0)
            topK = kParsed;

        var pEnv = Environment.GetEnvironmentVariable("GEMINI_TOPP");
        if (!string.IsNullOrWhiteSpace(pEnv) && double.TryParse(pEnv, out var pParsed))
            topP = Math.Clamp(pParsed, 0.0, 1.0);

        var maxEnv = Environment.GetEnvironmentVariable("GEMINI_MAX_OUTPUT_TOKENS");
        if (!string.IsNullOrWhiteSpace(maxEnv) && int.TryParse(maxEnv, out var maxParsed) && maxParsed > 0)
            maxOutputTokens = Math.Min(maxParsed, 2048);

        var candEnv = Environment.GetEnvironmentVariable("GEMINI_CANDIDATE_COUNT");
        if (!string.IsNullOrWhiteSpace(candEnv) && int.TryParse(candEnv, out var candParsed) && candParsed > 0)
            candidateCount = Math.Min(candParsed, 5);

        return new
        {
            temperature,
            topK,
            topP,
            maxOutputTokens,
            candidateCount
        };
    }

    public void LoadResume(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Resume not found.", filePath);
        var content = File.ReadAllText(filePath);
        var resume = JsonSerializer.Deserialize<Resume>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (resume == null)
            throw new Exception("Failed to deserialize resume.");

        var newList = new List<ResumeChunk>();
        AddChunkToList(newList, "Summary", resume.Summary ?? string.Empty);
        AddChunkToList(newList, "Skills", string.Join(", ", resume.Skills ?? new List<string>()));
        if (resume.Experiences != null)
        {
            foreach (var job in resume.Experiences)
                AddChunkToList(newList, $"Experience at {job.Company}", string.Join("\n", job.BulletPoints ?? new List<string>()));
        }
        if (resume.Projects != null)
        {
            foreach (var project in resume.Projects)
                AddChunkToList(newList, $"Project: {project.Name}", project.Description ?? string.Empty);
        }

        Interlocked.Exchange(ref _resumeChunks, newList.ToArray());
    }

    public async Task LoadResumeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Resume not found.", filePath);

        var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var resume = JsonSerializer.Deserialize<Resume>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (resume == null)
            throw new Exception("Failed to deserialize resume.");

        var newList = new List<ResumeChunk>();
        AddChunkToList(newList, "Summary", resume.Summary ?? string.Empty);
        AddChunkToList(newList, "Skills", string.Join(", ", resume.Skills ?? new List<string>()));
        if (resume.Experiences != null)
        {
            foreach (var job in resume.Experiences)
                AddChunkToList(newList, $"Experience at {job.Company}", string.Join("\n", job.BulletPoints ?? new List<string>()));
        }
        if (resume.Projects != null)
        {
            foreach (var project in resume.Projects)
                AddChunkToList(newList, $"Project: {project.Name}", project.Description ?? string.Empty);
        }

        Interlocked.Exchange(ref _resumeChunks, newList.ToArray());
    }

    public void LoadResumeFromDatabase()
    {
        LoadResumeFromDatabaseAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task LoadResumeFromDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var resume = await _resumeRepository.GetLatestAsync().ConfigureAwait(false);
        if (resume == null)
        {
            _logger.LogInformation("No resume found in database. Starting without resume.");
            return;
        }

        var newList = new List<ResumeChunk>();
        AddChunkToList(newList, "Summary", resume.Summary ?? string.Empty);
        AddChunkToList(newList, "Skills", string.Join(", ", resume.Skills ?? new List<string>()));
        if (resume.Experiences != null)
        {
            foreach (var job in resume.Experiences)
                AddChunkToList(newList, $"Experience at {job.Company}", string.Join("\n", job.BulletPoints ?? new List<string>()));
        }
        if (resume.Projects != null)
        {
            foreach (var project in resume.Projects)
                AddChunkToList(newList, $"Project: {project.Name}", project.Description ?? string.Empty);
        }

        Interlocked.Exchange(ref _resumeChunks, newList.ToArray());
    }

    public async Task<string> AskQuestionAsync(string question, CancellationToken cancellationToken = default)
    {
        var snapshot = _resumeChunks;
        if (snapshot.Length == 0)
            throw new InvalidOperationException("Resume is not loaded. Please load the resume before asking questions.");

        var relevantChunks = FindRelevantChunks(question, snapshot);
        var combinedResume = string.Join("\n\n", relevantChunks.Select(c => $"{c.Label}: {c.Content}"));

        int maxChars = 10000;
        var envMax = Environment.GetEnvironmentVariable("GEMINI_PROMPT_MAX_CHARS");
        if (!string.IsNullOrWhiteSpace(envMax) && int.TryParse(envMax, out var parsedMax) && parsedMax > 0)
            maxChars = parsedMax;
        if (combinedResume.Length > maxChars)
        {
            combinedResume = combinedResume.Substring(0, maxChars) + "\n\n...[truncated resume context]";
        }

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
            generationConfig = BuildGenerationConfig(question),
            safetySettings = new[]
            {
                new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var modelsToTry = new List<string> { _modelName, "gemini-1.5-flash-8b:generateContent", "gemini-1.5-flash:generateContent" };
        var (responseBody, usedModel) = await SendRequestWithRetriesAsync(json, modelsToTry.Distinct(StringComparer.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            _logger.LogWarning("No successful response received from Gemini models");
            return LocalFallbackOrError(question);
        }

        if (!string.IsNullOrWhiteSpace(usedModel))
            _logger.LogInformation("Response from model: {Model}", usedModel);

        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return ErrorReply();
        }
        var firstCandidate = candidates[0];
        if (!firstCandidate.TryGetProperty("content", out var contentElement))
        {
            return ErrorReply();
        }
        if (!contentElement.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
        {
            return ErrorReply();
        }
        string? reply = null;
        try
        {
            reply = parts[0].GetProperty("text").GetString();
        }
        catch
        {
            reply = parts[0].ToString();
        }
        var finalReply = string.IsNullOrWhiteSpace(reply) ? ErrorReply() : reply.Trim();
        return finalReply;
    }

    public Task<string> AskQuestionAsync(string question)
    {
        return AskQuestionAsync(question, CancellationToken.None);
    }

    private async Task<(string? Body, string? UsedModel)> SendRequestWithRetriesAsync(string json, IEnumerable<string> models, CancellationToken cancellationToken)
    {
        foreach (var model in models)
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var url = $"{_endpoint}{model}";
                    HttpResponseMessage? response = null;
                    var sw = Stopwatch.StartNew();
                    Interlocked.Increment(ref _inFlightCount);
                    _requestsCounter.Add(1);
                try
                {
                        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                        request.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);
                        response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        sw.Stop();
                        _latencyHistogram.Record(sw.Elapsed.TotalMilliseconds);
                        return (body, model);
                    }

                    if (response.StatusCode == HttpStatusCode.ServiceUnavailable || (int)response.StatusCode == 429)
                    {
                            var baseDelay = (int)Math.Min(3000, 250 * Math.Pow(2, attempt - 1));
                            var jitter = Random.Shared.Next(0, 200);
                            var waitMs = baseDelay + jitter;
                            _logger.LogWarning("Transient error {Status} from {Model} (attempt {Attempt}/5). Retrying in {Delay}ms.", response.StatusCode, model, attempt, waitMs);
                            await Task.Delay(waitMs, cancellationToken).ConfigureAwait(false);
                            continue;
                    }

                    var bodyErr = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogError("API error ({Status}) calling {Url}: {Body}", response.StatusCode, url, bodyErr);
                    sw.Stop();
                    _latencyHistogram.Record(sw.Elapsed.TotalMilliseconds);
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Request cancelled to model {Model}", model);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception while calling {Url}", url);
                    sw.Stop();
                    _latencyHistogram.Record(sw.Elapsed.TotalMilliseconds);
                    break;
                }
                    finally
                    {
                        response?.Dispose();
                    }
                }
                finally
                {
                    _throttle.Release();
                    Interlocked.Decrement(ref _inFlightCount);
                }
            }
        }

        return (null, null);
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

    private List<ResumeChunk> FindRelevantChunks(string question, ResumeChunk[] snapshot)
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

        var scoredChunks = snapshot.Select(chunk =>
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

        var maxChunks = 8;
        var envTop = Environment.GetEnvironmentVariable("GEMINI_TOP_CHUNKS");
        if (!string.IsNullOrWhiteSpace(envTop) && int.TryParse(envTop, out var parsedTop) && parsedTop > 0)
            maxChunks = parsedTop;

        var topChunks = scoredChunks
            .Where(x => x.Score > 0 || x.Chunk.Label.Contains("Summary"))
            .Take(maxChunks)
            .Select(x => x.Chunk)
            .ToList();
        return topChunks.Any() ? topChunks : snapshot.Take(maxChunks).ToList();
    }

    private void AddChunkToList(List<ResumeChunk> list, string label, string content, string source = "")
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        for (int i = 0; i < content.Length; i += ChunkSize)
        {
            list.Add(new ResumeChunk
            {
                Label = label,
                Source = source,
                Content = content.Substring(i, Math.Min(ChunkSize, content.Length - i))
            });
        }
    }
}

using Microsoft.Extensions.Configuration;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using KaiAssistant.Domain.Entities;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using System.Net;
using KaiAssistant.Application.Services;

namespace KaiAssistant.Infrastructure.Extensions;

public static class AssistantServiceCollectionExtensions
{
    public static IServiceCollection AddGeminiAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        var geminiSettings = configuration.GetSection("GeminiSettings");

        string apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                  ?? geminiSettings["ApiKey"]
                  ?? throw new ArgumentException("Gemini setting 'ApiKey' is missing or empty.");

        List<string> modelNames = new();
        var envModelNames = Environment.GetEnvironmentVariable("GEMINI_MODEL_NAMES");
        if (!string.IsNullOrWhiteSpace(envModelNames))
        {
            modelNames = envModelNames.Split(';', ',').Select(m => m.Trim()).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        }
        else if (geminiSettings.Exists())
        {
            var configModels = geminiSettings.GetSection("ModelNames").Get<string[]>();
            if (configModels != null && configModels.Length > 0)
                modelNames = configModels.ToList();
        }
        if (modelNames.Count == 0)
            throw new ArgumentException("Gemini setting 'ModelNames' is missing or empty.");

        string endpoint = Environment.GetEnvironmentVariable("GEMINI_ENDPOINT")
                          ?? geminiSettings["Endpoint"]
                          ?? throw new ArgumentException("Gemini setting 'Endpoint' is missing or empty.");

        string systemPrompt = Environment.GetEnvironmentVariable("GEMINI_SYSTEM_PROMPT")
                           ?? geminiSettings["SystemPrompt"]
                           ?? string.Empty;

        int promptMaxChars = 10000;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_PROMPT_MAX_CHARS")))
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("GEMINI_PROMPT_MAX_CHARS"), out var parsed) && parsed > 0)
                promptMaxChars = parsed;
        }
        else if (!string.IsNullOrWhiteSpace(geminiSettings["PromptMaxChars"]))
        {
            if (int.TryParse(geminiSettings["PromptMaxChars"], out var parsed) && parsed > 0)
                promptMaxChars = parsed;
        }

        bool includePersonalDetails = true;
        var envIncludePersonal = Environment.GetEnvironmentVariable("GEMINI_INCLUDE_PERSONAL_DETAILS");
        if (!string.IsNullOrWhiteSpace(envIncludePersonal) && bool.TryParse(envIncludePersonal, out var parsedInclude))
        {
            includePersonalDetails = parsedInclude;
        }
        else if (!string.IsNullOrWhiteSpace(geminiSettings["IncludePersonalDetails"]) && bool.TryParse(geminiSettings["IncludePersonalDetails"], out var parsedConfigInclude))
        {
            includePersonalDetails = parsedConfigInclude;
        }

        double? temperature = null;
        if (!string.IsNullOrWhiteSpace(geminiSettings["Temperature"]) && double.TryParse(geminiSettings["Temperature"], out var tempConfig))
            temperature = tempConfig;

        int? topK = null;
        if (!string.IsNullOrWhiteSpace(geminiSettings["TopK"]) && int.TryParse(geminiSettings["TopK"], out var topKConfig))
            topK = topKConfig;

        double? topP = null;
        if (!string.IsNullOrWhiteSpace(geminiSettings["TopP"]) && double.TryParse(geminiSettings["TopP"], out var topPConfig))
            topP = topPConfig;

        int? maxOutputTokens = null;
        if (!string.IsNullOrWhiteSpace(geminiSettings["MaxOutputTokens"]) && int.TryParse(geminiSettings["MaxOutputTokens"], out var maxTokensConfig))
            maxOutputTokens = maxTokensConfig;

        int? candidateCount = null;
        if (!string.IsNullOrWhiteSpace(geminiSettings["CandidateCount"]) && int.TryParse(geminiSettings["CandidateCount"], out var candidateConfig))
            candidateCount = candidateConfig;

        services.Configure<GeminiSettings>(opts =>
        {
            opts.ApiKey = apiKey;
            opts.ModelNames = modelNames;
            opts.Endpoint = endpoint;
            opts.SystemPrompt = systemPrompt;
            opts.PromptMaxChars = promptMaxChars;
            opts.IncludePersonalDetails = includePersonalDetails;
            opts.Temperature = temperature;
            opts.TopK = topK;
            opts.TopP = topP;
            opts.MaxOutputTokens = maxOutputTokens;
            opts.CandidateCount = candidateCount;
        });

        var registry = services.AddPolicyRegistry();

        registry.Add("gemini-retry", Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.ServiceUnavailable)
            .WaitAndRetryAsync(5, retryAttempt =>
                TimeSpan.FromMilliseconds(Math.Min(3000, 250 * Math.Pow(2, retryAttempt - 1))) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 200))));

        registry.Add("gemini-timeout", Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(15)));

        services.AddHttpClient("Gemini", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddPolicyHandlerFromRegistry("gemini-timeout")
        .AddPolicyHandler((sp, request) =>
        {
            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.ServiceUnavailable)
                .WaitAndRetryAsync(5, retryAttempt =>
                    TimeSpan.FromMilliseconds(Math.Min(3000, 250 * Math.Pow(2, retryAttempt - 1))) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 200)),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("GeminiHttpClient");
                        logger?.LogWarning("Retry {Retry} for {Request} due to {Reason}", retryCount, request.RequestUri, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                    });
        })
        .AddPolicyHandler((sp, _) =>
        {
            var resilience = sp.GetRequiredService<IResilienceStatusProvider>();
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("GeminiHttpClient");

            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.ServiceUnavailable)
                .CircuitBreakerAsync(
                    5,
                    TimeSpan.FromSeconds(30),
                    (result, breakDelay) =>
                    {
                        var reason = result.Exception?.Message ?? result.Result?.StatusCode.ToString() ?? "unknown";
                        resilience.RecordFailure("ai", reason);
                        resilience.RecordCircuitState("ai", "Open");
                        logger?.LogWarning("AI circuit opened for {BreakDelay} due to {Reason}", breakDelay, reason);
                    },
                    () =>
                    {
                        resilience.RecordCircuitState("ai", "Closed");
                        logger?.LogInformation("AI circuit reset.");
                    },
                    () => resilience.RecordCircuitState("ai", "HalfOpen"));
        });

        services.AddSingleton<IGeminiGateway, Gateways.GeminiGateway>();
        services.AddScoped<IAssistantService, AssistantService>();
        return services;
    }
}

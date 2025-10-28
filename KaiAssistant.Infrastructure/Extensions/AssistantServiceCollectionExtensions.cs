using KaiAssistant.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using KaiAssistant.Domain.Entities;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http;
using System.Net;
using System.Diagnostics;

namespace KaiAssistant.Infrastructure.Extensions;

public static class AssistantServiceCollectionExtensions
{
        public static IServiceCollection AddGeminiAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        var geminiSettings = configuration.GetSection("GeminiSettings");

        string apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                        ?? geminiSettings["ApiKey"]
                        ?? throw new ArgumentException("Gemini setting 'ApiKey' is missing or empty.");

        string modelName = Environment.GetEnvironmentVariable("GEMINI_MODEL_NAME")
                            ?? geminiSettings["ModelName"]
                            ?? throw new ArgumentException("Gemini setting 'ModelName' is missing or empty.");

        string endpoint = Environment.GetEnvironmentVariable("GEMINI_ENDPOINT")
                            ?? geminiSettings["Endpoint"]
                            ?? throw new ArgumentException("Gemini setting 'Endpoint' is missing or empty.");

        services.Configure<GeminiSettings>(opts =>
        {
            opts.ApiKey = apiKey;
            opts.ModelName = modelName;
            opts.Endpoint = endpoint;
        });

        services.AddHttpClient("Gemini", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddPolicyHandler((sp, request) =>
        {
            var jitterer = new Random();
            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.ServiceUnavailable || (int)msg.StatusCode == 429)
                .WaitAndRetryAsync(5, retryAttempt =>
                    TimeSpan.FromMilliseconds(Math.Min(3000, 250 * Math.Pow(2, retryAttempt - 1))) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 200)),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("GeminiHttpClient");
                        logger?.LogWarning("Retry {Retry} for {Request} due to {Reason}", retryCount, request.RequestUri, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                    });
        })
        .AddTransientHttpErrorPolicy(policyBuilder => policyBuilder.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

        services.AddSingleton<IAssistantService>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiSettings>>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("Gemini");
            var resumeRepo = sp.GetRequiredService<KaiAssistant.Domain.Interfaces.Repositories.IResumeRepository>();
            var logger = sp.GetRequiredService<ILogger<AssistantServiceGemini>>();
            return new AssistantServiceGemini(options, httpClient, resumeRepo, logger);
        });

        return services;
    }
}

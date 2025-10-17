using KaiAssistant.Application.Services;
using Microsoft.Extensions.Configuration;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using KaiAssistant.Domain.Entities;

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

        services.AddSingleton<IAssistantService>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiSettings>>();
            var httpClient = new HttpClient();
            return new AssistantServiceGemini(options, httpClient);
        });

        return services;
    }
}

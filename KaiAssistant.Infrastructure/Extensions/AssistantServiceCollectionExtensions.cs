using KaiAssistant.Application.Services;
using Microsoft.Extensions.Configuration;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using KaiAssistant.Domain.Entities;

namespace KaiAssistant.Infrastructure.Extensions;

public static class AssistantServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        var openAiSection = configuration.GetSection("OpenAiSettings");

        string apiKey = Environment.GetEnvironmentVariable("API_KEY")
                        ?? openAiSection["ApiKey"]
                        ?? throw new ArgumentException("OpenAI setting 'ApiKey' is missing or empty.");

        services.Configure<OpenAiSettings>(opts => opts.ApiKey = apiKey);
        services.AddHttpClient<IAssistantService, AssistantServiceOpenAI>();

        return services;
    }

    public static IServiceCollection AddHuggingFaceServices(this IServiceCollection services, IConfiguration configuration)
    {
        var huggingFaceSection = configuration.GetSection("HuggingFaceSettings");

        string apiKey = Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY")
                        ?? huggingFaceSection["ApiKey"]
                        ?? throw new ArgumentException("HuggingFace setting 'ApiKey' is missing or empty.");

        string modelName = Environment.GetEnvironmentVariable("HUGGINGFACE_MODEL_NAME")
                            ?? huggingFaceSection["ModelName"]
                            ?? throw new ArgumentException("HuggingFace setting 'ModelName' is missing or empty.");

        string endpoint = Environment.GetEnvironmentVariable("HUGGINGFACE_ENDPOINT")
                        ?? huggingFaceSection["Endpoint"]
                        ?? throw new ArgumentException("HuggingFace setting 'Endpoint' is missing or empty.");

        services.Configure<HuggingFaceSettings>(opts =>
        {
            opts.ApiKey = apiKey;
            opts.ModelName = modelName;
            opts.Endpoint = endpoint;
        });

        services.AddSingleton<IAssistantService>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HuggingFaceSettings>>();
            var httpClient = new HttpClient();
            return new AssistantServiceHuggingFace(options, httpClient);
        });

        return services;
    }

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

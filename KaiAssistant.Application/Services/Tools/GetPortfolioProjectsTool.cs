namespace KaiAssistant.Application.Services.Tools;
using global::KaiAssistant.Application.Interfaces;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net.Http;
public sealed class GetPortfolioProjectsTool : IAssistantTool
{
    private readonly ILogger<GetPortfolioProjectsTool> _logger;
    public string Name => "get_portfolio_projects";
    public string Description => 
        "Retrieves a list of portfolio projects with descriptions, technologies, and links. " +
        "Use this to answer questions about projects, technologies used, or achievements.";
    public string InputSchema => JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            filter = new
            {
                type = "string",
                description = "Optional filter: 'recent', 'featured', 'all' (default: 'featured')"
            },
            limit = new
            {
                type = "integer",
                description = "Max projects to return (default: 10)"
            }
        }
    });
    public GetPortfolioProjectsTool(ILogger<GetPortfolioProjectsTool> logger)
    {
        _logger = logger;
    }
    public bool ValidateArguments(Dictionary<string, object> args)
    {
        // filter is optional, limit is optional
        if (args.TryGetValue("limit", out var limitObj) && limitObj is not int)
            return false;
        return true;
    }
    public async Task<object> ExecuteAsync(Dictionary<string, object> args, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = args.TryGetValue("filter", out var f) ? f as string ?? "featured" : "featured";
            var limit = args.TryGetValue("limit", out var l) && l is int lim ? lim : 10;
            // Stub implementation returning sample data
            // In production, this would call GitHub API or internal service
            var projects = new[]
            {
                new
                {
                    name = "KaiAssistant",
                    description = "Production-grade .NET 10 AI backend with Gemini integration, MongoDB persistence, and advanced orchestration",
                    technologies = new[] { "C#", ".NET 10", "Gemini API", "MongoDB", "Redis", "Polly", "RabbitMQ" },
                    url = "https://github.com/you/kaiassistant",
                    featured = true
                },
                new
                {
                    name = "Semantic Search Engine",
                    description = "Vector-based document retrieval using embeddings and cosine similarity",
                    technologies = new[] { "Python", "NumPy", "Scikit-learn", "FastAPI" },
                    url = "https://github.com/you/semantic-search",
                    featured = true
                }
            };
            var filtered = projects
                .Where(p => filter == "all" || (filter == "featured" && p.featured))
                .Take(limit)
                .ToList();
            _logger.LogInformation("Portfolio projects retrieved: {Count} projects", filtered.Count);
            return new { projects = filtered, count = filtered.Count };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetPortfolioProjectsTool");
            throw;
        }
    }
}
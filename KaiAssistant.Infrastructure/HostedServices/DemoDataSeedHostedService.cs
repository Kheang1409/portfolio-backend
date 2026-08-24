using KaiAssistant.Application.Interfaces;
using KaiAssistant.Domain.Entities.AI;
using KaiAssistant.Domain.Entities.Experiences;
using KaiAssistant.Domain.Entities.Projects;
using KaiAssistant.Domain.Entities.Resumes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
namespace KaiAssistant.Infrastructure.HostedServices;
public sealed class DemoDataSeedHostedService : IHostedService
{
    private readonly IHostEnvironment _environment;
    private readonly IMongoDatabase _database;
    private readonly ILogger<DemoDataSeedHostedService> _logger;
    public DemoDataSeedHostedService(
        IHostEnvironment environment,
        IMongoDatabase database,
        ILogger<DemoDataSeedHostedService> logger)
    {
        _environment = environment;
        _database = database;
        _logger = logger;
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return;
        }
        await SeedResumeAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Development resume seed completed; knowledge corpus requires authenticated admin ingestion.");
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private async Task SeedResumeAsync(CancellationToken cancellationToken)
    {
        var resumes = _database.GetCollection<Resume>("resumes");
        var demoMarkerFilter = Builders<Resume>.Filter.Regex(
            x => x.Summary,
            new BsonRegularExpression("KaiAssistant demo", "i"));
        var existing = await resumes.CountDocumentsAsync(
            demoMarkerFilter,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existing > 0)
        {
            return;
        }
        var resume = new Resume
        {
            Summary = "KaiAssistant demo resume seeded for interview mode and local walkthroughs.",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        foreach (var skill in new[]
                 {
                     "ASP.NET Core",
                     "MongoDB",
                     "Redis",
                     "RabbitMQ",
                     "OpenTelemetry",
                     "Clean Architecture",
                     "AI Orchestration",
                     "RAG"
                 })
        {
            resume.AddSkill(skill);
        }
        resume.AddExperience(Experience.Create(
            "Senior Backend Engineer",
            "Portfolio Systems Lab",
            "Built resilient AI orchestration APIs with distributed rate limiting, outbox reliability, and full telemetry.",
            DateTime.UtcNow.AddYears(-2),
            null,
            [
                "Designed model fallback strategy with observability-first instrumentation.",
                "Implemented semantic cache and RAG pipeline for lower latency and improved relevance.",
                "Hardened operational surfaces with security controls and runtime feature flags."
            ]));
        resume.AddProject(Project.Create("KaiAssistant API", "Production-grade AI assistant backend with RAG, semantic caching, and streaming.", [".NET 10", "MongoDB", "Redis"]));
        resume.AddProject(Project.Create("Outbox Reliability Layer", "Exactly-once-ish integration publishing with idempotency store and dead-lettering.", ["RabbitMQ", "Redis", "Polly"]));
        resume.AddProject(Project.Create("Observability Fabric", "Trace and metric instrumentation for AI routing, cache efficiency, and outbox processing.", ["OpenTelemetry", "Serilog"]));
        await resumes.InsertOneAsync(resume, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

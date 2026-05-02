using Amazon.Lambda.AspNetCoreServer;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using KaiAssistant.API.Extensions;
using KaiAssistant.API.HealthChecks;
using KaiAssistant.API.Middleware;
using KaiAssistant.API.Options;
using KaiAssistant.API.Services;
using KaiAssistant.Application.Extensions;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Infrastructure.FeatureFlags;
using KaiAssistant.Infrastructure.EventBus;
using KaiAssistant.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System.Text.Json;
var builder = WebApplication.CreateBuilder(args);
ValidateCriticalConfiguration(builder.Configuration, builder.Environment);
builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "KaiAssistant.API",
        serviceInstanceId: Environment.GetEnvironmentVariable("INSTANCE_ID") ?? Environment.MachineName))
    .WithTracing(tb =>
    {
        tb.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("KaiAssistant.OutboxProcessor");
        if (builder.Environment.IsDevelopment())
        {
            tb.AddConsoleExporter();
        }
        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tb.AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(mb => mb
        .AddMeter("KaiAssistant.AssistantService")
        .AddMeter("KaiAssistant.Cache")
        .AddMeter("KaiAssistant.Resume")
        .AddMeter("KaiAssistant.Outbox")
        .AddMeter("KaiAssistant.AiGovernance")
        .AddMeter("KaiAssistant.OutboxProcessor")
        .AddMeter("KaiAssistant.ApiEndpoints")
        .AddMeter("KaiAssistant.RateLimiting")
        .AddMeter("KaiAssistant.AiModels")
        .AddMeter("KaiAssistant.Redis")
        .AddMeter("KaiAssistant.AiOrchestrator")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAiOrchestration(builder.Configuration);
builder.Services.AddConfiguredCors(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddOutputCache();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.AddOptions<RateLimitingOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<RequestHardeningOptions>()
    .Bind(builder.Configuration.GetSection(RequestHardeningOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<OpsOptions>()
    .Bind(builder.Configuration.GetSection(OpsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<OutboxRecoveryOptions>()
    .Bind(builder.Configuration.GetSection(OutboxRecoveryOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AiGovernanceOptions>()
    .Bind(builder.Configuration.GetSection(AiGovernanceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AiStreamingOptions>()
    .Bind(builder.Configuration.GetSection(AiStreamingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AiModelOrchestrationOptions>()
    .Bind(builder.Configuration.GetSection(AiModelOrchestrationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AiOrchestrationOptions>()
    .Bind(builder.Configuration.GetSection(AiOrchestrationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<SemanticCacheOptions>()
    .Bind(builder.Configuration.GetSection(SemanticCacheOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<RagOptions>()
    .Bind(builder.Configuration.GetSection(RagOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<ConversationOptions>()
    .Bind(builder.Configuration.GetSection(ConversationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<FeatureFlagStoreOptions>()
    .Bind(builder.Configuration.GetSection(FeatureFlagStoreOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AiEvaluationOptions>()
    .Bind(builder.Configuration.GetSection(AiEvaluationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<OpsSecurityOptions>()
    .Bind(builder.Configuration.GetSection(OpsSecurityOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<LoadTestHooksOptions>()
    .Bind(builder.Configuration.GetSection(LoadTestHooksOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IClientContextAccessor, HttpClientContextAccessor>();
builder.Services.AddSingleton<IRateLimitTelemetry, RateLimitTelemetry>();
builder.Services.AddSingleton<IOperationalSimulationState, OperationalSimulationState>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<MongoDbHealthCheck>("mongodb", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"])
    .AddCheck<AiProviderHealthCheck>("ai-provider", tags: ["ready"]);
builder.Services.AddSingleton<IExceptionHandler, ArgumentExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, UnauthorizedAccessExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, InvalidOperationExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, NotFoundExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, ValidationExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, RequestTimeoutExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, UnhandledExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = false;
    });
builder.Services.AddAuthorization();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var securityOptions = builder.Configuration.GetSection(OpsSecurityOptions.SectionName).Get<OpsSecurityOptions>() ?? new OpsSecurityOptions();
        options.Authority = securityOptions.JwtAuthority;
        options.Audience = securityOptions.JwtAudience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(securityOptions.JwtAuthority),
            ValidateAudience = !string.IsNullOrWhiteSpace(securityOptions.JwtAudience),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });
var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}
app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<EndpointMetricsMiddleware>();
app.UseMiddleware<RequestProfilingMiddleware>();
app.UseMiddleware<RequestSizeLimitMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors("AllowNetlifyApp");
app.UseResponseCompression();
app.UseOutputCache();
app.UseMiddleware<RedisRateLimitingMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
var httpsUrlConfigured = builder.Configuration.GetSection("Kestrel").Exists() ||
                         (builder.Configuration["ASPNETCORE_URLS"]?.Contains("https://") ?? false);
if (httpsUrlConfigured)
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = async (httpContext, report) =>
    {
        httpContext.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description
            })
        };
        await httpContext.Response.WriteAsJsonAsync(payload).ConfigureAwait(false);
    }
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapControllers();
app.Run();
static void ValidateCriticalConfiguration(IConfiguration configuration, IHostEnvironment environment)
{
    if (!environment.IsProduction())
    {
        return;
    }
    var mongoConn = FirstNonEmpty(
        Environment.GetEnvironmentVariable("MONGODB_CONNECTIONSTRING"),
        configuration["MongoDB:ConnectionString"]);
    var mongoDb = FirstNonEmpty(
        Environment.GetEnvironmentVariable("MONGODB_DATABASE"),
        configuration["MongoDB:DatabaseName"]);
    if (string.IsNullOrWhiteSpace(mongoConn) || string.IsNullOrWhiteSpace(mongoDb))
    {
        throw new InvalidOperationException("MongoDB configuration is required in production.");
    }
    var flags = configuration.GetSection(FeatureFlagsOptions.SectionName).Get<FeatureFlagsOptions>() ?? new FeatureFlagsOptions();
    if (flags.EnableCache)
    {
        var redisConn = FirstNonEmpty(
            Environment.GetEnvironmentVariable("REDIS__CONNECTIONSTRING"),
            configuration["Redis:ConnectionString"]);
        if (string.IsNullOrWhiteSpace(redisConn))
        {
            throw new InvalidOperationException("Redis configuration is required when cache feature is enabled in production.");
        }
        if (!redisConn.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Redis connection must use rediss:// in production.");
        }
    }
    if (flags.EnableRabbitMqPublishing)
    {
        var rabbit = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        if (!rabbit.Enabled || string.IsNullOrWhiteSpace(rabbit.HostName) || string.IsNullOrWhiteSpace(rabbit.ExchangeName))
        {
            throw new InvalidOperationException("RabbitMQ configuration is invalid for production publishing.");
        }
    }
    var aiGovernance = configuration.GetSection(AiGovernanceOptions.SectionName).Get<AiGovernanceOptions>() ?? new AiGovernanceOptions();
    if (aiGovernance.Enabled)
    {
        if (aiGovernance.MaxTokensPerRequest <= 0 || aiGovernance.MaxRequestsPerMinutePerIp <= 0)
        {
            throw new InvalidOperationException("AI governance limits must be positive in production.");
        }
    }
    static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
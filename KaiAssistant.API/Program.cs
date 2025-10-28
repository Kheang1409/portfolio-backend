using KaiAssistant.API.Middleware;
using OpenTelemetry.Metrics;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Infrastructure.Persistence;
using KaiAssistant.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithMetrics(mb => mb
        .AddMeter("KaiAssistant.AssistantService")
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddConfiguredCors(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddControllers();

var app = builder.Build();

var assistantService = app.Services.GetRequiredService<IAssistantService>();
await assistantService.LoadResumeFromDatabaseAsync();
app.MapPrometheusScrapingEndpoint();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowNetlifyApp");

app.UseMiddleware<GlobalExceptionMiddleware>();
var httpsUrlConfigured = builder.Configuration.GetSection("Kestrel").Exists() ||
                         (builder.Configuration["ASPNETCORE_URLS"]?.Contains("https://") ?? false);
if (httpsUrlConfigured)
{
    app.UseHttpsRedirection();
}

app.MapControllers();
app.Run();
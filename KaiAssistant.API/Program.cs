using Amazon.Lambda.AspNetCoreServer;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using KaiAssistant.API.Extensions;
using KaiAssistant.API.Middleware;
using KaiAssistant.Application.Extensions;
using KaiAssistant.Infrastructure.Persistence;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// Enable Lambda runtime integration for API Gateway HTTP API; keeps controllers intact.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

builder.Services.AddOpenTelemetry()
    .WithMetrics(mb => mb
        .AddMeter("KaiAssistant.AssistantService")
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());


builder.Services.AddApplicationServices();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddConfiguredCors(builder.Configuration);

builder.Services.AddSingleton<IExceptionHandler, ArgumentExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, UnauthorizedAccessExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, InvalidOperationExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, NotFoundExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, RequestTimeoutExceptionHandler>();
builder.Services.AddSingleton<IExceptionHandler, UnhandledExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

var app = builder.Build();

app.MapPrometheusScrapingEndpoint();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
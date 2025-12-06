using KaiAssistant.API.Middleware;
using OpenTelemetry.Metrics;
using KaiAssistant.Application.Extensions;
using KaiAssistant.Infrastructure.Persistence;
using KaiAssistant.API.Extensions;

var builder = WebApplication.CreateBuilder(args);

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
using KaiAssistant.Api.Middleware;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowNetlifyApp", policy =>
    {
        policy.WithOrigins("https://kaitaing.netlify.app")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddControllers();

var app = builder.Build();

var resumePath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "kai_taing_resume.json");
var assistantService = app.Services.GetRequiredService<IAssistantService>();
assistantService.LoadResume(resumePath);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseHttpsRedirection();

app.UseCors("AllowNetlifyApp"); 



app.MapControllers();
app.Run();


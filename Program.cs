using ContactFormApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Define CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowNetlifyApp", policy =>
    {
        policy.WithOrigins("https://kaitaing.netlify.app")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Register services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks(); // 👈 Add this
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors("AllowNetlifyApp"); // 👈 Apply the CORS policy
app.UseAuthorization();
app.MapControllers();

// Expose health check endpoint
app.MapHealthChecks("/health"); // 👈 Add this too

app.Run();
